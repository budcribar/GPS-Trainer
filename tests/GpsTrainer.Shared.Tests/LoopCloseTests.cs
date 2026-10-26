using GpsTrainer;

namespace GpsTrainer.Shared.Tests;

public class LoopCloseTests
{
    [Fact]
    public void One_point_is_not_an_open_loop()
    {
        var points = new List<Waypoint>
        {
            new() { Latitude = 40, Longitude = -105, WaypointType = WaypointType.Start }
        };
        Assert.False(LoopClose.HasOpenLoop(points));
        Assert.False(LoopClose.IsNearStart(points, 41, -105));
    }

    [Fact]
    public void Finish_near_the_start_closes_and_a_far_click_does_not()
    {
        var points = OpenCourse();
        Assert.True(LoopClose.HasOpenLoop(points));
        var beside = GeoMath.Translate(40, -105, 90, LoopClose.NearStartMeters / 1000.0 * 0.5);
        var away = GeoMath.Translate(40, -105, 90, 0.5);
        Assert.True(LoopClose.IsNearStart(points, beside.Latitude, beside.Longitude));
        Assert.False(LoopClose.IsNearStart(points, away.Latitude, away.Longitude));
    }

    [Fact]
    public void A_course_that_already_finishes_at_the_start_is_closed()
    {
        var points = OpenCourse();
        points.Add(new Waypoint { Latitude = 40, Longitude = -105, WaypointType = WaypointType.Finish, TimestampTicks = 3 });
        Assert.False(LoopClose.HasOpenLoop(points));
    }

    [Fact]
    public void Publishing_a_closed_loop_stamps_distance_and_shows_finish_on_the_profile()
    {
        var points = OpenCourse();
        points.Add(new Waypoint
        {
            Latitude = 40,
            Longitude = -105,
            AltitudeKm = Altitude.Unknown.Kilometers,
            WaypointType = WaypointType.Finish,
            TimestampTicks = 3
        });
        var course = new Course { Name = "Loop" };
        LoopClose.Publish(course, points);

        Assert.Equal(1.6, points[^1].AltitudeKm);
        Assert.True(course.ElapsedDistanceKm > 1);
        Assert.Equal(course.ElapsedDistanceKm, points[^1].ElapsedDistanceKm, 5);
        var plot = ElevationProfile.Plot(points, metric: false, course.Name);
        Assert.Contains(plot.Marks, mark => mark.Label.StartsWith("Finish", StringComparison.Ordinal));
        Assert.True(plot.MaxDistanceKm > 1);
    }

    [Fact]
    public void An_implicit_return_tells_the_rider_to_finish_on_the_start()
    {
        var sketch = new CourseSketch();
        sketch.AddClick(40.5853, -105.0844, WaypointType.Start, followRoads: false, route: null);
        sketch.AddClick(40.5900, -105.0760, WaypointType.Continue, followRoads: true, new RouteResponseDto
        {
            Routed = true,
            Points = [new RoutePointDto { Latitude = 40.5853, Longitude = -105.0844, AltitudeKm = 1.5 }]
        });

        Assert.Single(sketch.Waypoints);
        Assert.Contains("FINISH", sketch.Notice);
    }

    [Fact]
    public void An_off_road_start_keeps_the_road_and_the_exact_start()
    {
        Assert.True(LoopClose.NeedsWiderSnap(RouteResponseDto.Fail(
            "Could not find routable point within a radius of 350.0 meters of specified coordinate 1.")));
        Assert.False(LoopClose.NeedsWiderSnap(RouteResponseDto.Fail("Routing is not configured.")));
        Assert.False(LoopClose.NeedsWiderSnap(new RouteResponseDto { Routed = true, Points = [new RoutePointDto()] }));

        var beside = GeoMath.Translate(40, -105, 0, 0.005);
        var replaced = LoopClose.PinEndToStart(
            [new RoutePointDto { Latitude = beside.Latitude, Longitude = beside.Longitude, WaypointType = "Continue" }],
            40, -105);
        Assert.Single(replaced);
        Assert.Equal(40, replaced[0].Latitude, 5);
        Assert.Equal(-105, replaced[0].Longitude, 5);

        var road = GeoMath.Translate(40, -105, 0, 0.040);
        var appended = LoopClose.PinEndToStart(
            [new RoutePointDto { Latitude = road.Latitude, Longitude = road.Longitude, WaypointType = "Continue", AltitudeKm = 1.5 }],
            40, -105);
        Assert.Equal(2, appended.Count);
        Assert.Equal(road.Latitude, appended[0].Latitude, 5);
        Assert.Equal(1.5, appended[0].AltitudeKm, 5);
        Assert.Equal(40, appended[1].Latitude, 5);
        Assert.Equal(-105, appended[1].Longitude, 5);
        Assert.Equal(WaypointType.Finish.ToString(), appended[1].WaypointType);
    }

    [Fact]
    public void A_finish_on_the_start_uses_the_combined_glyph()
    {
        var open = OpenCourse();
        Assert.False(LoopClose.ShowsStartFinish(open));
        Assert.Equal("S", LoopClose.MarkerGlyph(WaypointType.Start, startAndFinish: false));
        open.Add(new Waypoint { Latitude = 40, Longitude = -105, WaypointType = WaypointType.Finish, TimestampTicks = 3 });
        Assert.True(LoopClose.ShowsStartFinish(open));
        Assert.Equal("S/F", LoopClose.MarkerGlyph(WaypointType.Start, startAndFinish: true));
        Assert.Equal("F", LoopClose.MarkerGlyph(WaypointType.Finish, startAndFinish: true));
    }

    private static List<Waypoint> OpenCourse() =>
    [
        new() { Latitude = 40, Longitude = -105, AltitudeKm = 1.6, WaypointType = WaypointType.Start, TimestampTicks = 1 },
        new() { Latitude = 40.02, Longitude = -105.01, AltitudeKm = 1.7, WaypointType = WaypointType.Continue, TimestampTicks = 2 }
    ];
}
