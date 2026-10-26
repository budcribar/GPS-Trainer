using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace GpsTrainer.Server.Tests;

public class IntervalPersistenceTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;

    public IntervalPersistenceTests(ApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Intervals_persist_move_and_round_trip_through_gpx()
    {
        await _client.PostAsJsonAsync("/api/users", new UserWriteDto { Login = "intervals", Password = "secret", Email = "intervals@example.com" });
        var session = await (await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Login = "intervals", Password = "secret" })).Content.ReadFromJsonAsync<LoginResponse>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.Token);

        var course = await (await _client.PostAsJsonAsync("/api/courses", new Course { Name = "Anchored", Login = "intervals" })).Content.ReadFromJsonAsync<Course>();
        var line = new List<Waypoint>();
        for (var i = 0; i < 4; i++)
        {
            var point = GeoMath.Translate(40, -105, 0, i);
            line.Add(new Waypoint
            {
                Latitude = point.Latitude,
                Longitude = point.Longitude,
                ElapsedDistanceKm = i,
                TimestampTicks = i + 1,
                WaypointType = i == 0 ? WaypointType.Start : i == 3 ? WaypointType.Finish : WaypointType.Continue
            });
        }
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutAsJsonAsync($"/api/courses/{course!.CourseId}/waypoints", line)).StatusCode);

        var placed = await (await _client.PostAsJsonAsync($"/api/courses/{course.CourseId}/intervals", new PlaceIntervalRequest
        {
            Name = "Climb",
            StartElapsedKm = 1,
            FinishElapsedKm = 2
        })).Content.ReadFromJsonAsync<CourseIntervalDto>();
        Assert.Equal("Climb", placed!.Name);
        Assert.Equal(1, placed.LengthKm, 2);

        var listed = await _client.GetFromJsonAsync<List<CourseIntervalDto>>($"/api/courses/{course.CourseId}/intervals");
        var stored = Assert.Single(listed!);
        Assert.Equal(placed.IntervalId, stored.IntervalId);
        Assert.Equal(placed.StartTimestampTicks, stored.StartTimestampTicks);

        var moved = await (await _client.PostAsJsonAsync($"/api/courses/{course.CourseId}/intervals/{stored.IntervalId}/move", new MoveIntervalDistanceRequest
        {
            StartElapsedKm = 0.5,
            FinishElapsedKm = 1.5
        })).Content.ReadFromJsonAsync<CourseIntervalDto>();
        Assert.Equal(stored.IntervalId, moved!.IntervalId);
        Assert.NotEqual(stored.StartTimestampTicks, moved.StartTimestampTicks);
        Assert.Equal(0.5, moved.StartElapsedKm, 2);
        Assert.Equal(1.5, moved.FinishElapsedKm, 2);

        var waypoints = await _client.GetFromJsonAsync<List<Waypoint>>($"/api/courses/{course.CourseId}/waypoints");
        Assert.DoesNotContain(waypoints!, w => w.TimestampTicks == stored.StartTimestampTicks);
        Assert.Contains(waypoints!, w => w.TimestampTicks == moved.StartTimestampTicks && w.WaypointType == WaypointType.StartInterval);

        var gpx = await _client.GetStringAsync($"/api/courses/{course.CourseId}/export/gpx");
        GpxCourse.Validate(gpx);
        Assert.Contains(GpxCourse.IntervalNamespace, gpx, StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/courses/{course.CourseId}/intervals/{moved.IntervalId}")).StatusCode);
        Assert.Empty((await _client.GetFromJsonAsync<List<CourseIntervalDto>>($"/api/courses/{course.CourseId}/intervals"))!);

        var imported = await _client.PostAsync($"/api/courses/{course.CourseId}/import/gpx", new StringContent(gpx, System.Text.Encoding.UTF8, "application/gpx+xml"));
        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
        var again = await _client.GetFromJsonAsync<List<CourseIntervalDto>>($"/api/courses/{course.CourseId}/intervals");
        var restored = Assert.Single(again!);
        Assert.Equal("Climb", restored.Name);
        Assert.Equal(moved.StartElapsedKm, restored.StartElapsedKm, 4);
        Assert.Equal(moved.FinishElapsedKm, restored.FinishElapsedKm, 4);
        Assert.Equal(moved.StartTimestampTicks, restored.StartTimestampTicks);
    }
}
