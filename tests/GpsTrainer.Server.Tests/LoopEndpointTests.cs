using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace GpsTrainer.Server.Tests;

public class LoopEndpointTests : IClassFixture<RoutingApiFactory>
{
    private readonly RoutingApiFactory _factory;

    public LoopEndpointTests(RoutingApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Anonymous_generation_does_not_call_openrouteservice()
    {
        _factory.Ors.Calls = 0;
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/routing/loops", new LoopGenerateRequestDto
        {
            Latitude = MapDefaults.FortCollinsLatitude,
            Longitude = MapDefaults.FortCollinsLongitude,
            Distance = 8,
            Unit = "mi",
            Climbing = "flat",
            Type = "trail"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoopGenerateResponseDto>();
        Assert.NotNull(body);
        Assert.Contains("COTREX", body!.Attribution);
        Assert.Contains("OpenStreetMap", body.Attribution);
        Assert.False(body.ElevationUsed);
        Assert.False(body.RoadSnapped);
        Assert.Equal(0, _factory.Ors.Calls);
        Assert.NotEmpty(body.Candidates);
        Assert.Equal(WaypointType.Start, body.Candidates[0].Waypoints[0].WaypointType);
        Assert.Equal(WaypointType.Finish, body.Candidates[0].Waypoints[^1].WaypointType);
        Assert.Null(CourseRules.Validate(body.Candidates[0].Waypoints));
    }

    [Fact]
    public async Task Quota_response_keeps_the_loop_and_does_not_echo_the_key()
    {
        using var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/users", new UserWriteDto { Login = "loops", Password = "secret", Email = "loops@example.com" });
        var session = await (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Login = "loops", Password = "secret" })).Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.Token);
        _factory.Ors.Next = new GpsTrainer.Server.Routing.OrsHttpResult(429, """{"error":"Quota exceeded"}""");
        _factory.Ors.Calls = 0;

        var response = await client.PostAsJsonAsync("/api/routing/loops", new LoopGenerateRequestDto
        {
            Distance = 8,
            Unit = "mi",
            Climbing = "moderate",
            Type = "lane"
        });
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(RoutingApiFactory.EnvironmentKey, json);
        var body = JsonSerializer.Deserialize<LoopGenerateResponseDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(body);
        Assert.True(body!.QuotaLimited);
        Assert.NotEmpty(body.Candidates);
        Assert.True(_factory.Ors.Calls is > 0 and <= GpsTrainer.Server.Loops.LoopGenerationService.MaxOrsCalls);
    }

    [Fact]
    public async Task Rejects_a_distance_outside_the_supported_range()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/routing/loops", new LoopGenerateRequestDto { Distance = 1, Unit = "mi" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
