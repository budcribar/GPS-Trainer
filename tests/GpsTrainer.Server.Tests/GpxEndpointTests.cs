using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace GpsTrainer.Server.Tests;

public class GpxEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public GpxEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Preview_requires_auth_rejects_malicious_xml_and_does_not_save()
    {
        var anonymous = new StringContent("<gpx version=\"1.1\"></gpx>", Encoding.UTF8, "application/gpx+xml");
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsync("/api/courses/import/gpx", anonymous)).StatusCode);

        await _client.PostAsJsonAsync("/api/users", new UserWriteDto { Login = "gpx", Password = "secret", Email = "gpx@example.com" });
        var session = await (await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Login = "gpx", Password = "secret" })).Content.ReadFromJsonAsync<LoginResponse>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.Token);

        var bomb = new StringContent("<!DOCTYPE gpx [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]><gpx>&xxe;</gpx>", Encoding.UTF8, "application/gpx+xml");
        var rejected = await _client.PostAsync("/api/courses/import/gpx?fileName=evil.gpx", bomb);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        var oversized = new string('x', GpxCourse.MaxCharacters + 1);
        var tooBig = await _client.PostAsync("/api/courses/import/gpx", new StringContent(oversized, Encoding.UTF8, "application/gpx+xml"));
        Assert.Equal(HttpStatusCode.BadRequest, tooBig.StatusCode);

        var gpx = """
            <gpx version="1.1" creator="StravaGPX" xmlns="http://www.topografix.com/GPX/1/1">
              <trk><name>Preview</name><trkseg>
                <trkpt lat="40" lon="-105"><ele>1000</ele></trkpt>
                <trkpt lat="40.001" lon="-105"><ele>1010</ele></trkpt>
              </trkseg></trk>
            </gpx>
            """;
        var preview = await _client.PostAsync("/api/courses/import/gpx?fileName=preview.gpx", new StringContent(gpx, Encoding.UTF8, "application/gpx+xml"));
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var body = await preview.Content.ReadFromJsonAsync<GpxImportResult>();
        Assert.NotNull(body);
        Assert.False(body.Course.Publish);
        Assert.Equal("Preview", body.Course.Name);
        Assert.Equal("gpx", body.Course.Login);
        Assert.Contains("preview.gpx", body.Course.Comments, StringComparison.Ordinal);
        Assert.Contains("StravaGPX", body.Course.Comments, StringComparison.Ordinal);
        Assert.False(body.PreservedGpsTrainerExtensions);
        Assert.Equal(WaypointType.Start, body.Waypoints[0].WaypointType);
        Assert.Equal(WaypointType.Finish, body.Waypoints[^1].WaypointType);

        var listed = await _client.GetFromJsonAsync<List<Course>>("/api/courses?login=gpx");
        Assert.DoesNotContain(listed!, course => course.Name == "Preview");
    }

    [Fact]
    public async Task Export_requires_auth_and_round_trips_through_preview()
    {
        var missing = await _client.GetAsync($"/api/courses/{Guid.NewGuid()}/export/gpx");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        await _client.PostAsJsonAsync("/api/users", new UserWriteDto { Login = "gpx-export", Password = "secret", Email = "gpx-export@example.com" });
        var session = await (await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Login = "gpx-export", Password = "secret" })).Content.ReadFromJsonAsync<LoginResponse>();
        var authed = _client;
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.Token);

        var course = await (await authed.PostAsJsonAsync("/api/courses", new Course { Name = "Export me", Login = "gpx-export" })).Content.ReadFromJsonAsync<Course>();
        var line = new List<Waypoint>();
        for (var i = 0; i < 4; i++)
        {
            var point = GeoMath.Translate(40, -105, 0, i * 0.01);
            line.Add(new Waypoint
            {
                Latitude = point.Latitude,
                Longitude = point.Longitude,
                AltitudeKm = 1.6,
                ElapsedDistanceKm = i * 0.01,
                TimestampTicks = 100 + i,
                HeadingDegrees = 0,
                WaypointType = i == 0 ? WaypointType.Start : i == 3 ? WaypointType.Finish : WaypointType.Continue,
                TextDescription = i == 1 ? "gate" : ""
            });
        }
        Assert.Equal(HttpStatusCode.NoContent, (await authed.PutAsJsonAsync($"/api/courses/{course!.CourseId}/waypoints", line)).StatusCode);

        using var anonymous = _factory.CreateClient();
        var denied = await anonymous.GetAsync($"/api/courses/{course.CourseId}/export/gpx");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        var xml = await authed.GetStringAsync($"/api/courses/{course.CourseId}/export/gpx");
        GpxCourse.Validate(xml);
        Assert.Contains(GpxCourse.IntervalNamespace, xml, StringComparison.Ordinal);
        Assert.Contains("gate", xml, StringComparison.Ordinal);

        var preview = await authed.PostAsync("/api/courses/import/gpx", new StringContent(xml, Encoding.UTF8, "application/gpx+xml"));
        var body = await preview.Content.ReadFromJsonAsync<GpxImportResult>();
        Assert.True(body!.PreservedGpsTrainerExtensions);
        Assert.Equal(line.Count, body.Waypoints.Count);
        Assert.Equal(line.Select(point => point.WaypointType), body.Waypoints.Select(point => point.WaypointType));
        Assert.Equal(line.Select(point => point.TimestampTicks), body.Waypoints.Select(point => point.TimestampTicks));
        Assert.Equal("gate", body.Waypoints[1].TextDescription);
    }

    [Fact]
    public async Task Preview_sketch_edit_and_save_persists_course_and_waypoints()
    {
        await _client.PostAsJsonAsync("/api/users", new UserWriteDto { Login = "gpx-sketch", Password = "secret", Email = "gpx-sketch@example.com" });
        var session = await (await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Login = "gpx-sketch", Password = "secret" })).Content.ReadFromJsonAsync<LoginResponse>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.Token);

        var gpx = """
            <gpx version="1.1" creator="StravaGPX" xmlns="http://www.topografix.com/GPX/1/1">
              <trk><name>Sketch Ride</name><trkseg>
                <trkpt lat="40.58" lon="-105.08"><ele>1500</ele></trkpt>
                <trkpt lat="40.581" lon="-105.079"><ele>1510</ele></trkpt>
                <trkpt lat="40.582" lon="-105.081"><ele>1490</ele></trkpt>
              </trkseg></trk>
            </gpx>
            """;
        var preview = await _client.PostAsync("/api/courses/import/gpx?fileName=sketch.gpx", new StringContent(gpx, Encoding.UTF8, "application/gpx+xml"));
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var body = await preview.Content.ReadFromJsonAsync<GpxImportResult>();
        Assert.NotNull(body);
        Assert.False(body.Course.Publish);
        Assert.Contains("sketch.gpx", body.Course.Comments, StringComparison.Ordinal);

        var keptId = Guid.NewGuid();
        var course = body.Course;
        course.CourseId = keptId;
        course.Login = "gpx-sketch";
        course.Publish = false;

        var sketch = new CourseSketch();
        sketch.Replace(body.Waypoints);
        var last = sketch.LastRoutePoint();
        Assert.NotNull(last);
        sketch.AddClick(last.Latitude + 0.001, last.Longitude, WaypointType.Announcement, followRoads: false, route: null);
        Assert.True(sketch.Waypoints.Count > body.Waypoints.Count);

        var beforeSave = await _client.GetFromJsonAsync<List<Course>>("/api/courses?login=gpx-sketch");
        Assert.DoesNotContain(beforeSave!, item => item.Name == "Sketch Ride");

        var created = await _client.PostAsJsonAsync("/api/courses", course);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var saved = await created.Content.ReadFromJsonAsync<Course>();
        Assert.Equal(keptId, saved!.CourseId);
        Assert.False(saved.Publish);

        var written = await _client.PutAsJsonAsync($"/api/courses/{saved.CourseId}/waypoints", sketch.Waypoints);
        Assert.Equal(HttpStatusCode.NoContent, written.StatusCode);
        course.ElapsedDistanceKm = sketch.Waypoints[^1].ElapsedDistanceKm;
        var updated = await _client.PutAsJsonAsync($"/api/courses/{saved.CourseId}", course);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var storedCourse = await _client.GetFromJsonAsync<Course>($"/api/courses/{saved.CourseId}");
        Assert.Equal("Sketch Ride", storedCourse!.Name);
        Assert.False(storedCourse.Publish);
        Assert.Contains("sketch.gpx", storedCourse.Comments, StringComparison.Ordinal);

        var stored = await _client.GetFromJsonAsync<List<Waypoint>>($"/api/courses/{saved.CourseId}/waypoints");
        Assert.Equal(sketch.Waypoints.Count, stored!.Count);
        Assert.Equal(WaypointType.Start, stored[0].WaypointType);
        Assert.Equal(sketch.Waypoints[0].Latitude, stored[0].Latitude, 6);
        Assert.Equal(sketch.Waypoints[0].Longitude, stored[0].Longitude, 6);
        Assert.Equal(WaypointType.Announcement, stored[^1].WaypointType);
        Assert.Equal(sketch.Waypoints[^1].Latitude, stored[^1].Latitude, 6);
    }
}
