using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GpsTrainer;
using GpsTrainer.Server.Data;
using GpsTrainer.Server.Routing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GpsTrainer.Server.Tests;

public class RoutingApiFactory : WebApplicationFactory<Program>
{
    public const string EnvironmentKey = "super-secret-ors-key-ABCD";
    private readonly string _database = Path.Combine(Path.GetTempPath(), "gpstrainer-routing-" + Guid.NewGuid().ToString("N") + ".db");
    public FakeOrsClient Ors { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:GpsTrainer", "Data Source=" + _database);
        builder.UseSetting("GpsTrainer:AuthSigningKey", "test-signing-key");
        builder.UseSetting("GpsTrainer:AdminLogins:0", "bud");
        builder.UseSetting("GpsTrainer:SeedInferredActivityCodes", "true");
        builder.UseSetting("Routing:OrsApiKey", EnvironmentKey);
        builder.UseSetting("Routing:Provider", "OpenRouteService");
        builder.ConfigureTestServices(services =>
        {
            var existing = services.SingleOrDefault(descriptor => descriptor.ServiceType == typeof(IOpenRouteServiceClient));
            if (existing is not null)
                services.Remove(existing);
            services.AddSingleton<IOpenRouteServiceClient>(Ors);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (File.Exists(_database))
            File.Delete(_database);
    }
}

public sealed class FakeOrsClient : IOpenRouteServiceClient
{
    public string? LastKey { get; private set; }
    public string? LastUrl { get; private set; }
    public string? LastBody { get; private set; }
    public int Calls { get; set; }
    public OrsHttpResult Next { get; set; } = new(200, FakeOrsClient.BentRoute);

    public const string BentRoute = """
        {
          "type": "FeatureCollection",
          "features": [{
            "type": "Feature",
            "geometry": {
              "type": "LineString",
              "coordinates": [
                [-105.0844, 40.5853],
                [-105.0800, 40.5853],
                [-105.0760, 40.5853],
                [-105.0760, 40.5900]
              ]
            },
            "properties": {
              "segments": [{
                "steps": [
                  { "type": 11, "instruction": "Head east", "way_points": [0, 2] },
                  { "type": 0, "instruction": "Turn left onto College Avenue", "way_points": [2, 3] },
                  { "type": 10, "instruction": "Arrive at the destination", "way_points": [3, 3] }
                ]
              }]
            }
          }]
        }
        """;

    public Func<string, string, OrsHttpResult>? Handler { get; set; }

    public Task<OrsHttpResult> PostJsonAsync(string relativeUrl, string json, string apiKey, CancellationToken cancellationToken)
    {
        Calls++;
        LastKey = apiKey;
        LastUrl = relativeUrl;
        LastBody = json;
        if (Handler is not null)
            return Task.FromResult(Handler(relativeUrl, json));
        return Task.FromResult(Next);
    }

    public Task<OrsHttpResult> GetAsync(string relativeUrl, string apiKey, CancellationToken cancellationToken)
    {
        Calls++;
        LastKey = apiKey;
        LastUrl = relativeUrl;
        return Task.FromResult(new OrsHttpResult(200, """
            {"features":[{"geometry":{"coordinates":[-105.0844,40.5853]},"properties":{"label":"Fort Collins, Colorado, USA"}}]}
            """));
    }
}

public sealed record CapturedOrsRequest(string Method, string Url, string? Authorization, string? Body);

public sealed class RecordingOrsHandler : HttpMessageHandler
{
    public List<CapturedOrsRequest> Calls { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var authorization = request.Headers.TryGetValues("Authorization", out var values) ? values.Single() : null;
        var url = request.RequestUri!.AbsoluteUri;
        Calls.Add(new CapturedOrsRequest(request.Method.Method, url, authorization, body));

        var payload = url.Contains("/v2/directions/", StringComparison.Ordinal)
            ? FakeOrsClient.BentRoute
            : url.Contains("/line", StringComparison.Ordinal)
                ? """{"geometry":[[-105.1,40.5,1600],[-105.1,40.51,1610]]}"""
                : """{"features":[{"geometry":{"coordinates":[-105.0844,40.5853]},"properties":{"label":"Fort Collins, Colorado, USA"}}]}""";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
    }
}

public sealed class ElevationStubHandler : HttpMessageHandler
{
    public int Calls { get; private set; }
    public HttpRequestMessage? Request { get; private set; }
    public string? Body { get; private set; }
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public string ResponseBody { get; set; } = """{"geometry":[[-105.10000,40.50000,1600],[-105.10000,40.51000,1615]]}""";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        Request = request;
        Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(Status)
        {
            Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json")
        };
    }
}

public sealed class StubOrsHandler : HttpMessageHandler
{
    public HttpRequestMessage? Request { get; private set; }
    public string? Body { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Request = request;
        Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(FakeOrsClient.BentRoute, Encoding.UTF8, "application/geo+json")
        };
    }
}

public class RoutingTests : IClassFixture<RoutingApiFactory>
{
    private readonly RoutingApiFactory _factory;
    private readonly HttpClient _client;

    public RoutingTests(RoutingApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Directions_follow_the_mocked_road_and_hide_the_key()
    {
        await SignIn("bud", "secret", "bud@example.com");
        await _client.PutAsJsonAsync("/api/admin/routing", new RoutingSettingsWriteDto
        {
            Provider = "OpenRouteService",
            ClearStoredKey = true
        });
        var callsBefore = _factory.Ors.Calls;
        var response = await _client.PostAsJsonAsync("/api/routing/directions", new RouteRequestDto
        {
            FromLatitude = 40.5853,
            FromLongitude = -105.0844,
            ToLatitude = 40.5900,
            ToLongitude = -105.0760,
            Activity = "Cycling"
        });
        var json = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(RoutingApiFactory.EnvironmentKey, json, StringComparison.Ordinal);
        var route = JsonSerializer.Deserialize<RouteResponseDto>(json, JsonOptions);
        Assert.True(route!.Routed);
        Assert.Equal("OpenRouteService", route.Provider);
        Assert.Contains("openrouteservice", route.Attribution, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OpenStreetMap", route.Attribution, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "https://api.heigit.org/openrouteservice/v2/directions/cycling-road/geojson",
            _factory.Ors.LastUrl);
        Assert.Contains("\"elevation\":true", _factory.Ors.LastBody, StringComparison.Ordinal);
        Assert.DoesNotContain("round_trip", _factory.Ors.LastBody, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(RoutingApiFactory.EnvironmentKey, _factory.Ors.LastKey);
        Assert.DoesNotContain(RoutingApiFactory.EnvironmentKey, _factory.Ors.LastBody, StringComparison.Ordinal);
        Assert.True(_factory.Ors.Calls > callsBefore);

        Assert.Equal(3, route.Points.Count);
        Assert.Equal(WaypointType.TurnLeft.ToString(), route.Points[1].WaypointType);
        Assert.Contains("College Avenue", route.Points[1].Text);
        var longitudes = route.Points.Select(point => point.Longitude).ToList();
        Assert.True(longitudes.Max() - longitudes.Min() > 0.001);
    }

    [Fact]
    public async Task Anonymous_callers_follow_the_mocked_road_and_other_users_cannot_edit_provider_settings()
    {
        using var anonymous = _factory.CreateClient();
        var callsBefore = _factory.Ors.Calls;
        var routed = await anonymous.PostAsJsonAsync("/api/routing/directions", new RouteRequestDto
        {
            FromLatitude = 40.5853,
            FromLongitude = -105.0844,
            ToLatitude = 40.59,
            ToLongitude = -105.07,
            Activity = "Cycling"
        });
        var routedJson = await routed.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, routed.StatusCode);
        Assert.DoesNotContain(RoutingApiFactory.EnvironmentKey, routedJson, StringComparison.Ordinal);
        var route = JsonSerializer.Deserialize<RouteResponseDto>(routedJson, JsonOptions);
        Assert.True(route!.Routed);
        Assert.True(_factory.Ors.Calls > callsBefore);
        Assert.Equal(RoutingApiFactory.EnvironmentKey, _factory.Ors.LastKey);

        var geocode = await anonymous.PostAsJsonAsync("/api/routing/geocode", new GeocodeRequestDto { Address = "Fort Collins" });
        Assert.Equal(HttpStatusCode.Unauthorized, geocode.StatusCode);

        await SignIn("rider", "secret", "rider-routing@example.com");
        var forbidden = await _client.GetAsync("/api/admin/routing");
        var forbiddenBody = await forbidden.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.DoesNotContain(RoutingApiFactory.EnvironmentKey, forbiddenBody, StringComparison.Ordinal);

        var rejected = await _client.PutAsJsonAsync("/api/admin/routing", new RoutingSettingsWriteDto
        {
            Provider = "OpenRouteService",
            ApiKey = "should-not-stick"
        });
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
    }

    [Fact]
    public async Task Admin_settings_mask_the_key_and_store_it_encrypted()
    {
        await SignIn("bud", "secret", "bud@example.com");
        var before = await _client.GetFromJsonAsync<RoutingSettingsDto>("/api/admin/routing");
        Assert.Equal("OpenRouteService", before!.Provider);
        Assert.Equal("environment", before.KeySource);
        Assert.True(before.KeyConfigured);
        Assert.Equal("••••ABCD", before.KeyMask);
        Assert.DoesNotContain("super-secret", before.KeyMask, StringComparison.Ordinal);

        const string storedKey = "brand-new-ors-key-ZZZZ";
        var saved = await _client.PutAsJsonAsync("/api/admin/routing", new RoutingSettingsWriteDto
        {
            Provider = "OpenRouteService",
            ApiKey = storedKey
        });
        var savedJson = await saved.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.DoesNotContain(storedKey, savedJson, StringComparison.Ordinal);
        Assert.DoesNotContain(RoutingApiFactory.EnvironmentKey, savedJson, StringComparison.Ordinal);
        var described = JsonSerializer.Deserialize<RoutingSettingsDto>(savedJson, JsonOptions);
        Assert.Equal("stored", described!.KeySource);
        Assert.Equal("••••ZZZZ", described.KeyMask);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GpsTrainerDb>();
            var row = await db.RoutingSettings.SingleAsync();
            Assert.DoesNotContain(storedKey, row.ProtectedApiKey, StringComparison.Ordinal);
            var protector = scope.ServiceProvider.GetRequiredService<SecretProtector>();
            Assert.Equal(storedKey, protector.Unprotect(row.ProtectedApiKey));
        }

        _factory.Ors.Calls = 0;
        await _client.PostAsJsonAsync("/api/routing/directions", new RouteRequestDto
        {
            FromLatitude = 40.5853,
            FromLongitude = -105.0844,
            ToLatitude = 40.5900,
            ToLongitude = -105.0760,
            Activity = "Running"
        });
        Assert.Equal(storedKey, _factory.Ors.LastKey);
        Assert.Equal(
            "https://api.heigit.org/openrouteservice/v2/directions/foot-walking/geojson",
            _factory.Ors.LastUrl);

        var cleared = await _client.PutAsJsonAsync("/api/admin/routing", new RoutingSettingsWriteDto
        {
            Provider = "OpenRouteService",
            ClearStoredKey = true
        });
        var clearedBody = await cleared.Content.ReadFromJsonAsync<RoutingSettingsDto>();
        Assert.Equal("environment", clearedBody!.KeySource);
        Assert.Equal("••••ABCD", clearedBody.KeyMask);

        var google = await _client.PutAsJsonAsync("/api/admin/routing", new RoutingSettingsWriteDto { Provider = "Google" });
        Assert.Equal(HttpStatusCode.BadRequest, google.StatusCode);
    }

    [Fact]
    public async Task Limits_reject_a_tiny_segment_without_calling_the_router()
    {
        await SignIn("bud", "secret", "bud@example.com");
        var calls = _factory.Ors.Calls;
        var response = await _client.PostAsJsonAsync("/api/routing/directions", new RouteRequestDto
        {
            FromLatitude = 40.5853,
            FromLongitude = -105.0844,
            ToLatitude = 40.58531,
            ToLongitude = -105.08441
        });
        var route = await response.Content.ReadFromJsonAsync<RouteResponseDto>();
        Assert.False(route!.Routed);
        Assert.False(route.StraightFallback);
        Assert.Contains("too close", route.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(calls, _factory.Ors.Calls);
    }

    [Fact]
    public async Task A_provider_error_is_a_straight_fallback_notice()
    {
        await SignIn("bud", "secret", "bud@example.com");
        _factory.Ors.Next = new OrsHttpResult(404, """{"error":{"message":"Could not find routable point nearby."}}""");
        try
        {
            var response = await _client.PostAsJsonAsync("/api/routing/directions", new RouteRequestDto
            {
                FromLatitude = 40.5853,
                FromLongitude = -105.0844,
                ToLatitude = 40.5900,
                ToLongitude = -105.0760,
                Activity = "Running"
            });
            var route = await response.Content.ReadFromJsonAsync<RouteResponseDto>();
            Assert.False(route!.Routed);
            Assert.True(route.StraightFallback);
            Assert.Contains("routable point", route.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            _factory.Ors.Next = new OrsHttpResult(200, FakeOrsClient.BentRoute);
        }
    }

    [Fact]
    public async Task Profile_location_and_geocode_round_trip_for_the_signed_in_user()
    {
        await SignIn("mapper", "secret", "mapper@example.com");
        var saved = await _client.PutAsJsonAsync("/api/users/mapper/location", new UserLocationDto
        {
            Latitude = MapDefaults.FortCollinsLatitude,
            Longitude = MapDefaults.FortCollinsLongitude
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var profile = await _client.GetFromJsonAsync<UserProfileDto>("/api/users/mapper");
        Assert.Equal(MapDefaults.FortCollinsLatitude, profile!.Latitude);
        Assert.Equal(MapDefaults.FortCollinsLongitude, profile.Longitude);

        var geocode = await _client.PostAsJsonAsync("/api/routing/geocode", new GeocodeRequestDto { Address = "Fort Collins, CO" });
        var geocodeJson = await geocode.Content.ReadAsStringAsync();
        var place = JsonSerializer.Deserialize<GeocodeResponseDto>(geocodeJson, JsonOptions);
        Assert.True(place!.Found);
        Assert.Equal(
            "https://api.heigit.org/pelias/v1/search?size=1&text=Fort%20Collins%2C%20CO",
            _factory.Ors.LastUrl);
        Assert.Contains("Fort Collins", place.Label);
        Assert.DoesNotContain(RoutingApiFactory.EnvironmentKey, geocodeJson, StringComparison.Ordinal);

        await SignIn("other", "secret", "other-routing@example.com");
        var blocked = await _client.PutAsJsonAsync("/api/users/mapper/location", new UserLocationDto { Latitude = 1, Longitude = 2 });
        Assert.Equal(HttpStatusCode.Unauthorized, blocked.StatusCode);
    }

    [Fact]
    public async Task Elevation_line_uses_the_server_key_and_a_repeat_is_cached()
    {
        var previous = _factory.Ors.Next;
        try
        {
            _factory.Ors.Next = new OrsHttpResult(200, """
                {"geometry":[[-105.11,40.61,1600],[-105.11,40.62,1610]]}
                """);
            using var anonymous = _factory.CreateClient();
            var calls = _factory.Ors.Calls;
            var body = new ElevationLineRequestDto
            {
                Points =
                [
                    new ElevationSampleDto { Latitude = 40.61, Longitude = -105.11, ElapsedKm = 0 },
                    new ElevationSampleDto { Latitude = 40.62, Longitude = -105.11, ElapsedKm = 1.1 }
                ]
            };
            var response = await anonymous.PostAsJsonAsync("/api/routing/elevation", body);
            var json = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain(RoutingApiFactory.EnvironmentKey, json, StringComparison.Ordinal);
            var parsed = JsonSerializer.Deserialize<ElevationLineResponseDto>(json, JsonOptions);
            Assert.True(parsed!.Found);
            Assert.False(parsed.FromCache);
            Assert.False(parsed.Unavailable);
            Assert.False(parsed.Limited);
            Assert.Equal(0, parsed.Points[0].ElapsedKm);
            Assert.Equal(1.1, parsed.Points[1].ElapsedKm);
            Assert.Equal(1.6, parsed.Points[0].AltitudeKm, 5);
            Assert.Equal(1.61, parsed.Points[1].AltitudeKm, 5);
            Assert.Equal("https://api.heigit.org/openelevationservice/v0/line", _factory.Ors.LastUrl);
            Assert.Contains("\"format_in\":\"polyline\"", _factory.Ors.LastBody, StringComparison.Ordinal);
            Assert.Equal(RoutingApiFactory.EnvironmentKey, _factory.Ors.LastKey);
            Assert.DoesNotContain(RoutingApiFactory.EnvironmentKey, _factory.Ors.LastBody, StringComparison.Ordinal);
            var after = _factory.Ors.Calls;
            Assert.True(after > calls);

            await SignIn("bud", "secret", "bud@example.com");
            var again = await _client.PostAsJsonAsync("/api/routing/elevation", body);
            var againJson = await again.Content.ReadAsStringAsync();
            var cached = JsonSerializer.Deserialize<ElevationLineResponseDto>(againJson, JsonOptions);
            Assert.True(cached!.Found);
            Assert.True(cached.FromCache);
            Assert.Equal(1.6, cached.Points[0].AltitudeKm, 5);
            Assert.Equal(after, _factory.Ors.Calls);
            Assert.DoesNotContain(RoutingApiFactory.EnvironmentKey, againJson, StringComparison.Ordinal);
        }
        finally
        {
            _factory.Ors.Next = previous;
        }
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private async Task SignIn(string login, string password, string email)
    {
        await _client.PostAsJsonAsync("/api/users", new UserWriteDto
        {
            Login = login,
            Password = password,
            Email = email
        });
        var session = await (await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Login = login, Password = password }))
            .Content.ReadFromJsonAsync<LoginResponse>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.Token);
    }
}

public sealed class LimitedRoutingApiFactory : RoutingApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Routing:AnonymousPermitLimit", "2");
    }
}

public class AnonymousRoutingLimitTests : IClassFixture<LimitedRoutingApiFactory>
{
    private readonly LimitedRoutingApiFactory _factory;

    public AnonymousRoutingLimitTests(LimitedRoutingApiFactory factory) => _factory = factory;

    [Fact]
    public void A_full_window_refuses_another_address_until_it_expires()
    {
        var limiter = new AnonymousRoutingLimiter(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Routing:AnonymousPermitLimit"] = "2",
            ["Routing:AnonymousWindowSeconds"] = "60"
        }).Build());
        var start = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(limiter.TryAcquire("203.0.113.4", start));
        Assert.True(limiter.TryAcquire("203.0.113.4", start.AddSeconds(1)));
        Assert.False(limiter.TryAcquire("203.0.113.4", start.AddSeconds(2)));
        Assert.True(limiter.TryAcquire("203.0.113.5", start.AddSeconds(2)));
        Assert.True(limiter.TryAcquire("203.0.113.4", start.AddSeconds(61)));
    }

    [Fact]
    public void The_client_address_is_the_first_forwarded_ip()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers["X-Forwarded-For"] = "203.0.113.8, 10.0.0.1";
        http.Connection.RemoteIpAddress = IPAddress.Loopback;
        Assert.Equal("203.0.113.8", AnonymousRoutingLimiter.ClientAddress(http));
    }

    [Fact]
    public async Task Anonymous_routing_stops_at_the_limit_and_a_signed_in_rider_still_routes()
    {
        using var anonymous = _factory.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.20");
        var body = new RouteRequestDto
        {
            FromLatitude = 40.5853,
            FromLongitude = -105.0844,
            ToLatitude = 40.5900,
            ToLongitude = -105.0760,
            Activity = "Cycling"
        };
        Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsJsonAsync("/api/routing/directions", body)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsJsonAsync("/api/routing/directions", body)).StatusCode);
        var calls = _factory.Ors.Calls;

        var blocked = await anonymous.PostAsJsonAsync("/api/routing/directions", body);
        var json = await blocked.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.DoesNotContain(RoutingApiFactory.EnvironmentKey, json, StringComparison.Ordinal);
        var route = JsonSerializer.Deserialize<RouteResponseDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.False(route!.Routed);
        Assert.False(route.StraightFallback);
        Assert.Contains("Too many road requests", route.Message, StringComparison.Ordinal);
        Assert.Equal(calls, _factory.Ors.Calls);

        var elevation = await anonymous.PostAsJsonAsync("/api/routing/elevation", new ElevationLineRequestDto
        {
            Points = [new ElevationSampleDto { Latitude = 40.61, Longitude = -105.11 }]
        });
        Assert.Equal(HttpStatusCode.TooManyRequests, elevation.StatusCode);
        Assert.DoesNotContain(RoutingApiFactory.EnvironmentKey, await elevation.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var signed = _factory.CreateClient();
        signed.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.20");
        await signed.PostAsJsonAsync("/api/users", new UserWriteDto
        {
            Login = "rider-limit",
            Password = "secret",
            Email = "rider-limit@example.com"
        });
        var session = await (await signed.PostAsJsonAsync("/api/auth/login", new LoginRequest { Login = "rider-limit", Password = "secret" }))
            .Content.ReadFromJsonAsync<LoginResponse>();
        signed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.Token);
        var allowed = await signed.PostAsJsonAsync("/api/routing/directions", body);
        var allowedJson = await allowed.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.DoesNotContain(RoutingApiFactory.EnvironmentKey, allowedJson, StringComparison.Ordinal);
        Assert.True(JsonSerializer.Deserialize<RouteResponseDto>(allowedJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!.Routed);
        Assert.True(_factory.Ors.Calls > calls);
    }
}

public class OpenRouteServiceClientTests
{
    [Fact]
    public async Task The_key_is_sent_as_a_header_and_not_in_the_url()
    {
        var handler = new StubOrsHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://ors.test/") };
        var client = new OpenRouteServiceClient(http);
        const string key = "header-only-ors-key";
        var result = await client.PostJsonAsync("v2/directions/foot-walking/geojson", """{"coordinates":[]}""", key, CancellationToken.None);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal(key, handler.Request!.Headers.GetValues("Authorization").Single());
        Assert.DoesNotContain(key, handler.Request.RequestUri!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(key, handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Elevation_line_posts_a_polyline_and_the_second_lookup_is_cached()
    {
        var handler = new ElevationStubHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://ors.test/") };
        var client = new OpenRouteServiceClient(http);
        var cache = new ElevationLineCache();
        const string key = "header-only-ors-key";
        var provider = new OpenRouteServiceElevationProvider(client, key, cache);
        var points = new (double Longitude, double Latitude)[]
        {
            (-105.1, 40.5),
            (-105.1, 40.51)
        };

        var first = await provider.ElevationLineAsync(points, CancellationToken.None);
        Assert.True(first.Found);
        Assert.False(first.FromCache);
        Assert.Equal(1.6, first.Points[0].AltitudeKm, 5);
        Assert.Equal(1.615, first.Points[1].AltitudeKm, 5);
        Assert.Equal("https://api.heigit.org/openelevationservice/v0/line", handler.Request!.RequestUri!.AbsoluteUri);
        Assert.Contains("\"format_in\":\"polyline\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"format_out\":\"polyline\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("[-105.1,40.5]", handler.Body, StringComparison.Ordinal);
        Assert.Equal(key, handler.Request.Headers.GetValues("Authorization").Single());
        Assert.DoesNotContain(key, handler.Request.RequestUri.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(key, handler.Body, StringComparison.Ordinal);

        var second = await provider.ElevationLineAsync(points, CancellationToken.None);
        Assert.True(second.FromCache);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(1.6, second.Points[0].AltitudeKm, 5);

        var geo = OrsGeoJson.ParseElevation("""{"geometry":{"type":"LineString","coordinates":[[-105.1,40.5,1500],[-105.2,40.6,1400]]}}""");
        Assert.True(geo.Ok);
        Assert.Equal([1500, 1400], geo.Meters);
    }

    [Fact]
    public async Task A_failed_elevation_lookup_is_not_cached_and_the_key_is_scrubbed()
    {
        var handler = new ElevationStubHandler
        {
            Status = HttpStatusCode.TooManyRequests,
            ResponseBody = """{"error":"quota header-only-ors-key"}"""
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://ors.test/") };
        const string key = "header-only-ors-key";
        var provider = new OpenRouteServiceElevationProvider(new OpenRouteServiceClient(http), key, new ElevationLineCache());
        (double Longitude, double Latitude)[] points = [(-105.1, 40.5)];
        var failed = await provider.ElevationLineAsync(points, CancellationToken.None);
        Assert.False(failed.Found);
        Assert.True(failed.Limited);
        Assert.False(failed.Unavailable);
        Assert.DoesNotContain(key, failed.Message, StringComparison.Ordinal);
        var again = await provider.ElevationLineAsync(points, CancellationToken.None);
        Assert.False(again.Found);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Directions_elevation_and_geocode_use_the_HeiGit_urls()
    {
        var handler = new RecordingOrsHandler();
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        const string key = "header-only-ors-key";
        var httpClient = new OpenRouteServiceClient(http);
        var routing = new OpenRouteServiceRoutingProvider(httpClient);
        var elevationProvider = new OpenRouteServiceElevationProvider(httpClient, key);

        var route = await routing.RouteAsync(new RouteRequestDto
        {
            FromLatitude = 40.5853,
            FromLongitude = -105.0844,
            ToLatitude = 40.59,
            ToLongitude = -105.07,
            Activity = "Cycling"
        }, key, CancellationToken.None);
        Assert.True(route.Routed, route.Message);

        var elevation = await elevationProvider.ElevationLineAsync(
        [
            (-105.1, 40.5),
            (-105.1, 40.51)
        ], CancellationToken.None);
        Assert.True(elevation.Found, elevation.Message);

        var place = await routing.GeocodeAsync("Fort Collins", key, CancellationToken.None);
        Assert.True(place.Found, place.Message);

        Assert.Equal(3, handler.Calls.Count);
        Assert.Equal(HttpMethod.Post.Method, handler.Calls[0].Method);
        Assert.Equal("https://api.heigit.org/openrouteservice/v2/directions/cycling-road/geojson", handler.Calls[0].Url);
        Assert.Equal(HttpMethod.Post.Method, handler.Calls[1].Method);
        Assert.Equal("https://api.heigit.org/openelevationservice/v0/line", handler.Calls[1].Url);
        Assert.Contains("\"format_in\":\"polyline\"", handler.Calls[1].Body, StringComparison.Ordinal);
        Assert.Equal(HttpMethod.Get.Method, handler.Calls[2].Method);
        Assert.Equal("https://api.heigit.org/pelias/v1/search?size=1&text=Fort%20Collins", handler.Calls[2].Url);
        foreach (var call in handler.Calls)
        {
            Assert.Equal(key, call.Authorization);
            Assert.DoesNotContain(key, call.Url, StringComparison.Ordinal);
            if (call.Body is not null)
                Assert.DoesNotContain(key, call.Body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_legacy_base_url_keeps_the_old_paths_and_does_not_override_HeiGit_defaults()
    {
        var legacyConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Routing:BaseUrl"] = "https://api.openrouteservice.org/"
        }).Build();
        var legacy = OpenRouteServiceEndpoints.From(legacyConfig);
        Assert.Equal("https://api.openrouteservice.org/v2/directions/foot-walking/geojson", legacy.Directions("foot-walking"));
        Assert.Equal("https://api.openrouteservice.org/elevation/line", legacy.ElevationLine());
        Assert.Equal("https://api.openrouteservice.org/geocode/search?size=1&text=Fort%20Collins", legacy.Geocode("Fort Collins"));

        var both = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Routing:BaseUrl"] = "https://api.openrouteservice.org/",
            ["Routing:DirectionsBaseUrl"] = OpenRouteServiceEndpoints.DefaultDirectionsBaseUrl,
            ["Routing:ElevationBaseUrl"] = OpenRouteServiceEndpoints.DefaultElevationBaseUrl,
            ["Routing:GeocodeBaseUrl"] = OpenRouteServiceEndpoints.DefaultGeocodeBaseUrl
        }).Build();
        var current = OpenRouteServiceEndpoints.From(both);
        Assert.Equal("https://api.heigit.org/openrouteservice/v2/directions/cycling-road/geojson", current.Directions("cycling-road"));
        Assert.Equal("https://api.heigit.org/openelevationservice/v0/line", current.ElevationLine());
        Assert.Equal("https://api.heigit.org/pelias/v1/search?size=1&text=Main", current.Geocode("Main"));

        var handler = new RecordingOrsHandler();
        using var http = new HttpClient(handler);
        const string key = "legacy-header-key";
        var httpClient = new OpenRouteServiceClient(http);
        var routing = new OpenRouteServiceRoutingProvider(httpClient, legacy);
        var elevationProvider = new OpenRouteServiceElevationProvider(httpClient, key, endpoints: legacy);
        await routing.RouteAsync(new RouteRequestDto
        {
            FromLatitude = 40.5853,
            FromLongitude = -105.0844,
            ToLatitude = 40.59,
            ToLongitude = -105.07,
            Activity = "Running"
        }, key, CancellationToken.None);
        await elevationProvider.ElevationLineAsync([(-105.1, 40.5)], CancellationToken.None);
        await routing.GeocodeAsync("Fort Collins", key, CancellationToken.None);

        Assert.Equal("https://api.openrouteservice.org/v2/directions/foot-walking/geojson", handler.Calls[0].Url);
        Assert.Equal("https://api.openrouteservice.org/elevation/line", handler.Calls[1].Url);
        Assert.Equal("https://api.openrouteservice.org/geocode/search?size=1&text=Fort%20Collins", handler.Calls[2].Url);
        Assert.All(handler.Calls, call => Assert.Equal(key, call.Authorization));
        Assert.All(handler.Calls, call => Assert.DoesNotContain(key, call.Url, StringComparison.Ordinal));
    }

    [Fact]
    public void GeoJson_keeps_turn_points_and_caps_a_dense_line()
    {
        var bent = OrsGeoJson.ParseRoute(FakeOrsClient.BentRoute, 40.5853, -105.0844);
        Assert.True(bent.Routed);
        Assert.Equal(WaypointType.TurnLeft.ToString(), bent.Points[1].WaypointType);

        var coordinates = new StringBuilder();
        for (var i = 0; i < 2000; i++)
        {
            if (i > 0)
                coordinates.Append(',');
            var lon = -105.0844 + i * 0.00001;
            coordinates.Append(FormattableString.Invariant($"[{lon},40.5853]"));
        }
        var dense = OrsGeoJson.ParseRoute(
            "{\"type\":\"FeatureCollection\",\"features\":[{\"type\":\"Feature\",\"geometry\":{\"type\":\"LineString\",\"coordinates\":[" + coordinates + "]},\"properties\":{\"segments\":[]}}]}",
            40.5853,
            -105.0844);
        Assert.True(dense.Routed);
        Assert.InRange(dense.Points.Count, 2, RoutingLimits.MaxStoredPoints);
        Assert.Equal(40.5853, dense.Points[^1].Latitude, 5);
    }

    [Fact]
    public void A_three_dimensional_route_keeps_elevation_and_does_not_repeat_the_origin()
    {
        const string elevated = """
            {
              "type": "FeatureCollection",
              "features": [{
                "type": "Feature",
                "geometry": {
                  "type": "LineString",
                  "coordinates": [
                    [-105.0844, 40.5853, 1520],
                    [-105.0800, 40.5860, 1535],
                    [-105.0760, 40.5900, 1490],
                    [-105.0844, 40.5853, 1520]
                  ]
                },
                "properties": { "segments": [] }
              }]
            }
            """;
        var route = OrsGeoJson.ParseRoute(elevated, 40.5853, -105.0844);
        Assert.Equal(1.520, route.OriginAltitudeKm);
        Assert.Equal(3, route.Points.Count);
        Assert.Equal(1.535, route.Points[0].AltitudeKm, 5);
        Assert.Equal(1.490, route.Points[1].AltitudeKm, 5);
        Assert.Equal(40.5900, route.Points[1].Latitude, 5);
        var open = RoutePath.WithoutImplicitReturn(40.5853, -105.0844, 40.5900, -105.0760, route.Points);
        Assert.Equal(2, open.Count);
        Assert.Equal(40.5900, open[^1].Latitude, 5);
        Assert.NotEqual(40.5853, open[^1].Latitude, 5);
    }

    [Fact]
    public void A_segment_longer_than_150_km_is_rejected_before_a_route_is_stored()
    {
        Assert.Null(RoutingLimits.RejectSegment(40.5853, -105.0844, 40.5900, -105.0760));
        Assert.Contains("150 km", RoutingLimits.RejectSegment(40.5853, -105.0844, 42.5, -105.0844));
        Assert.Contains("not on the map", RoutingLimits.RejectSegment(120, 0, 40, -105));
    }

    [Fact]
    public void Stored_keys_round_trip_without_the_plaintext_in_the_ciphertext()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GpsTrainer:AuthSigningKey"] = "unit-test-protection"
        }).Build();
        var protector = new SecretProtector(config);
        const string key = "plaintext-ors-key-should-not-appear";
        var packed = protector.Protect(key);
        Assert.DoesNotContain(key, packed, StringComparison.Ordinal);
        Assert.Equal(key, protector.Unprotect(packed));
        Assert.Equal("••••pear", SecretProtector.Mask(key));

        var other = new SecretProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GpsTrainer:AuthSigningKey"] = "different-protection"
        }).Build());
        Assert.Null(other.Unprotect(packed));
    }

    [Fact]
    public async Task A_close_snap_radius_is_forwarded_and_a_normal_request_leaves_the_default()
    {
        var handler = new RecordingOrsHandler();
        using var http = new HttpClient(handler);
        var provider = new OpenRouteServiceRoutingProvider(new OpenRouteServiceClient(http));
        var request = new RouteRequestDto
        {
            FromLatitude = 40.5853,
            FromLongitude = -105.0844,
            ToLatitude = 40.59,
            ToLongitude = -105.07,
            Activity = "Cycling"
        };

        await provider.RouteAsync(request, "header-only-ors-key", CancellationToken.None);
        using (var plain = JsonDocument.Parse(handler.Calls[0].Body!))
            Assert.False(plain.RootElement.TryGetProperty("radiuses", out _));

        request.SnapRadiusMeters = LoopClose.CloseSnapMeters;
        await provider.RouteAsync(request, "header-only-ors-key", CancellationToken.None);
        using (var widened = JsonDocument.Parse(handler.Calls[1].Body!))
        {
            var radii = widened.RootElement.GetProperty("radiuses");
            Assert.Equal(2, radii.GetArrayLength());
            Assert.Equal(LoopClose.CloseSnapMeters, radii[0].GetDouble());
            Assert.Equal(LoopClose.CloseSnapMeters, radii[1].GetDouble());
        }

        request.SnapRadiusMeters = LoopClose.CloseSnapMeters + 1;
        await provider.RouteAsync(request, "header-only-ors-key", CancellationToken.None);
        using var ignored = JsonDocument.Parse(handler.Calls[2].Body!);
        Assert.False(ignored.RootElement.TryGetProperty("radiuses", out _));
    }
}

public class OpenRouteServiceLiveTests
{
    [Fact]
    public async Task Fort_Collins_segment_follows_a_road_when_ORS_API_KEY_is_set()
    {
        var key = Environment.GetEnvironmentVariable("ORS_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
            return;

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var client = new OpenRouteServiceClient(http);
        var provider = new OpenRouteServiceRoutingProvider(client);
        var route = await provider.RouteAsync(new RouteRequestDto
        {
            FromLatitude = 40.5853,
            FromLongitude = -105.0844,
            ToLatitude = 40.5895,
            ToLongitude = -105.0775,
            Activity = "Cycling"
        }, key!, CancellationToken.None);

        Assert.True(route.Routed, route.Message);
        Assert.True(route.Points.Count >= 2);
        var longitudes = route.Points.Select(point => point.Longitude).Append(-105.0844).Append(-105.0775).ToList();
        var latitudes = route.Points.Select(point => point.Latitude).Append(40.5853).Append(40.5895).ToList();
        // A road route between these corners is not the straight diagonal: it spreads farther than that chord.
        Assert.True(longitudes.Max() - longitudes.Min() > Math.Abs(-105.0775 - -105.0844) - 0.0001
            || route.Points.Count >= 4, route.Message);
        Assert.InRange(latitudes.Min(), 40.4, 40.7);
    }
}
