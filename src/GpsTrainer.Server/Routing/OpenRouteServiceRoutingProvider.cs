using System.Text.Json;
using System.Text.Json.Serialization;
using GpsTrainer;

namespace GpsTrainer.Server.Routing;

public interface IRoutingProvider
{
    string Name { get; }
    Task<RouteResponseDto> RouteAsync(RouteRequestDto request, string apiKey, CancellationToken cancellationToken);
    Task<GeocodeResponseDto> GeocodeAsync(string address, string apiKey, CancellationToken cancellationToken);
    Task<ElevationLineResponseDto> ElevationLineAsync(ElevationLineRequestDto request, string apiKey, CancellationToken cancellationToken);
}

public interface IRoutingProviderFactory
{
    IRoutingProvider? Get(string name);
    IReadOnlyList<string> Names { get; }
}

public sealed class RoutingProviderFactory : IRoutingProviderFactory
{
    private readonly IReadOnlyDictionary<string, IRoutingProvider> _providers;

    public RoutingProviderFactory(IEnumerable<IRoutingProvider> providers)
    {
        _providers = providers.ToDictionary(provider => provider.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> Names { get; } = [OpenRouteServiceRoutingProvider.ProviderName];

    public IRoutingProvider? Get(string name) =>
        _providers.TryGetValue(name, out var provider) ? provider : null;
}

public sealed class OpenRouteServiceRoutingProvider : IRoutingProvider
{
    public const string ProviderName = "OpenRouteService";

    public const int MaxElevationPoints = 300;

    private readonly IOpenRouteServiceClient _client;
    private readonly ElevationLineCache _cache;
    private readonly OpenRouteServiceEndpoints _endpoints;

    public OpenRouteServiceRoutingProvider(
        IOpenRouteServiceClient client,
        ElevationLineCache? cache = null,
        OpenRouteServiceEndpoints? endpoints = null)
    {
        _client = client;
        _cache = cache ?? new ElevationLineCache();
        _endpoints = endpoints ?? OpenRouteServiceEndpoints.Defaults;
    }

    public string Name => ProviderName;

    public async Task<RouteResponseDto> RouteAsync(RouteRequestDto request, string apiKey, CancellationToken cancellationToken)
    {
        var rejected = RoutingLimits.RejectSegment(request.FromLatitude, request.FromLongitude, request.ToLatitude, request.ToLongitude);
        if (rejected is not null)
            return Scrub(RouteResponseDto.Fail(rejected, RoutingLimits.AllowsStraightFallback(rejected)), apiKey);

        var profile = OrsProfile.ForActivity(request.Activity);
        if (!OrsProfile.IsAllowed(profile))
            return RouteResponseDto.Fail("Routing failed.");

        var body = JsonSerializer.Serialize(new OrsDirectionsBody
        {
            Coordinates =
            [
                [request.FromLongitude, request.FromLatitude],
                [request.ToLongitude, request.ToLatitude]
            ],
            Radiuses = SnapRadiuses(request.SnapRadiusMeters)
        });

        OrsHttpResult result;
        try
        {
            result = await _client.PostJsonAsync(_endpoints.Directions(profile), body, apiKey, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return RouteResponseDto.Fail("Routing failed.");
        }
        catch (TaskCanceledException)
        {
            return RouteResponseDto.Fail("Routing timed out.");
        }

        if (string.IsNullOrWhiteSpace(result.Body))
            return RouteResponseDto.Fail(result.StatusCode is >= 200 and < 300 ? "No route found." : "Routing failed.");

        var parsed = OrsGeoJson.ParseRoute(result.Body, request.FromLatitude, request.FromLongitude);
        if (!parsed.Routed && result.StatusCode is < 200 or >= 300 && string.IsNullOrWhiteSpace(parsed.Message))
            parsed.Message = "Routing failed.";
        return Scrub(parsed, apiKey);
    }

    public async Task<GeocodeResponseDto> GeocodeAsync(string address, string apiKey, CancellationToken cancellationToken)
    {
        var trimmed = address.Trim();
        if (trimmed.Length == 0)
            return new GeocodeResponseDto { Message = "Enter an address." };
        if (trimmed.Length > RoutingLimits.MaxAddressLength)
            return new GeocodeResponseDto { Message = "That address is too long." };

        OrsHttpResult result;
        try
        {
            result = await _client.GetAsync(_endpoints.Geocode(trimmed), apiKey, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return new GeocodeResponseDto { Message = "Geocoding failed." };
        }
        catch (TaskCanceledException)
        {
            return new GeocodeResponseDto { Message = "Geocoding timed out." };
        }

        if (string.IsNullOrWhiteSpace(result.Body))
            return new GeocodeResponseDto { Message = "No location found." };
        var parsed = OrsGeoJson.ParseGeocode(result.Body);
        if (!string.IsNullOrEmpty(apiKey) && parsed.Message.Contains(apiKey, StringComparison.Ordinal))
            parsed.Message = "Geocoding failed.";
        return parsed;
    }

    /// <summary>
    /// One elevation line request. The free plan lists 2000 elevation requests per day and 40 per minute.
    /// </summary>
    public async Task<ElevationLineResponseDto> ElevationLineAsync(
        ElevationLineRequestDto request, string apiKey, CancellationToken cancellationToken)
    {
        var points = request?.Points ?? [];
        if (points.Count == 0)
            return ElevationFail("No elevation was found.");
        if (points.Count > MaxElevationPoints)
            return ElevationFail("That elevation line is too long.");
        foreach (var point in points)
        {
            if (!RoutingLimits.IsPosition(point.Latitude, point.Longitude))
                return ElevationFail("Those coordinates are not on the map.");
        }

        var cacheKey = _cache.KeyFor(points);
        if (_cache.TryGet(cacheKey, out var cached))
            return ElevationResult(points, cached, fromCache: true);

        var body = JsonSerializer.Serialize(new OrsElevationBody
        {
            Geometry = points.Select(point => new[] { point.Longitude, point.Latitude }).ToArray()
        });

        OrsHttpResult result;
        try
        {
            result = await _client.PostJsonAsync(_endpoints.ElevationLine(), body, apiKey, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return ElevationFail("Elevation lookup failed.");
        }
        catch (TaskCanceledException)
        {
            return ElevationFail("Elevation lookup timed out.");
        }

        if (result.StatusCode is < 200 or >= 300)
            return Scrub(ElevationFail(ElevationMessage(result.Body)), apiKey);

        var parsed = OrsGeoJson.ParseElevation(result.Body);
        if (!parsed.Ok || parsed.Meters.Length != points.Count)
            return Scrub(ElevationFail(string.IsNullOrWhiteSpace(parsed.Message) ? "Elevation lookup failed." : parsed.Message), apiKey);

        var kilometers = parsed.Meters.Select(meters => meters / 1000.0).ToArray();
        _cache.Store(cacheKey, kilometers);
        return Scrub(ElevationResult(points, kilometers, fromCache: false), apiKey);
    }

    /// <summary>
    /// ORS searches this many meters for a road at each end. A missing radius
    /// keeps the service default. Values outside the close-snap limit are ignored.
    /// </summary>
    private static double[]? SnapRadiuses(double? meters)
    {
        if (meters is null || meters is < 1 or > LoopClose.CloseSnapMeters)
            return null;
        return [meters.Value, meters.Value];
    }

    private static RouteResponseDto Scrub(RouteResponseDto response, string apiKey)
    {
        if (!string.IsNullOrEmpty(apiKey) && response.Message.Contains(apiKey, StringComparison.Ordinal))
            response.Message = "Routing failed.";
        return response;
    }

    private static ElevationLineResponseDto ElevationResult(IReadOnlyList<ElevationSampleDto> requested, double[] altitudeKm, bool fromCache)
    {
        var points = new List<ElevationSampleDto>(requested.Count);
        for (var i = 0; i < requested.Count; i++)
        {
            var source = requested[i];
            points.Add(new ElevationSampleDto
            {
                Latitude = source.Latitude,
                Longitude = source.Longitude,
                ElapsedKm = source.ElapsedKm,
                AltitudeKm = i < altitudeKm.Length && SummitSearch.IsKnown(altitudeKm[i])
                    ? altitudeKm[i]
                    : Altitude.Unknown.Kilometers
            });
        }
        return new ElevationLineResponseDto { Found = true, FromCache = fromCache, Points = points };
    }

    private static ElevationLineResponseDto ElevationFail(string message) => new() { Message = message };

    private static string ElevationMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "Elevation lookup failed.";
        var parsed = OrsGeoJson.ParseElevation(body);
        return string.IsNullOrWhiteSpace(parsed.Message) ? "Elevation lookup failed." : parsed.Message;
    }

    private static ElevationLineResponseDto Scrub(ElevationLineResponseDto response, string apiKey)
    {
        if (!string.IsNullOrEmpty(apiKey) && response.Message.Contains(apiKey, StringComparison.Ordinal))
            response.Message = "Elevation lookup failed.";
        return response;
    }

    private sealed class OrsElevationBody
    {
        [JsonPropertyName("format_in")]
        public string FormatIn { get; set; } = "polyline";

        [JsonPropertyName("format_out")]
        public string FormatOut { get; set; } = "polyline";

        [JsonPropertyName("geometry")]
        public double[][] Geometry { get; set; } = [];
    }

    private sealed class OrsDirectionsBody
    {
        [JsonPropertyName("coordinates")]
        public double[][] Coordinates { get; set; } = [];

        [JsonPropertyName("instructions")]
        public bool Instructions { get; set; } = true;

        [JsonPropertyName("instructions_format")]
        public string InstructionsFormat { get; set; } = "text";

        [JsonPropertyName("elevation")]
        public bool Elevation { get; set; } = true;

        [JsonPropertyName("radiuses")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double[]? Radiuses { get; set; }
    }
}
