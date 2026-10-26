using GpsTrainer;

namespace GpsTrainer.Shared.Tests;

public class MapCourseTapTests
{
    [Fact]
    public void A_road_tap_asks_for_a_route_and_stores_the_returned_points()
    {
        var sketch = new CourseSketch();
        sketch.AddClick(40.58, -105.08, WaypointType.Start, followRoads: false, route: null);
        Assert.True(CourseMapTap.TryCreateRouteRequest(
            sketch, 40.59, -105.07, followRoads: true, "Cycling", WaypointType.Continue, out var request, out var blocked));
        Assert.Null(blocked);
        Assert.NotNull(request);
        Assert.Equal("Cycling", request.Activity);
        Assert.Equal(40.58, request.FromLatitude);
        Assert.Equal(-105.08, request.FromLongitude);

        var route = new RouteResponseDto
        {
            Routed = true,
            Points =
            [
                new() { Latitude = 40.585, Longitude = -105.075, WaypointType = "Continue" },
                new() { Latitude = 40.59, Longitude = -105.07, WaypointType = "Continue" }
            ]
        };
        sketch.AddClick(40.59, -105.07, WaypointType.Finish, followRoads: true, route);
        var km = CourseMapTap.StampElapsed(sketch.Waypoints);
        Assert.Equal(3, sketch.Waypoints.Count);
        Assert.Equal(0, sketch.Waypoints[0].ElapsedDistanceKm);
        Assert.Equal(WaypointType.Finish, sketch.Waypoints[^1].WaypointType);
        Assert.True(sketch.Waypoints[^1].ElapsedDistanceKm > 0);
        Assert.Equal(sketch.Waypoints[^1].ElapsedDistanceKm, km);
        Assert.True(sketch.Undo());
        Assert.Equal(WaypointType.Start, Assert.Single(sketch.Waypoints).WaypointType);
    }

    [Fact]
    public void Trail_courses_do_not_ask_the_router_unless_road_following_is_on()
    {
        var sketch = Started();
        var follow = RouteModePolicy.FollowsRoads("Trail Running");
        Assert.False(CourseMapTap.TryCreateRouteRequest(
            sketch, 40.59, -105.07, follow, "Trail Running", WaypointType.Continue, out var request, out var blocked));
        Assert.Null(request);
        Assert.Null(blocked);
    }

    [Fact]
    public void A_tap_on_the_previous_point_is_not_a_new_waypoint()
    {
        var sketch = Started();
        Assert.False(CourseMapTap.TryCreateRouteRequest(
            sketch, 40.58, -105.08, true, "Running", WaypointType.Continue, out var request, out var blocked));
        Assert.Null(request);
        Assert.Contains("too close", blocked, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_map_bridge_reads_the_tapped_coordinates()
    {
        Assert.True(MapTapUrl.TryParse("gpstrainer://waypoint?lat=40.5853&lng=-105.0844", out var lat, out var lon));
        Assert.Equal(40.5853, lat, 4);
        Assert.Equal(-105.0844, lon, 4);
        Assert.False(MapTapUrl.TryParse("https://tile.openstreetmap.org/1/2/3.png", out _, out _));
    }

    private static CourseSketch Started()
    {
        var sketch = new CourseSketch();
        sketch.AddClick(40.58, -105.08, WaypointType.Start, followRoads: false, route: null);
        return sketch;
    }
}
