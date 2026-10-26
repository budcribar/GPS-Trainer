using GpsTrainer;

namespace GpsTrainer.Shared.Tests;

public class EditorContextMenuTests
{
    [Fact]
    public void Elevation_opens_in_English_and_the_choice_is_stored_per_login()
    {
        Assert.False(ElevationUnits.MetricByDefault);
        Assert.Equal("Elevation units", ElevationUnits.GroupLabel);
        Assert.Equal("English (mi/ft)", ElevationUnits.EnglishLabel);
        Assert.Equal("Metric (km/m)", ElevationUnits.MetricLabel);
        Assert.Equal("gpstrainer.elevationUnits", ElevationUnits.StorageKey(null));
        Assert.Equal("gpstrainer.elevationUnits", ElevationUnits.StorageKey("  "));
        Assert.Equal("gpstrainer.elevationUnits.bud", ElevationUnits.StorageKey(" bud "));
        Assert.False(ElevationUnits.IsMetric(null));
        Assert.False(ElevationUnits.IsMetric("english"));
        Assert.True(ElevationUnits.IsMetric(" metric "));

        var plot = ElevationProfile.Plot(
            [new Waypoint { Latitude = 40, Longitude = -105, AltitudeKm = 1.5, WaypointType = WaypointType.Start }],
            ElevationUnits.MetricByDefault,
            "Horsetooth");
        Assert.Equal("Distance(mi)", plot.DistanceAxis);
        Assert.Equal("Altitude(ft)", plot.AltitudeAxis);
        Assert.EndsWith(" ft", plot.ClimbDescent);
    }

    [Fact]
    public void Delete_removes_a_middle_point_and_names_the_leg_its_neighbors_join()
    {
        var sketch = Line();
        var edit = sketch.DeleteAt(1);
        Assert.True(edit.Ok);
        Assert.Equal(3, sketch.Waypoints.Count);
        Assert.Equal(new RouteGap(1, 3), Assert.Single(edit.Gaps));
        Assert.Equal([1L, 3L, 4L], sketch.Waypoints.Select(point => point.TimestampTicks));

        Assert.True(sketch.Undo());
        Assert.Equal(4, sketch.Waypoints.Count);
        Assert.Equal(40.01, sketch.Waypoints[1].Latitude, 5);
    }

    [Fact]
    public void Deleting_an_end_promotes_Start_or_Finish_and_does_not_open_a_gap()
    {
        var sketch = Line();
        var first = sketch.DeleteAt(0);
        Assert.True(first.Ok);
        Assert.Empty(first.Gaps);
        Assert.Equal(WaypointType.Start, sketch.Waypoints[0].WaypointType);
        Assert.Equal(40.01, sketch.Waypoints[0].Latitude, 5);

        sketch.Undo();
        var last = sketch.DeleteAt(3);
        Assert.Empty(last.Gaps);
        Assert.Equal(WaypointType.Finish, sketch.Waypoints[^1].WaypointType);
        Assert.Equal(WaypointType.Start, sketch.Waypoints[0].WaypointType);
    }

    [Fact]
    public void Deleting_an_interval_marker_removes_the_pair_without_a_road_gap()
    {
        var sketch = Line();
        var placed = sketch.PlaceInterval(0, "400m", Guid.Empty);
        Assert.True(placed.Ok);
        Assert.Contains(sketch.Waypoints, point => point.WaypointType == WaypointType.StartInterval);
        var index = sketch.Waypoints.FindIndex(point => point.WaypointType == WaypointType.StartInterval);
        var deleted = sketch.DeleteAt(index);
        Assert.True(deleted.Ok);
        Assert.Empty(deleted.Gaps);
        Assert.DoesNotContain(sketch.Waypoints, point => point.WaypointType is WaypointType.StartInterval or WaypointType.FinishInterval);
        Assert.Equal(4, sketch.Waypoints.Count);
        Assert.True(sketch.Undo());
        Assert.Contains(sketch.Waypoints, point => point.WaypointType == WaypointType.FinishInterval);
    }

    [Fact]
    public void Connect_inserts_after_the_selected_point_and_will_not_join_a_point_to_itself()
    {
        var sketch = Line();
        sketch.Waypoints[3].WaypointType = WaypointType.Finish;
        var self = sketch.Connect(3, 3, WaypointType.Finish);
        Assert.False(self.Ok);
        Assert.Equal("Cannot connect waypoint to itself", self.Message);
        Assert.False(sketch.CanUndo);
        Assert.Equal(WaypointType.Finish, sketch.Waypoints[3].WaypointType);

        var edit = sketch.Connect(3, 0, WaypointType.TurnRight);
        Assert.True(edit.Ok);
        Assert.Equal(WaypointType.Continue, sketch.Waypoints[3].WaypointType);
        Assert.Equal(WaypointType.TurnRight, sketch.Waypoints[4].WaypointType);
        Assert.Equal(sketch.Waypoints[0].Latitude, sketch.Waypoints[4].Latitude, 5);
        Assert.Equal(WaypointType.Start, sketch.Waypoints[0].WaypointType);
        Assert.Equal(new RouteGap(4, sketch.Waypoints[4].TimestampTicks), Assert.Single(edit.Gaps));
        Assert.True(sketch.Waypoints[4].ElapsedDistanceKm > sketch.Waypoints[3].ElapsedDistanceKm);

        var placed = sketch.PlaceInterval(0, "400m", Guid.Empty);
        Assert.True(placed.Ok);
        Assert.Equal(WaypointType.Start, sketch.Waypoints.First(point => point.WaypointType == WaypointType.Start).WaypointType);
        Assert.Equal(40.00, sketch.Waypoints.First(point => !CourseEdits.IsInterval(point)).Latitude, 5);
        Assert.Equal(WaypointType.TurnRight, sketch.Waypoints.Last(point => !CourseEdits.IsInterval(point)).WaypointType);

        Assert.True(sketch.Undo());
        Assert.True(sketch.Undo());
        Assert.Equal(4, sketch.Waypoints.Count);
        Assert.Equal(WaypointType.Finish, sketch.Waypoints[3].WaypointType);
    }

    [Fact]
    public void Interval_distances_use_the_2013_list_and_land_on_the_route()
    {
        Assert.Equal(["100m", "200m", "400m", "500m", "800m", "1000m", "1mi"], CourseEdits.IntervalDistances);
        var sketch = Line();
        var edit = sketch.PlaceInterval(1, "400m", Guid.Empty);
        Assert.True(edit.Ok);
        var interval = Assert.Single(IntervalCourse.FromMarkers(Guid.Empty, sketch.Waypoints));
        Assert.Equal("400m", interval.Name);
        Assert.Equal(0.4, interval.LengthKm, 2);
        Assert.Equal(WaypointType.StartInterval, sketch.Waypoints.Single(point => point.TimestampTicks == interval.StartTimestampTicks).WaypointType);

        var mile = new CourseSketch();
        mile.Replace(Line().Waypoints.Select(point => new Waypoint
        {
            TimestampTicks = point.TimestampTicks,
            Latitude = point.Latitude,
            Longitude = point.Longitude,
            WaypointType = point.WaypointType
        }).ToList());
        Assert.True(mile.PlaceInterval(0, "1mi", Guid.Empty).Ok);
        var longInterval = Assert.Single(IntervalCourse.FromMarkers(Guid.Empty, mile.Waypoints));
        Assert.Equal(EditorText.MilesToKilos(1), longInterval.LengthKm, 2);

        var tooShort = new CourseSketch();
        tooShort.AddClick(40, -105, WaypointType.Start, false, null);
        var rejected = tooShort.PlaceInterval(0, "100m", Guid.Empty);
        Assert.False(rejected.Ok);
        Assert.Equal("Course is too short to add an Interval", rejected.Message);
    }

    [Fact]
    public void The_standard_finish_uses_the_2013_length_and_the_click_as_its_direction()
    {
        Assert.Equal("1 mi", CourseEdits.StandardCourseLength(0, metric: false));
        Assert.Equal("1 km", CourseEdits.StandardCourseLength(0, metric: true));
        Assert.Equal("1 mi FINISH", CourseEdits.StandardFinishMenuLabel(0, metric: false));
        Assert.Equal("42.2 km", CourseEdits.StandardCourseLength(42.1, metric: true));
        Assert.Equal("26.2 mi", CourseEdits.StandardCourseLength(26.1 / 0.621371192, metric: false));
        Assert.Equal("4 mi", CourseEdits.StandardCourseLength(EditorText.MilesToKilos(3.2), metric: false));

        var sketch = new CourseSketch();
        sketch.AddClick(40, -105, WaypointType.Start, false, null);
        var toward = GeoMath.Translate(40, -105, 0, 1);
        var edit = sketch.AddStandardFinish(0, toward.Latitude, toward.Longitude, metric: false);
        Assert.True(edit.Ok);
        var expected = GeoMath.Translate(40, -105, 0, EditorText.MilesToKilos(1));
        Assert.Equal(WaypointType.Finish, sketch.Waypoints[1].WaypointType);
        Assert.Equal(expected.Latitude, sketch.Waypoints[1].Latitude, 4);
        Assert.Equal(expected.Longitude, sketch.Waypoints[1].Longitude, 4);
        Assert.Equal(new RouteGap(sketch.Waypoints[0].TimestampTicks, sketch.Waypoints[1].TimestampTicks), Assert.Single(edit.Gaps));

        var north = Line();
        var selected = north.Waypoints[1];
        var before = selected.Latitude;
        var samePoint = north.AddStandardFinish(1, selected.Latitude, selected.Longitude, metric: false);
        Assert.True(samePoint.Ok);
        Assert.True(north.Waypoints[2].Latitude > before);
        Assert.Equal(WaypointType.Finish, north.Waypoints[2].WaypointType);
    }

    [Fact]
    public void Moving_a_point_reroutes_both_neighbors_and_one_undo_puts_it_back()
    {
        var sketch = Line();
        var edit = sketch.MoveTo(2, 40.02, -104.99, Guid.Empty);
        Assert.True(edit.Ok);
        Assert.Equal(40.02, sketch.Waypoints[2].Latitude, 5);
        Assert.Equal(-104.99, sketch.Waypoints[2].Longitude, 5);
        Assert.Equal([new RouteGap(2, 3), new RouteGap(3, 4)], edit.Gaps);

        var first = sketch.SpliceRoute(2, 3, new RouteResponseDto
        {
            Routed = true,
            OriginAltitudeKm = 1.500,
            Points =
            [
                new RoutePointDto { Latitude = 40.015, Longitude = -104.995, AltitudeKm = 1.600, WaypointType = "Continue" },
                new RoutePointDto { Latitude = 40.02, Longitude = -104.99, AltitudeKm = 1.700 },
                new RoutePointDto { Latitude = 40.00, Longitude = -105.00, AltitudeKm = 1.400 }
            ]
        });
        var second = sketch.SpliceRoute(3, 4, new RouteResponseDto
        {
            Routed = true,
            Points =
            [
                new RoutePointDto { Latitude = 40.025, Longitude = -104.995, AltitudeKm = 1.800 },
                new RoutePointDto { Latitude = 40.03, Longitude = -105.00, AltitudeKm = 1.900 }
            ]
        });
        Assert.Equal(1, first);
        Assert.Equal(1, second);
        Assert.Equal(6, sketch.Waypoints.Count);
        Assert.Equal(1.500, sketch.Waypoints.Single(point => point.TimestampTicks == 2).AltitudeKm, 3);
        Assert.Equal(1.700, sketch.Waypoints.Single(point => point.TimestampTicks == 3).AltitudeKm, 3);
        Assert.Equal(1.900, sketch.Waypoints.Single(point => point.TimestampTicks == 4).AltitudeKm, 3);
        Assert.DoesNotContain(sketch.Waypoints.Skip(1), point =>
            GeoMath.DistanceMeters(point.Latitude, point.Longitude, 40.00, -105.00) < RoutePath.SamePointMeters);
        var measured = ElevationProfile.GainLoss(sketch.Waypoints);
        Assert.True(measured.GainKm > 0.4);
        Assert.Equal(0, measured.LossKm, 5);

        Assert.True(sketch.Undo());
        Assert.Equal(4, sketch.Waypoints.Count);
        Assert.Equal(-105.00, sketch.Waypoints[2].Longitude, 5);
        Assert.Equal(1.500, sketch.Waypoints[2].AltitudeKm, 3);
    }

    [Fact]
    public void Moving_an_interval_start_slides_the_pair_along_the_route()
    {
        var sketch = Line();
        Assert.True(sketch.PlaceInterval(0, "400m", Guid.Empty).Ok);
        var before = Assert.Single(IntervalCourse.FromMarkers(Guid.Empty, sketch.Waypoints));
        var start = sketch.Waypoints.Single(point => point.WaypointType == WaypointType.StartInterval);
        var index = sketch.Waypoints.IndexOf(start);
        var further = GeoMath.Translate(start.Latitude, start.Longitude, 0, 0.5);
        var edit = sketch.MoveTo(index, further.Latitude, further.Longitude, Guid.Empty);
        Assert.True(edit.Ok);
        Assert.Empty(edit.Gaps);
        var after = Assert.Single(IntervalCourse.FromMarkers(Guid.Empty, sketch.Waypoints));
        Assert.Equal(0.4, after.LengthKm, 2);
        Assert.True(after.StartElapsedKm > before.StartElapsedKm + 0.2);
    }

    private static CourseSketch Line()
    {
        var sketch = new CourseSketch();
        sketch.Replace(
        [
            new Waypoint { TimestampTicks = 1, Latitude = 40.00, Longitude = -105.00, AltitudeKm = 1.400, WaypointType = WaypointType.Start },
            new Waypoint { TimestampTicks = 2, Latitude = 40.01, Longitude = -105.00, AltitudeKm = 1.450, WaypointType = WaypointType.Continue },
            new Waypoint { TimestampTicks = 3, Latitude = 40.02, Longitude = -105.00, AltitudeKm = 1.500, WaypointType = WaypointType.Continue },
            new Waypoint { TimestampTicks = 4, Latitude = 40.03, Longitude = -105.00, AltitudeKm = 1.550, WaypointType = WaypointType.Continue }
        ]);
        return sketch;
    }
}
