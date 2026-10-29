using GpsTrainer;
using GpsTrainer.Server.Data;
using GpsTrainer.Server.Loops;
using GpsTrainer.Server.Routing;
using GpsTrainer.Server.Security;

namespace GpsTrainer.Server.Api;

public static partial class GpsEndpoints
{
    private static void MapRouting(RouteGroupBuilder api)
    {
        api.MapPost("/routing/directions", async (
            RouteRequestDto body,
            GpsTrainerDb db,
            IConfiguration config,
            IRoutingProviderFactory providers,
            RoutingSettingsStore store,
            AnonymousRoutingLimiter limiter,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            if (!AllowAnonymous(http, config, limiter))
                return TooManyRoads();

            var resolved = await store.ResolveAsync(db, config, cancellationToken);
            var failure = await RouteWithProvider(body, resolved, providers, cancellationToken);
            return Results.Ok(failure);
        });

        api.MapPost("/routing/elevation", async (
            ElevationLineRequestDto body,
            IConfiguration config,
            IElevationProvider elevation,
            AnonymousRoutingLimiter limiter,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            if (!AllowAnonymous(http, config, limiter))
                return TooManyElevation();

            var requested = body?.Points ?? [];
            var found = await elevation.ElevationLineAsync(
                requested.Select(point => (Longitude: point.Longitude, Latitude: point.Latitude)).ToList(),
                cancellationToken);
            return Results.Ok(WithElapsed(requested, found));
        });

        api.MapPost("/routing/loops", async (
            LoopGenerateRequestDto body,
            LoopGenerationService loops,
            GpsTrainerDb db,
            IConfiguration config,
            RoutingSettingsStore store,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            if (!LoopRequests.TryParse(body, out var request, out var error))
                return Results.BadRequest(new LoopGenerateResponseDto { Message = error, Attribution = LoopNetworkFile.AttributionText });

            var signedIn = IsSignedIn(http, config);
            string? apiKey = null;
            if (signedIn)
            {
                var resolved = await store.ResolveAsync(db, config, cancellationToken);
                apiKey = resolved.ApiKey;
            }

            var generated = await loops.GenerateAsync(request, signedIn, apiKey, cancellationToken);
            return Results.Ok(generated);
        });

        api.MapPost("/routing/geocode", async (
            GeocodeRequestDto body,
            GpsTrainerDb db,
            IConfiguration config,
            IRoutingProviderFactory providers,
            RoutingSettingsStore store,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            if (!IsSignedIn(http, config))
                return Results.Unauthorized();

            var resolved = await store.ResolveAsync(db, config, cancellationToken);
            if (!string.Equals(resolved.Provider, OpenRouteServiceRoutingProvider.ProviderName, StringComparison.OrdinalIgnoreCase))
                return Results.Ok(new GeocodeResponseDto { Message = "Geocoding provider is not available." });
            if (string.IsNullOrWhiteSpace(resolved.ApiKey))
                return Results.Ok(new GeocodeResponseDto { Message = "Geocoding is not configured." });

            var provider = providers.Get(resolved.Provider);
            if (provider is null)
                return Results.Ok(new GeocodeResponseDto { Message = "Geocoding provider is not available." });

            var found = await provider.GeocodeAsync(body.Address ?? "", resolved.ApiKey, cancellationToken);
            return Results.Ok(found);
        });

        api.MapPut("/users/{login}/location", async (
            string login,
            UserLocationDto body,
            GpsTrainerDb db,
            HttpContext http,
            IConfiguration config) =>
        {
            if (!CanReadUser(http, config, login))
                return Results.Unauthorized();
            if (!RoutingLimits.IsPosition(body.Latitude, body.Longitude))
                return Results.BadRequest(new { message = "Those coordinates are not on the map." });
            var user = await db.Users.FindAsync(login);
            if (user is null)
                return Results.NotFound();
            user.Latitude = body.Latitude;
            user.Longitude = body.Longitude;
            user.UserTimestamp = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(ToProfile(user));
        });

        api.MapGet("/admin/routing", async (
            GpsTrainerDb db,
            IConfiguration config,
            RoutingSettingsStore store,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            if (!IsAdmin(http, config))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var resolved = await store.ResolveAsync(db, config, cancellationToken);
            return Results.Ok(store.Describe(resolved));
        });

        api.MapPut("/admin/routing", async (
            RoutingSettingsWriteDto body,
            GpsTrainerDb db,
            IConfiguration config,
            RoutingSettingsStore store,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            if (!IsAdmin(http, config))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var error = await store.SaveAsync(db, body, cancellationToken);
            if (error is not null)
                return Results.BadRequest(new { message = error });
            var resolved = await store.ResolveAsync(db, config, cancellationToken);
            return Results.Ok(store.Describe(resolved));
        });
    }

    private static async Task<RouteResponseDto> RouteWithProvider(
        RouteRequestDto body,
        ResolvedRouting resolved,
        IRoutingProviderFactory providers,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(resolved.Provider, OpenRouteServiceRoutingProvider.ProviderName, StringComparison.OrdinalIgnoreCase))
            return RouteResponseDto.Fail("Routing provider is not available.");
        if (string.IsNullOrWhiteSpace(resolved.ApiKey))
            return RouteResponseDto.Fail("Routing is not configured.");

        var provider = providers.Get(resolved.Provider);
        if (provider is null)
            return RouteResponseDto.Fail("Routing provider is not available.");

        return await provider.RouteAsync(body, resolved.ApiKey, cancellationToken);
    }

    private static ElevationLineResponseDto WithElapsed(
        IReadOnlyList<ElevationSampleDto> requested,
        ElevationLineResponseDto response)
    {
        if (!response.Found || response.Points.Count != requested.Count)
            return response;
        for (var i = 0; i < requested.Count; i++)
            response.Points[i].ElapsedKm = requested[i].ElapsedKm;
        return response;
    }

    private static bool IsSignedIn(HttpContext http, IConfiguration config) =>
        TryLogin(http, config, out _);

    private static bool AllowAnonymous(HttpContext http, IConfiguration config, AnonymousRoutingLimiter limiter) =>
        IsSignedIn(http, config) || limiter.TryAcquire(AnonymousRoutingLimiter.ClientAddress(http));

    private static IResult TooManyRoads() =>
        Results.Json(
            RouteResponseDto.Fail("Too many road requests from this network. Wait a minute and try again.", straightFallback: false),
            statusCode: StatusCodes.Status429TooManyRequests);

    private static IResult TooManyElevation() =>
        Results.Json(
            new ElevationLineResponseDto { Message = "Too many road requests from this network. Wait a minute and try again." },
            statusCode: StatusCodes.Status429TooManyRequests);
}
