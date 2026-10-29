using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GpsTrainer.Server.Routing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GpsTrainer.Server.Tests;

public class ElevationProviderSelectionTests
{
    [Fact]
    public void An_empty_setting_selects_openrouteservice()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        Assert.Equal(OpenRouteServiceElevationProvider.ProviderName, ElevationProviderSelection.Selected(config));
        Assert.True(ElevationProviderSelection.UsesOpenRouteService(config));
    }

    [Theory]
    [InlineData("OpenRouteService")]
    [InlineData(" openrouteservice ")]
    public void The_openrouteservice_name_is_selected_ignoring_case_and_space(string configured)
    {
        var config = Config(configured);
        Assert.True(ElevationProviderSelection.UsesOpenRouteService(config));
    }

    [Fact]
    public void Another_name_is_not_openrouteservice()
    {
        var config = Config("AnotherSource");
        Assert.Equal("AnotherSource", ElevationProviderSelection.Selected(config));
        Assert.False(ElevationProviderSelection.UsesOpenRouteService(config));
    }

    private static IConfiguration Config(string provider) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ElevationProviderSelection.ConfigurationKey] = provider
        }).Build();
}

public class OpenRouteServiceElevationBehaviorTests
{
    [Fact]
    public async Task A_blank_key_is_unavailable_and_does_not_call_the_service()
    {
        var handler = new ElevationStubHandler();
        using var http = new HttpClient(handler);
        var provider = new OpenRouteServiceElevationProvider(new OpenRouteServiceClient(http), "  ");
        var response = await provider.ElevationLineAsync([(-105.1, 40.5)], CancellationToken.None);
        Assert.True(response.Unavailable);
        Assert.False(response.Limited);
        Assert.False(response.Found);
        Assert.Equal("Elevation is not configured.", response.Message);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Validation_failures_match_the_previous_messages_and_do_not_call_the_service()
    {
        var handler = new ElevationStubHandler();
        using var http = new HttpClient(handler);
        var provider = new OpenRouteServiceElevationProvider(new OpenRouteServiceClient(http), "key");

        var empty = await provider.ElevationLineAsync([], CancellationToken.None);
        Assert.Equal("No elevation was found.", empty.Message);
        Assert.False(empty.Unavailable);

        var offMap = await provider.ElevationLineAsync([(0, 120)], CancellationToken.None);
        Assert.Equal("Those coordinates are not on the map.", offMap.Message);

        var tooLong = Enumerable.Range(0, OpenRouteServiceElevationProvider.MaxElevationPoints + 1)
            .Select(index => (Longitude: -105.1, Latitude: 40.0 + index * 0.0001))
            .ToList();
        var rejected = await provider.ElevationLineAsync(tooLong, CancellationToken.None);
        Assert.Equal("That elevation line is too long.", rejected.Message);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task A_non_quota_failure_is_not_limited()
    {
        var handler = new ElevationStubHandler
        {
            Status = HttpStatusCode.BadRequest,
            ResponseBody = """{"error":"unable to get elevation"}"""
        };
        using var http = new HttpClient(handler);
        var provider = new OpenRouteServiceElevationProvider(new OpenRouteServiceClient(http), "key");
        var failed = await provider.ElevationLineAsync([(-105.1, 40.5)], CancellationToken.None);
        Assert.False(failed.Found);
        Assert.False(failed.Limited);
        Assert.False(failed.Unavailable);
    }
}

public sealed class ConfiguredElevationFactory : RoutingApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting(ElevationProviderSelection.ConfigurationKey, "OpenRouteService");
    }
}

public sealed class UnknownElevationFactory : RoutingApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting(ElevationProviderSelection.ConfigurationKey, "AnotherSource");
    }
}

public class ElevationProviderRegistrationTests : IClassFixture<ConfiguredElevationFactory>, IClassFixture<UnknownElevationFactory>
{
    private readonly ConfiguredElevationFactory _configured;
    private readonly UnknownElevationFactory _unknown;

    public ElevationProviderRegistrationTests(ConfiguredElevationFactory configured, UnknownElevationFactory unknown)
    {
        _configured = configured;
        _unknown = unknown;
    }

    [Fact]
    public void The_configured_provider_is_openrouteservice()
    {
        using var scope = _configured.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IElevationProvider>();
        Assert.IsType<OpenRouteServiceElevationProvider>(provider);
        Assert.Equal(OpenRouteServiceElevationProvider.ProviderName, provider.Name);
    }

    [Fact]
    public async Task An_unknown_provider_is_unavailable_and_does_not_call_openrouteservice()
    {
        _unknown.Ors.Calls = 0;
        using var scope = _unknown.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IElevationProvider>();
        Assert.Equal("AnotherSource", provider.Name);
        Assert.IsNotType<OpenRouteServiceElevationProvider>(provider);

        var response = await provider.ElevationLineAsync([(-105.1, 40.5)], CancellationToken.None);
        Assert.True(response.Unavailable);
        Assert.False(response.Limited);
        Assert.Equal("Elevation provider is not available.", response.Message);

        using var client = _unknown.CreateClient();
        var http = await client.PostAsJsonAsync("/api/routing/elevation", new ElevationLineRequestDto
        {
            Points = [new ElevationSampleDto { Latitude = 40.5, Longitude = -105.1, ElapsedKm = 1 }]
        });
        var body = await http.Content.ReadFromJsonAsync<ElevationLineResponseDto>();
        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        Assert.True(body!.Unavailable);
        Assert.Equal("Elevation provider is not available.", body.Message);
        Assert.Equal(0, _unknown.Ors.Calls);
    }
}

public sealed class FakeElevationProvider : IElevationProvider
{
    public string Name => "Fake";
    public int Calls { get; private set; }
    public IReadOnlyList<(double Longitude, double Latitude)>? Last { get; private set; }
    public ElevationLineResponseDto? Next { get; set; }

    public void Reset()
    {
        Calls = 0;
        Last = null;
        Next = null;
    }

    public Task<ElevationLineResponseDto> ElevationLineAsync(
        IReadOnlyList<(double Longitude, double Latitude)> points,
        CancellationToken cancellationToken)
    {
        Calls++;
        Last = points;
        if (Next is not null)
            return Task.FromResult(Next);
        return Task.FromResult(new ElevationLineResponseDto
        {
            Found = true,
            Points = points.Select(point => new ElevationSampleDto
            {
                Longitude = point.Longitude,
                Latitude = point.Latitude,
                AltitudeKm = 1.5
            }).ToList()
        });
    }
}

public sealed class InterfaceElevationFactory : RoutingApiFactory
{
    public FakeElevationProvider Elevation { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            foreach (var descriptor in services.Where(item => item.ServiceType == typeof(IElevationProvider)).ToList())
                services.Remove(descriptor);
            services.AddScoped<IElevationProvider>(_ => Elevation);
        });
    }
}

public class ElevationCallerTests : IClassFixture<InterfaceElevationFactory>
{
    private readonly InterfaceElevationFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ElevationCallerTests(InterfaceElevationFactory factory) => _factory = factory;

    [Fact]
    public async Task The_elevation_endpoint_calls_the_interface_and_keeps_elapsed_distance()
    {
        _factory.Elevation.Reset();
        _factory.Ors.Calls = 0;
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/routing/elevation", new ElevationLineRequestDto
        {
            Points =
            [
                new ElevationSampleDto { Latitude = 40.61, Longitude = -105.11, ElapsedKm = 0.25 },
                new ElevationSampleDto { Latitude = 40.62, Longitude = -105.10, ElapsedKm = 1.5 }
            ]
        });
        var parsed = JsonSerializer.Deserialize<ElevationLineResponseDto>(await response.Content.ReadAsStringAsync(), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, _factory.Elevation.Calls);
        Assert.Equal((-105.11, 40.61), _factory.Elevation.Last![0]);
        Assert.Equal((-105.10, 40.62), _factory.Elevation.Last[1]);
        Assert.True(parsed!.Found);
        Assert.Equal(0.25, parsed.Points[0].ElapsedKm);
        Assert.Equal(1.5, parsed.Points[1].ElapsedKm);
        Assert.Equal(1.5, parsed.Points[0].AltitudeKm, 5);
        Assert.Equal(0, _factory.Ors.Calls);
    }

    [Fact]
    public async Task Saving_waypoints_fills_unknown_altitudes_through_the_interface()
    {
        _factory.Elevation.Reset();
        _factory.Ors.Calls = 0;
        using var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/users", new UserWriteDto
        {
            Login = "elev-save",
            Password = "secret",
            Email = "elev-save@example.com"
        });
        var session = await (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Login = "elev-save",
            Password = "secret"
        })).Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.Token);
        var course = await (await client.PostAsJsonAsync("/api/courses", new Course
        {
            Name = "Hill",
            Login = "elev-save",
            ActivityCode = 1,
            Laps = 1
        })).Content.ReadFromJsonAsync<Course>();

        var saved = await client.PutAsJsonAsync($"/api/courses/{course!.CourseId}/waypoints", new[]
        {
            new Waypoint
            {
                Latitude = 40.50,
                Longitude = -105.20,
                WaypointType = WaypointType.Start,
                TimestampTicks = 1,
                AltitudeKm = Altitude.Unknown.Kilometers
            },
            new Waypoint
            {
                Latitude = 40.51,
                Longitude = -105.19,
                WaypointType = WaypointType.Finish,
                TimestampTicks = 2,
                AltitudeKm = Altitude.Unknown.Kilometers
            }
        });
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var stored = await client.GetFromJsonAsync<List<Waypoint>>($"/api/courses/{course.CourseId}/waypoints");
        Assert.Equal(1, _factory.Elevation.Calls);
        Assert.Equal((-105.20, 40.50), _factory.Elevation.Last![0]);
        Assert.Equal(1.5, stored![0].AltitudeKm, 5);
        Assert.Equal(1.5, stored[1].AltitudeKm, 5);
        Assert.Equal(0, _factory.Ors.Calls);
    }

    [Fact]
    public async Task Loop_generation_uses_the_interface_for_elevation_and_stops_when_it_is_limited()
    {
        _factory.Elevation.Reset();
        _factory.Elevation.Next = new ElevationLineResponseDto
        {
            Limited = true,
            Message = "Elevation lookup failed."
        };
        _factory.Ors.Calls = 0;
        using var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/users", new UserWriteDto
        {
            Login = "elev-loop",
            Password = "secret",
            Email = "elev-loop@example.com"
        });
        var session = await (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Login = "elev-loop",
            Password = "secret"
        })).Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.Token);

        var response = await client.PostAsJsonAsync("/api/routing/loops", new LoopGenerateRequestDto
        {
            Latitude = MapDefaults.FortCollinsLatitude,
            Longitude = MapDefaults.FortCollinsLongitude,
            Distance = 8,
            Unit = "mi",
            Climbing = "flat",
            Type = "trail"
        });
        var body = await response.Content.ReadFromJsonAsync<LoopGenerateResponseDto>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body!.QuotaLimited);
        Assert.False(body.ElevationUsed);
        Assert.Equal(1, _factory.Elevation.Calls);
        Assert.Equal(0, _factory.Ors.Calls);
    }
}
