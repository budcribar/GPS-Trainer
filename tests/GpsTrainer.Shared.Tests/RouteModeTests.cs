using GpsTrainer;

namespace GpsTrainer.Shared.Tests;

public class RouteModeTests
{
    [Theory]
    [InlineData("Running", true)]
    [InlineData("Cycling", true)]
    [InlineData("Race Walking", true)]
    [InlineData("Rowing", true)]
    [InlineData("Other", true)]
    [InlineData("Trail Running", false)]
    [InlineData("Mountain Biking", false)]
    [InlineData("XCountry Skate", false)]
    [InlineData("XCountry Classic", false)]
    [InlineData(null, true)]
    public void Road_following_matches_the_2013_activity_rule(string? activity, bool followsRoads)
    {
        Assert.Equal(followsRoads, RouteModePolicy.FollowsRoads(activity));
    }

    [Fact]
    public void Logged_out_map_center_is_Fort_Collins()
    {
        Assert.Equal(40.5853, MapDefaults.FortCollinsLatitude, 4);
        Assert.Equal(-105.0844, MapDefaults.FortCollinsLongitude, 4);
    }

    [Fact]
    public void A_saved_home_is_the_opening_center_and_a_missing_home_stays_at_Fort_Collins()
    {
        var home = MapDefaults.OpeningCenter(37.7749, -122.4194);
        Assert.Equal(37.7749, home.Latitude, 5);
        Assert.Equal(-122.4194, home.Longitude, 5);

        var missing = MapDefaults.OpeningCenter(null, null);
        Assert.Equal(MapDefaults.FortCollinsLatitude, missing.Latitude, 5);
        Assert.Equal(MapDefaults.FortCollinsLongitude, missing.Longitude, 5);

        var half = MapDefaults.OpeningCenter(37.7749, null);
        Assert.Equal(MapDefaults.FortCollinsLatitude, half.Latitude, 5);
        Assert.Equal(MapDefaults.FortCollinsLongitude, half.Longitude, 5);

        var offMap = MapDefaults.OpeningCenter(120, -122.4194);
        Assert.Equal(MapDefaults.FortCollinsLatitude, offMap.Latitude, 5);
        Assert.Equal(MapDefaults.FortCollinsLongitude, offMap.Longitude, 5);
    }
}

public class CourseSketchTests
{
    [Fact]
    public void A_routed_click_stores_the_road_points_and_undo_removes_that_segment()
    {
        var sketch = new CourseSketch();
        sketch.AddClick(40.5853, -105.0844, WaypointType.Start, followRoads: true, route: null);
        var route = new RouteResponseDto
        {
            Routed = true,
            Points =
            [
                new RoutePointDto { Latitude = 40.5855, Longitude = -105.0800, WaypointType = "Continue" },
                new RoutePointDto { Latitude = 40.5870, Longitude = -105.0760, WaypointType = "TurnLeft", Text = "Turn left onto College Avenue" },
                new RoutePointDto { Latitude = 40.5900, Longitude = -105.0760, WaypointType = "Continue" }
            ]
        };

        sketch.AddClick(40.5900, -105.0760, WaypointType.Finish, followRoads: true, route);
        Assert.Equal(4, sketch.Waypoints.Count);
        Assert.Equal(WaypointType.TurnLeft, sketch.Waypoints[2].WaypointType);
        Assert.Equal("Turn left onto College Avenue", sketch.Waypoints[2].TextDescription);
        Assert.Equal(WaypointType.Finish, sketch.Waypoints[3].WaypointType);
        Assert.NotEqual(sketch.Waypoints[0].Longitude, sketch.Waypoints[1].Longitude);

        Assert.True(sketch.Undo());
        Assert.Single(sketch.Waypoints);
        Assert.Equal(WaypointType.Start, sketch.Waypoints[0].WaypointType);
        Assert.True(sketch.Undo());
        Assert.Empty(sketch.Waypoints);
        Assert.False(sketch.CanUndo);
    }

    [Fact]
    public void A_routing_failure_adds_one_straight_point_and_says_so()
    {
        var sketch = new CourseSketch();
        sketch.AddClick(40.58, -105.08, WaypointType.Start, true, null);
        sketch.AddClick(40.59, -105.07, WaypointType.TurnRight, true, RouteResponseDto.Fail("Routing failed."));
        Assert.Equal(2, sketch.Waypoints.Count);
        Assert.Equal(40.59, sketch.Waypoints[1].Latitude);
        Assert.Contains("straight segment", sketch.Notice, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_point_that_is_too_close_is_not_stored()
    {
        var sketch = new CourseSketch();
        sketch.AddClick(40.58, -105.08, WaypointType.Start, true, null);
        sketch.AddClick(40.58001, -105.08001, WaypointType.Continue, true,
            RouteResponseDto.Fail("That point is too close to the previous one.", straightFallback: false));
        Assert.Single(sketch.Waypoints);
        Assert.Contains("too close", sketch.Notice, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Freehand_stores_the_click_itself()
    {
        var sketch = new CourseSketch();
        sketch.AddClick(40.58, -105.08, WaypointType.Start, followRoads: false, route: null);
        sketch.AddClick(40.59, -105.07, WaypointType.TurnLeft, followRoads: false, route: null);
        Assert.Equal(2, sketch.Waypoints.Count);
        Assert.Equal(40.59, sketch.Waypoints[1].Latitude);
        Assert.Null(sketch.Notice);
    }

    [Fact]
    public void Segments_stay_in_order_and_a_return_to_the_start_is_not_stored()
    {
        var sketch = new CourseSketch();
        sketch.AddClick(40.5853, -105.0844, WaypointType.Start, followRoads: false, route: null);
        sketch.AddClick(40.5900, -105.0760, WaypointType.TurnLeft, followRoads: true, new RouteResponseDto
        {
            Routed = true,
            OriginAltitudeKm = 1.520,
            Points =
            [
                new RoutePointDto { Latitude = 40.5860, Longitude = -105.0800, AltitudeKm = 1.530 },
                new RoutePointDto { Latitude = 40.5900, Longitude = -105.0760, AltitudeKm = 1.540 },
                new RoutePointDto { Latitude = 40.5853, Longitude = -105.0844, AltitudeKm = 1.520 }
            ]
        });
        sketch.AddClick(40.5930, -105.0700, WaypointType.Finish, followRoads: true, new RouteResponseDto
        {
            Routed = true,
            Points =
            [
                new RoutePointDto { Latitude = 40.5915, Longitude = -105.0730, AltitudeKm = 1.510 },
                new RoutePointDto { Latitude = 40.5930, Longitude = -105.0700, AltitudeKm = 1.500 }
            ]
        });

        Assert.Equal(40.5853, sketch.Waypoints[0].Latitude, 5);
        Assert.Equal(1.520, sketch.Waypoints[0].AltitudeKm, 5);
        Assert.Equal(40.5860, sketch.Waypoints[1].Latitude, 5);
        Assert.Equal(1.530, sketch.Waypoints[1].AltitudeKm, 5);
        Assert.Equal(40.5930, sketch.Waypoints[^1].Latitude, 5);
        Assert.Equal(WaypointType.Finish, sketch.Waypoints[^1].WaypointType);
        Assert.DoesNotContain(sketch.Waypoints.Skip(1), point =>
            GeoMath.DistanceMeters(point.Latitude, point.Longitude, 40.5853, -105.0844) < 15);
    }

    [Fact]
    public void Closing_on_the_start_keeps_the_return_and_a_preview_keeps_its_elevations()
    {
        var sketch = new CourseSketch();
        sketch.Replace(
        [
            new Waypoint { Latitude = 40.50, Longitude = -105.10, AltitudeKm = 1.5, WaypointType = WaypointType.Start, TimestampTicks = 1 },
            new Waypoint { Latitude = 40.60, Longitude = -105.20, AltitudeKm = 1.7, WaypointType = WaypointType.Continue, TimestampTicks = 2 }
        ]);
        sketch.AddClick(40.50, -105.10, WaypointType.Finish, followRoads: true, new RouteResponseDto
        {
            Routed = true,
            Points =
            [
                new RoutePointDto { Latitude = 40.55, Longitude = -105.15, AltitudeKm = 1.6 },
                new RoutePointDto { Latitude = 40.50, Longitude = -105.10, AltitudeKm = 1.5 }
            ]
        });

        Assert.Equal(1.5, sketch.Waypoints[0].AltitudeKm);
        Assert.Equal(1.7, sketch.Waypoints[1].AltitudeKm);
        Assert.Equal(40.50, sketch.Waypoints[^1].Latitude, 5);
        Assert.Equal(WaypointType.Finish, sketch.Waypoints[^1].WaypointType);
        Assert.Equal(4, sketch.Waypoints.Count);
    }
}
