using System.Globalization;
using System.Xml;

namespace GpsTrainer.Shared.Tests;

public class GpxImportTests
{
    [Fact]
    public void Strava_track_becomes_start_finish_and_turns()
    {
        var imported = GpxCourse.Import(Load("strava.gpx"), new GpxImportOptions { FileName = "morning.gpx" });
        Assert.False(imported.HasGpsTrainerExtensions);
        Assert.False(imported.Publish);
        Assert.Equal("Morning Ride", imported.Name);
        Assert.Equal("StravaGPX Android", imported.Creator);
        Assert.Equal("Imported from morning.gpx (StravaGPX Android)", imported.Provenance);
        Assert.Equal(4, imported.Waypoints.Count);
        Assert.Equal(WaypointType.Start, imported.Waypoints[0].WaypointType);
        Assert.Equal(WaypointType.TurnLeft, imported.Waypoints[1].WaypointType);
        Assert.Equal(WaypointType.TurnRight, imported.Waypoints[2].WaypointType);
        Assert.Equal(WaypointType.Finish, imported.Waypoints[^1].WaypointType);
        Assert.True(imported.ElapsedDistanceKm > 0.2);
        Assert.True(imported.AltitudeGainKm > 0);
        Assert.Equal(imported.Waypoints[^1].ElapsedDistanceKm, imported.ElapsedDistanceKm, 6);
        Assert.Equal(1.62, imported.Waypoints[0].AltitudeKm, 3);
    }

    [Fact]
    public void Garmin_extensions_are_ignored_and_gpx_1_0_is_accepted()
    {
        var garmin = GpxCourse.Import(Load("garmin.gpx"), new GpxImportOptions { FileName = "lunch.gpx" });
        Assert.False(garmin.HasGpsTrainerExtensions);
        Assert.Equal("Lunch loop", garmin.Name);
        Assert.Equal("Imported from lunch.gpx (Garmin Connect)", garmin.Provenance);
        Assert.Equal(WaypointType.Start, garmin.Waypoints[0].WaypointType);
        Assert.Equal(WaypointType.Finish, garmin.Waypoints[^1].WaypointType);
        Assert.Equal(3, garmin.Waypoints.Count);

        var old = GpxCourse.Import(Load("gpx10.gpx"));
        Assert.Equal("Old track", old.Name);
        Assert.Equal(WaypointType.Start, old.Waypoints[0].WaypointType);
        Assert.Equal(WaypointType.Finish, old.Waypoints[1].WaypointType);
        Assert.Equal(1.7, old.Waypoints[0].AltitudeKm, 3);
    }

    [Fact]
    public void RideWithGps_route_points_and_waypoints_become_announcements()
    {
        var imported = GpxCourse.Import(Load("ridewithgps.gpx"), new GpxImportOptions { FileName = "creek.gpx" });
        Assert.Equal("Creek path", imported.Name);
        Assert.Contains("Ride with GPS", imported.Provenance, StringComparison.Ordinal);
        Assert.Equal(WaypointType.Start, imported.Waypoints[0].WaypointType);
        Assert.Contains("parking lot", imported.Waypoints[0].TextDescription, StringComparison.Ordinal);
        var water = imported.Waypoints.Single(point => point.WaypointType == WaypointType.Announcement);
        Assert.Equal("Water: left side", water.TextDescription);
        Assert.Equal(40.021, water.Latitude, 5);
        Assert.Equal(WaypointType.Finish, imported.Waypoints[^1].WaypointType);
    }

    [Theory]
    [InlineData(0, WaypointType.Continue)]
    [InlineData(19, WaypointType.Continue)]
    [InlineData(-19, WaypointType.Continue)]
    [InlineData(20, WaypointType.BearRight)]
    [InlineData(-20, WaypointType.BearLeft)]
    [InlineData(59, WaypointType.BearRight)]
    [InlineData(60, WaypointType.TurnRight)]
    [InlineData(-90, WaypointType.TurnLeft)]
    [InlineData(149, WaypointType.TurnRight)]
    [InlineData(150, WaypointType.TurnAround)]
    [InlineData(-180, WaypointType.TurnAround)]
    public void Classify_turn_uses_heading_change(double degrees, WaypointType expected) =>
        Assert.Equal(expected, GpxCourse.ClassifyTurn(degrees));

    [Fact]
    public void Thinning_drops_a_straight_line_and_keeps_a_heading_change()
    {
        var straight = new List<(double Latitude, double Longitude)>();
        for (var i = 0; i <= 40; i++)
            straight.Add(GeoMath.Translate(40, -105, 0, i * 0.005));
        var thinned = GpxCourse.Import(Track("Straight", straight, 1000), new GpxImportOptions { ToleranceMeters = 10 });
        Assert.Equal(2, thinned.Waypoints.Count);
        Assert.Equal(WaypointType.Start, thinned.Waypoints[0].WaypointType);
        Assert.Equal(WaypointType.Finish, thinned.Waypoints[1].WaypointType);

        var corner = GeoMath.Translate(40, -105, 0, 0.01);
        var gentleEnd = GeoMath.Translate(corner.Latitude, corner.Longitude, 10, 0.01);
        var sharpEnd = GeoMath.Translate(corner.Latitude, corner.Longitude, 25, 0.01);
        var gentle = GpxCourse.Import(Track("Gentle", [(40, -105), corner, gentleEnd], 1000), new GpxImportOptions { ToleranceMeters = 15 });
        var sharp = GpxCourse.Import(Track("Sharp", [(40, -105), corner, sharpEnd], 1000), new GpxImportOptions { ToleranceMeters = 15 });
        Assert.Equal(2, gentle.Waypoints.Count);
        Assert.Equal(3, sharp.Waypoints.Count);
        Assert.Equal(WaypointType.BearRight, sharp.Waypoints[1].WaypointType);

        Assert.Equal(GpxCourse.DefaultToleranceMeters, GpxCourse.ClampTolerance(0));
        Assert.Equal(GpxCourse.MinToleranceMeters, GpxCourse.ClampTolerance(4));
        Assert.Equal(12, GpxCourse.ClampTolerance(12));
        Assert.Equal(GpxCourse.MaxToleranceMeters, GpxCourse.ClampTolerance(100));
    }

    [Fact]
    public void Totals_use_smoothed_altitude_and_cumulative_distance()
    {
        var first = (Latitude: 40.0, Longitude: -105.0);
        var second = GeoMath.Translate(first.Latitude, first.Longitude, 0, 0.2);
        var third = GeoMath.Translate(second.Latitude, second.Longitude, 90, 0.2);
        var fourth = GeoMath.Translate(third.Latitude, third.Longitude, 0, 0.2);
        var points = new[] { first, second, third, fourth };
        var elevations = new[] { 100.0, 250.0, 100.0, 100.0 };
        var imported = GpxCourse.Import(Track("Climb", points, elevations));

        Assert.Equal(4, imported.Waypoints.Count);
        var expectedDistance = 0.0;
        for (var i = 1; i < imported.Waypoints.Count; i++)
        {
            expectedDistance += GeoMath.DistanceMeters(
                imported.Waypoints[i - 1].Latitude, imported.Waypoints[i - 1].Longitude,
                imported.Waypoints[i].Latitude, imported.Waypoints[i].Longitude) / 1000.0;
        }
        Assert.Equal(expectedDistance, imported.ElapsedDistanceKm, 6);
        Assert.InRange(imported.ElapsedDistanceKm, 0.59, 0.61);
        Assert.Equal(0.05, imported.AltitudeGainKm, 6);
        Assert.Equal(0.05, imported.AltitudeLossKm, 6);
        Assert.Equal(0.15, imported.Waypoints[1].AltitudeKm, 6);
    }

    [Fact]
    public void Round_trip_restores_waypoints_and_intervals_without_thinning()
    {
        var courseId = Guid.NewGuid();
        var line = new List<Waypoint>();
        for (var i = 0; i < 8; i++)
        {
            var point = GeoMath.Translate(40, -105, 0, i * 0.002);
            line.Add(new Waypoint
            {
                CourseId = courseId,
                TimestampTicks = 10_000 + i,
                Latitude = point.Latitude,
                Longitude = point.Longitude,
                AltitudeKm = 1.5 + i * 0.001,
                ElapsedDistanceKm = i * 0.002,
                HeadingDegrees = i == 7 ? 0 : 1.5,
                WaypointType = i switch
                {
                    0 => WaypointType.Start,
                    7 => WaypointType.Finish,
                    3 => WaypointType.Announcement,
                    _ => WaypointType.Continue
                },
                TextDescription = i == 3 ? "Cattle guard" : ""
            });
        }
        var placed = IntervalCourse.Place(line, courseId, "Wall", 0.004, 0.010);
        const string publicUrl = "https://maps.example.test";
        var xml = GpxCourse.Export("North", placed.Waypoints, [placed.Interval], publicUrl);
        Assert.Contains("https://maps.example.test/xmlschemas/gpx/1", xml, StringComparison.Ordinal);
        Assert.Contains("<trkpt", xml, StringComparison.Ordinal);
        GpxCourse.Validate(xml);

        var imported = GpxCourse.Import(xml);
        Assert.True(imported.HasGpsTrainerExtensions);
        Assert.False(imported.Publish);
        Assert.Equal(placed.Waypoints.Count, imported.Waypoints.Count);
        for (var i = 0; i < placed.Waypoints.Count; i++)
        {
            var expected = placed.Waypoints[i];
            var actual = imported.Waypoints[i];
            Assert.Equal(expected.WaypointType, actual.WaypointType);
            Assert.Equal(expected.TimestampTicks, actual.TimestampTicks);
            Assert.Equal(expected.Latitude, actual.Latitude, 6);
            Assert.Equal(expected.Longitude, actual.Longitude, 6);
            Assert.Equal(expected.AltitudeKm, actual.AltitudeKm, 5);
            Assert.Equal(expected.ElapsedDistanceKm, actual.ElapsedDistanceKm, 5);
            Assert.Equal(expected.HeadingDegrees, actual.HeadingDegrees, 3);
            Assert.Equal(expected.TextDescription ?? "", actual.TextDescription ?? "");
        }
        var interval = Assert.Single(imported.Intervals);
        Assert.Equal(placed.Interval.IntervalId, interval.IntervalId);
        Assert.Equal(placed.Interval.Ordinal, interval.Ordinal);
        Assert.Equal("Wall", interval.Name);
        Assert.Equal(placed.Interval.StartElapsedKm, interval.StartElapsedKm, 5);
        Assert.Equal(placed.Interval.FinishElapsedKm, interval.FinishElapsedKm, 5);
        Assert.Equal(placed.Interval.StartTimestampTicks, interval.StartTimestampTicks);
        Assert.Equal(placed.Interval.FinishTimestampTicks, interval.FinishTimestampTicks);
        Assert.Equal(placed.Interval.StartLatitude, interval.StartLatitude, 5);
        Assert.Equal(placed.Interval.FinishLongitude, interval.FinishLongitude, 5);

        var legacy = xml.Replace(GpxCourse.ExtensionNamespace(publicUrl), GpxCourse.LegacyExtensionNamespace, StringComparison.Ordinal);
        var fromLegacy = GpxCourse.Import(legacy);
        Assert.True(fromLegacy.HasGpsTrainerExtensions);
        Assert.Equal(imported.Waypoints.Count, fromLegacy.Waypoints.Count);
        Assert.Equal(imported.Waypoints.Select(point => point.WaypointType), fromLegacy.Waypoints.Select(point => point.WaypointType));
        Assert.Equal(interval.IntervalId, Assert.Single(fromLegacy.Intervals).IntervalId);
    }

    [Fact]
    public void Preview_waypoints_load_into_the_road_sketch_and_stay_editable()
    {
        var imported = GpxCourse.Import(Load("strava.gpx"), new GpxImportOptions { FileName = "morning.gpx" });
        var editorCourseId = Guid.NewGuid();
        var course = new Course
        {
            CourseId = editorCourseId,
            Name = imported.Name,
            Comments = imported.Provenance ?? "",
            Login = "rider",
            Publish = false,
            ElapsedDistanceKm = imported.ElapsedDistanceKm,
            AltitudeGainKm = imported.AltitudeGainKm,
            AltitudeLossKm = imported.AltitudeLossKm
        };

        var sketch = new CourseSketch();
        sketch.Replace(imported.Waypoints);
        Assert.False(course.Publish);
        Assert.Equal(editorCourseId, course.CourseId);
        Assert.Equal(imported.Provenance, course.Comments);
        Assert.Equal(imported.Waypoints.Count, sketch.Waypoints.Count);
        Assert.Equal(imported.Waypoints.Select(point => (point.Latitude, point.Longitude, point.WaypointType)), sketch.Waypoints.Select(point => (point.Latitude, point.Longitude, point.WaypointType)));

        var importedCount = sketch.Waypoints.Count;
        var last = sketch.LastRoutePoint();
        Assert.NotNull(last);
        sketch.AddClick(last.Latitude + 0.002, last.Longitude + 0.002, WaypointType.Announcement, followRoads: false, route: null);
        Assert.Equal(importedCount + 1, sketch.Waypoints.Count);
        Assert.Equal(WaypointType.Start, sketch.Waypoints[0].WaypointType);
        Assert.Equal(imported.Waypoints[0].Latitude, sketch.Waypoints[0].Latitude);
        Assert.Equal(WaypointType.Announcement, sketch.Waypoints[^1].WaypointType);
        Assert.True(sketch.Undo());
        Assert.Equal(importedCount, sketch.Waypoints.Count);
        Assert.Equal(imported.Waypoints.Select(point => point.WaypointType), sketch.Waypoints.Select(point => point.WaypointType));
    }

    [Fact]
    public void Malicious_xml_and_unsupported_versions_are_rejected()
    {
        var bomb = "<!DOCTYPE gpx [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]><gpx>&xxe;</gpx>";
        Assert.ThrowsAny<XmlException>(() => GpxCourse.Import(bomb));
        var expansion = "<!DOCTYPE gpx [<!ENTITY x \"hi\">]><gpx version=\"1.1\">&x;</gpx>";
        Assert.ThrowsAny<XmlException>(() => GpxCourse.Import(expansion));
        Assert.Throws<InvalidOperationException>(() => GpxCourse.Import("<gpx version=\"1.2\" xmlns=\"http://www.topografix.com/GPX/1/1\"></gpx>"));
        Assert.Throws<InvalidOperationException>(() => GpxCourse.Import("<gpx version=\"1.1\" xmlns=\"http://www.topografix.com/GPX/1/1\"></gpx>"));
        Assert.Throws<InvalidOperationException>(() => GpxCourse.Import(new string('a', GpxCourse.MaxCharacters + 1)));
    }

    private static string Load(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static string Track(string name, IReadOnlyList<(double Latitude, double Longitude)> points, double elevationMeters) =>
        Track(name, points, Enumerable.Repeat(elevationMeters, points.Count).ToArray());

    private static string Track(string name, IReadOnlyList<(double Latitude, double Longitude)> points, IReadOnlyList<double> elevationMeters)
    {
        var rows = string.Join("", points.Select((point, index) =>
            $"<trkpt lat=\"{point.Latitude.ToString("0.0000000", CultureInfo.InvariantCulture)}\" lon=\"{point.Longitude.ToString("0.0000000", CultureInfo.InvariantCulture)}\"><ele>{elevationMeters[index].ToString(CultureInfo.InvariantCulture)}</ele></trkpt>"));
        return $"""
            <gpx version="1.1" creator="fixture" xmlns="{GpxCourse.GpxNamespace}">
              <trk><name>{name}</name><trkseg>{rows}</trkseg></trk>
            </gpx>
            """;
    }
}
