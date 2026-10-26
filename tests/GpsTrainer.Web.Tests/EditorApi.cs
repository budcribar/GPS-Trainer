using System.Net;
using System.Net.Http.Json;

namespace GpsTrainer.Web.Tests;

internal sealed class EditorApi : HttpMessageHandler
{
    public List<RouteRequestDto> Routes { get; } = [];

    /// <summary>The next directions calls that have no snap radius fail as an off-road start.</summary>
    public int UnroutableBeforeSnap { get; set; }

    /// <summary>A widened snap returns a road end 40 m from the requested point.</summary>
    public bool SnapMissesStart { get; set; }

    /// <summary>Every directions call fails with the off-road snap error.</summary>
    public bool NeverRoutes { get; set; }

    /// <summary>Course bodies the editor posted or put, in order.</summary>
    public List<Course> SavedCourses { get; } = [];

    /// <summary>Waypoint lists the editor stored, in order.</summary>
    public List<List<Waypoint>> SavedWaypoints { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? "";
        if (path.Contains("activity-codes", StringComparison.Ordinal))
        {
            return Json(new[]
            {
                new ActivityCode { ActKey = 0, ActValue = "Other" },
                new ActivityCode { ActKey = 3, ActValue = "Cycling" }
            });
        }
        if (path.Contains("directions", StringComparison.Ordinal))
        {
            var body = await request.Content!.ReadFromJsonAsync<RouteRequestDto>(cancellationToken) ?? new RouteRequestDto();
            Routes.Add(body);
            if (NeverRoutes || (UnroutableBeforeSnap > 0 && body.SnapRadiusMeters is null))
            {
                if (UnroutableBeforeSnap > 0 && body.SnapRadiusMeters is null)
                    UnroutableBeforeSnap--;
                return Json(RouteResponseDto.Fail(
                    "Could not find routable point within a radius of 350.0 meters of specified coordinate 1."));
            }
            var endLat = body.ToLatitude;
            var endLon = body.ToLongitude;
            if (SnapMissesStart && body.SnapRadiusMeters is not null)
            {
                var shifted = GeoMath.Translate(endLat, endLon, 0, 0.040);
                endLat = shifted.Latitude;
                endLon = shifted.Longitude;
            }
            return Json(new RouteResponseDto
            {
                Routed = true,
                Points =
                [
                    new RoutePointDto
                    {
                        Latitude = (body.FromLatitude + endLat) / 2 + 0.01,
                        Longitude = (body.FromLongitude + endLon) / 2,
                        WaypointType = "Continue"
                    },
                    new RoutePointDto
                    {
                        Latitude = endLat,
                        Longitude = endLon,
                        WaypointType = "Continue"
                    }
                ]
            });
        }
        if (path.Contains("/users/", StringComparison.Ordinal))
            return Json(new UserProfileDto { Login = "bud" });
        if (path.Contains("/api/courses", StringComparison.Ordinal) && path.Contains("/waypoints", StringComparison.Ordinal)
            && request.Method == HttpMethod.Put)
        {
            var points = await request.Content!.ReadFromJsonAsync<List<Waypoint>>(cancellationToken) ?? [];
            SavedWaypoints.Add(points);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
        if (path.Contains("/api/courses", StringComparison.Ordinal) && !path.Contains("/waypoints", StringComparison.Ordinal)
            && (request.Method == HttpMethod.Post || request.Method == HttpMethod.Put))
        {
            var course = await request.Content!.ReadFromJsonAsync<Course>(cancellationToken) ?? new Course();
            if (course.CourseId == Guid.Empty)
                course.CourseId = Guid.NewGuid();
            SavedCourses.Add(course);
            return Json(course);
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json<T>(T body) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
}
