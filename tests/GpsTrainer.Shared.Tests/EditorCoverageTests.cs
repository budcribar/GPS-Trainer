using GpsTrainer;

namespace GpsTrainer.Shared.Tests;

/// <summary>
/// Waypoint types, units, GPX fidelity, loop close, and route-mode rules.
/// Tests marked Category=KnownBug assert the correct behavior and fail while the bug remains.
/// </summary>
public class EditorCoverageTests
{
    public static IEnumerable<object[]> EveryWaypointType() =>
        Enum.GetValues<WaypointType>().Select(type => new object[] { type });

    [Theory]
    [MemberData(nameof(EveryWaypointType))]
    public void Every_data_layer_type_round_trips_through_gpx_with_its_elevation(WaypointType type)
    {
        var index = (int)type;
        var waypoints = new List<Waypoint>
        {
            Point(WaypointType.Start, 1, 0, 40.00, -105.00, 1.610),
            Point(type, 2 + index, 0.4 + index / 100.0, 40.01, -105.01, 1.520 + index / 1000.0),
            Point(WaypointType.Finish, 90, 1.8, 40.02, -105.02, 1.480)
        };
        if (type is WaypointType.Start)
            waypoints = [Point(WaypointType.Start, 1, 0, 40.00, -105.00, 1.610), Point(WaypointType.Finish, 2, 1.2, 40.02, -105.02, 1.480)];
        if (type is WaypointType.Finish)
            waypoints = [Point(WaypointType.Start, 1, 0, 40.00, -105.00, 1.610), Point(WaypointType.Finish, 2, 1.2, 40.02, -105.02, 1.734)];

        var imported = GpxCourse.Import(GpxCourse.Export("Types", waypoints, []));
        var match = imported.Waypoints.Single(point => point.WaypointType == type);
        var original = waypoints.Single(point => point.WaypointType == type);
        Assert.Equal(original.TimestampTicks, match.TimestampTicks);
        Assert.Equal(original.AltitudeKm, match.AltitudeKm, 6);
        Assert.Equal(original.TextDescription, match.TextDescription);
        Assert.Equal(waypoints.Select(point => point.TimestampTicks), imported.Waypoints.Select(point => point.TimestampTicks));
    }

    [Fact]
    public void Menu_types_and_data_layer_types_are_the_sets_the_editor_documents()
    {
        Assert.Equal(
            [
                WaypointType.Continue, WaypointType.TurnRight, WaypointType.TurnLeft, WaypointType.TurnAround,
                WaypointType.Finish, WaypointType.FeedZone, WaypointType.Summit, WaypointType.Announcement
            ],
            WaypointMenus.MarkerTypes);
        Assert.Equal([WaypointType.FeedZone, WaypointType.Summit, WaypointType.Announcement], WaypointMenus.PinMenuTypes);
        Assert.DoesNotContain(WaypointMenus.MarkerTypes, type => type is WaypointType.BearLeft or WaypointType.BearRight or WaypointType.Arrive);

        var last = MapContextMenu.For(lastPointSelected: true, "1 mi FINISH").Select(row => row.Label).ToArray();
        Assert.Equal(["CONTINUE", "ROUTE", "TURN RIGHT", "TURN LEFT", "FINISH", "—", "1 mi FINISH", "—", "ZOOM", "MOVE"], last);
        var earlier = MapContextMenu.For(lastPointSelected: false, "1 mi FINISH").Select(row => row.Label).ToArray();
        Assert.Equal(["CONTINUE", "TURN RIGHT", "TURN LEFT", "ZOOM", "MOVE"], earlier);
        Assert.DoesNotContain(last, label => label.Contains("BEAR", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void English_is_the_default_and_miles_feet_kilometers_meters_convert()
    {
        Assert.False(ElevationUnits.MetricByDefault);
        Assert.Equal("km", ElevationUnits.SwitchLabel(metric: false));
        Assert.Equal("mi", ElevationUnits.SwitchLabel(metric: true));
        Assert.Equal("0.00 mi", ElevationUnits.FormatDistance(0, metric: false));
        Assert.Equal("1.00 km", ElevationUnits.FormatDistance(1, metric: true));
        Assert.Equal("0.62 mi", ElevationUnits.FormatDistance(1, metric: false));
        Assert.Equal("1.00 mi", ElevationUnits.FormatDistance(EditorText.MilesToKilos(1), metric: false));
        Assert.Equal("1.61 km", ElevationUnits.FormatDistance(EditorText.MilesToKilos(1), metric: true));
        Assert.Equal("3281/1640 ft", ElevationProfile.ClimbDescent(1, 0.5, metric: false));
        Assert.Equal("1000/500 m", ElevationProfile.ClimbDescent(1, 0.5, metric: true));
        Assert.Equal(1, EditorText.FeetToKilos(3280.8399), 9);
        Assert.Equal(1, EditorText.MetersToKilos(1000), 9);
    }

    [Fact]
    public void Units_are_remembered_per_login_and_fall_back_to_english()
    {
        Assert.Equal("gpstrainer.elevationUnits", ElevationUnits.StorageKey(null));
        Assert.Equal("gpstrainer.elevationUnits", ElevationUnits.StorageKey("  "));
        Assert.Equal("gpstrainer.elevationUnits.rider", ElevationUnits.StorageKey(" rider "));
        Assert.False(ElevationUnits.IsMetric(null));
        Assert.False(ElevationUnits.IsMetric("english"));
        Assert.True(ElevationUnits.IsMetric(" METRIC "));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("Cycling", true)]
    [InlineData("Running", true)]
    [InlineData("Trail running", false)]
    [InlineData("Mountain bike", false)]
    [InlineData("XCountry ski", false)]
    public void Route_mode_follows_roads_except_trail_mountain_and_cross_country(string? activity, bool followsRoads)
    {
        Assert.Equal(followsRoads, RouteModePolicy.FollowsRoads(activity));
    }

    [Fact]
    public void A_point_within_eight_meters_is_rejected_and_a_real_leg_is_not()
    {
        Assert.Equal("That point is too close to the previous one.", RoutingLimits.RejectSegment(40, -105, 40, -105));
        Assert.Null(RoutingLimits.RejectSegment(40.5853, -105.0844, 40.5950, -105.0700));
        Assert.False(RoutingLimits.AllowsStraightFallback("That point is too close to the previous one."));
        Assert.Equal("Those coordinates are not on the map.", RoutingLimits.RejectSegment(91, 0, 40, -105));
    }

    [Fact]
    public void Deleting_the_start_promotes_the_next_point_and_deleting_the_finish_promotes_the_previous()
    {
        var deletedStart = new List<Waypoint>
        {
            Point(WaypointType.Start, 1, 0, 40.00, -105.00, 1.5),
            Point(WaypointType.TurnLeft, 2, 0.4, 40.01, -105.01, 1.5, "onto Main"),
            Point(WaypointType.Finish, 3, 0.8, 40.02, -105.02, 1.5)
        };
        Assert.True(CourseEdits.Delete(deletedStart, 0).Ok);
        Assert.Equal(WaypointType.Start, deletedStart[0].WaypointType);
        Assert.Equal("onto Main", deletedStart[0].TextDescription);
        Assert.Equal(0, deletedStart[0].ElapsedDistanceKm);

        var deletedFinish = new List<Waypoint>
        {
            Point(WaypointType.Start, 1, 0, 40.00, -105.00, 1.5),
            Point(WaypointType.TurnLeft, 2, 0.4, 40.01, -105.01, 1.5, "onto Main"),
            Point(WaypointType.Finish, 3, 0.8, 40.02, -105.02, 1.5)
        };
        Assert.True(CourseEdits.Delete(deletedFinish, 2).Ok);
        Assert.Equal(WaypointType.Finish, deletedFinish[^1].WaypointType);
        Assert.Equal("onto Main", deletedFinish[^1].TextDescription);
    }

    [Fact]
    public void Undo_past_empty_does_nothing_and_one_undo_removes_only_the_last_point()
    {
        var sketch = new CourseSketch();
        Assert.False(sketch.Undo());
        sketch.AddClick(40.00, -105.00, WaypointType.Start, followRoads: false, route: null);
        sketch.AddClick(40.02, -105.02, WaypointType.Continue, followRoads: false, route: null);
        Assert.Equal(2, sketch.Waypoints.Count);
        Assert.True(sketch.Undo());
        Assert.Single(sketch.Waypoints);
        Assert.Equal(WaypointType.Start, sketch.Waypoints[0].WaypointType);
        Assert.True(sketch.Undo());
        Assert.Empty(sketch.Waypoints);
        Assert.False(sketch.Undo());
    }

    [Fact]
    public void Closing_on_the_start_shows_the_combined_marker_and_a_single_point_is_not_a_loop()
    {
        var open = new List<Waypoint>
        {
            Point(WaypointType.Start, 1, 0, 40.00, -105.00, 1.5),
            Point(WaypointType.Continue, 2, 0.5, 40.02, -105.02, 1.6)
        };
        Assert.True(LoopClose.HasOpenLoop(open));
        Assert.False(LoopClose.HasOpenLoop(open.Take(1).ToList()));
        Assert.False(LoopClose.ShowsStartFinish(open));

        open.Add(Point(WaypointType.Finish, 3, 1.0, 40.00, -105.00, 1.5));
        Assert.True(LoopClose.ShowsStartFinish(open));
        Assert.Equal("S/F", LoopClose.MarkerGlyph(WaypointType.Start, startAndFinish: true));
        Assert.Equal("F", LoopClose.MarkerGlyph(WaypointType.Finish, startAndFinish: true));
        Assert.False(LoopClose.HasOpenLoop(open));
    }

    [Fact]
    public void Moving_the_start_away_from_the_finish_clears_the_combined_marker()
    {
        var waypoints = new List<Waypoint>
        {
            Point(WaypointType.Start, 1, 0, 40.00, -105.00, 1.5),
            Point(WaypointType.Continue, 2, 0.5, 40.02, -105.02, 1.6),
            Point(WaypointType.Finish, 3, 1.0, 40.00, -105.00, 1.5)
        };
        Assert.True(LoopClose.ShowsStartFinish(waypoints));
        Assert.True(CourseEdits.Move(waypoints, 0, 40.03, -105.03, Guid.Empty).Ok);
        Assert.False(LoopClose.ShowsStartFinish(waypoints));
        Assert.Equal(WaypointType.Start, waypoints[0].WaypointType);
    }

    [Fact]
    public void Zero_or_one_waypoint_fails_validation_and_a_real_loop_passes()
    {
        Assert.Equal("Course is too short", CourseRules.Validate([]));
        Assert.Equal("Course is too short", CourseRules.Validate([Point(WaypointType.Start, 1, 0, 40, -105, 1)]));
        var loop = new List<Waypoint>
        {
            Point(WaypointType.Start, 1, 0, 40.00, -105.00, 1.5),
            Point(WaypointType.TurnRight, 2, 0.4, 40.01, -104.99, 1.6, "right"),
            Point(WaypointType.Finish, 3, 0.8, 40.00, -105.00, 1.5)
        };
        CourseEdits.StampRoute(loop);
        Assert.Null(CourseRules.Validate(loop));
    }

    [Fact]
    public void A_real_zero_meter_elevation_round_trips_through_gpx_itself()
    {
        var waypoints = new List<Waypoint>
        {
            Point(WaypointType.Start, 1, 0, 51.5, 0.0, 0),
            Point(WaypointType.Finish, 2, 0.3, 51.51, 0.01, 0.012)
        };
        var imported = GpxCourse.Import(GpxCourse.Export("Sea", waypoints, []));
        Assert.Equal(0, imported.Waypoints[0].AltitudeKm, 6);
        Assert.Equal(0.012, imported.Waypoints[1].AltitudeKm, 6);
    }

    [Fact]
    public void KnownBug_Gpx_export_keeps_timestamp_order_when_elapsed_distance_is_not_sorted()
    {
        var waypoints = new List<Waypoint>
        {
            Point(WaypointType.Start, 10, 0, 40.00, -105.00, 1.5),
            Point(WaypointType.Continue, 20, 1.2, 40.01, -105.01, 1.6),
            Point(WaypointType.Finish, 30, 0, 40.02, -105.02, 1.4, "added after the distance was stamped")
        };

        var imported = GpxCourse.Import(GpxCourse.Export("Scrambled", waypoints, []));

        Assert.Equal([10L, 20L, 30L], imported.Waypoints.Select(point => point.TimestampTicks).ToArray());
        Assert.Equal(WaypointType.Finish, imported.Waypoints[^1].WaypointType);
    }

    [Fact]
    public void KnownBug_A_track_point_with_no_elevation_is_unknown_not_sea_level()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <gpx version="1.1" creator="coverage">
              <trk><name>No elevation</name><trkseg>
                <trkpt lat="40.00" lon="-105.00"></trkpt>
                <trkpt lat="40.02" lon="-105.02"><ele>1600</ele></trkpt>
              </trkseg></trk>
            </gpx>
            """;

        var imported = GpxCourse.Import(xml);

        Assert.Equal(Altitude.Unknown.Kilometers, imported.Waypoints[0].AltitudeKm);
        Assert.Equal(1.6, imported.Waypoints[1].AltitudeKm, 6);
        Assert.Equal(0, imported.AltitudeGainKm, 3);
    }

    [Fact]
    public void KnownBug_Duplicate_user_name_is_not_reported_as_a_duplicate_course()
    {
        var source = ReadRepoFile("src/GpsTrainer.App/Pages/Screens.cs");
        var at = source.IndexOf("\"DuplicateUserName\"", StringComparison.Ordinal);
        Assert.True(at > 0, "Signup no longer maps DuplicateUserName.");
        var window = source.Substring(at, Math.Min(180, source.Length - at));
        Assert.DoesNotContain("duplicate_course_name", window, StringComparison.Ordinal);
        Assert.Contains("user name", window, StringComparison.OrdinalIgnoreCase);
    }

    private static Waypoint Point(
        WaypointType type, long ticks, double elapsedKm, double latitude, double longitude, double altitudeKm, string? text = null) =>
        new()
        {
            WaypointType = type,
            TimestampTicks = ticks,
            ElapsedDistanceKm = elapsedKm,
            Latitude = latitude,
            Longitude = longitude,
            AltitudeKm = altitudeKm,
            HeadingDegrees = 12,
            TextDescription = text ?? type.ToString()
        };

    private static string ReadRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(relative);
    }
}
