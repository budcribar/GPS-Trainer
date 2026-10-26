using System.Xml;

namespace GpsTrainer.Shared.Tests;

public class IntervalTests
{
    [Fact]
    public void Place_move_and_reproject_follow_the_old_marker_pair()
    {
        var courseId = Guid.NewGuid();
        var route = Line(courseId);
        var placed = IntervalCourse.Place(route, courseId, "Climb", 1, 2);
        Assert.Equal("Climb", placed.Interval.Name);
        Assert.Equal(1, placed.Interval.StartElapsedKm, 3);
        Assert.Equal(2, placed.Interval.FinishElapsedKm, 3);
        Assert.Contains(placed.Waypoints, w => w.TimestampTicks == placed.Interval.StartTimestampTicks && w.WaypointType == WaypointType.StartInterval);
        Assert.Contains(placed.Waypoints, w => w.TimestampTicks == placed.Interval.FinishTimestampTicks && w.WaypointType == WaypointType.FinishInterval);
        Assert.Equal("Climb", IntervalCourse.Describe(placed.Waypoints).Single().Split(':')[0]);

        var oldStart = placed.Interval.StartTimestampTicks;
        var oldFinish = placed.Interval.FinishTimestampTicks;
        var moved = IntervalCourse.Move(placed.Waypoints, placed.Interval, 0.5, 1.5);
        Assert.DoesNotContain(moved, w => w.TimestampTicks == oldStart);
        Assert.DoesNotContain(moved, w => w.TimestampTicks == oldFinish);
        Assert.Equal(0.5, placed.Interval.StartElapsedKm, 3);
        Assert.Equal(1.5, placed.Interval.FinishElapsedKm, 3);
        Assert.Throws<ArgumentException>(() => IntervalCourse.Move(route, placed.Interval, 0.2, 0.4));

        var shifted = Line(courseId);
        shifted[1] = new Waypoint
        {
            CourseId = courseId,
            TimestampTicks = 2,
            Latitude = shifted[1].Latitude,
            Longitude = shifted[1].Longitude + 0.02,
            ElapsedDistanceKm = 1,
            WaypointType = WaypointType.Continue
        };
        var before = (placed.Interval.StartLatitude, placed.Interval.StartLongitude);
        IntervalCourse.Reproject(shifted, placed.Interval);
        Assert.Equal(0.5, placed.Interval.StartElapsedKm, 3);
        Assert.NotEqual(before.StartLongitude, placed.Interval.StartLongitude);
        var onRoute = IntervalCourse.AtDistance(IntervalCourse.RouteLine(shifted), 0.5);
        Assert.Equal(onRoute.Latitude, placed.Interval.StartLatitude, 6);
        Assert.Equal(onRoute.Longitude, placed.Interval.StartLongitude, 6);
    }

    [Fact]
    public void Gpx_round_trip_keeps_intervals_in_the_extension_namespace()
    {
        var courseId = Guid.NewGuid();
        var placed = IntervalCourse.Place(Line(courseId), courseId, "Wall", 1, 2);
        var xml = GpxCourse.Export("North", placed.Waypoints, [placed.Interval]);
        GpxCourse.Validate(xml);
        Assert.Contains("http://www.topografix.com/GPX/1/1", xml, StringComparison.Ordinal);
        Assert.Contains(GpxCourse.IntervalNamespace, xml, StringComparison.Ordinal);
        Assert.Contains("<gpst:interval", xml, StringComparison.Ordinal);
        Assert.Contains("version=\"1.1\"", xml, StringComparison.Ordinal);

        var imported = GpxCourse.Import(xml);
        Assert.True(imported.HasGpsTrainerExtensions);
        Assert.Equal("North", imported.Name);
        Assert.Equal(placed.Waypoints.Count, imported.Waypoints.Count);
        Assert.Equal(WaypointType.Start, imported.Waypoints[0].WaypointType);
        Assert.Equal(WaypointType.StartInterval, imported.Waypoints.Single(w => w.TextDescription == "Wall").WaypointType);
        var interval = Assert.Single(imported.Intervals);
        Assert.Equal(placed.Interval.IntervalId, interval.IntervalId);
        Assert.Equal("Wall", interval.Name);
        Assert.Equal(placed.Interval.StartElapsedKm, interval.StartElapsedKm, 5);
        Assert.Equal(placed.Interval.FinishElapsedKm, interval.FinishElapsedKm, 5);
        Assert.Equal(placed.Interval.StartTimestampTicks, interval.StartTimestampTicks);
        Assert.Equal(placed.Interval.StartLatitude, interval.StartLatitude, 5);
        Assert.Equal(placed.Interval.FinishLongitude, interval.FinishLongitude, 5);

        var bomb = "<!DOCTYPE gpx [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]><gpx>&xxe;</gpx>";
        Assert.ThrowsAny<XmlException>(() => GpxCourse.Import(bomb));
    }

    private static List<Waypoint> Line(Guid courseId)
    {
        var pins = new List<Waypoint>();
        for (var i = 0; i < 4; i++)
        {
            var point = GeoMath.Translate(40, -105, 0, i);
            pins.Add(new Waypoint
            {
                CourseId = courseId,
                TimestampTicks = i + 1,
                Latitude = point.Latitude,
                Longitude = point.Longitude,
                ElapsedDistanceKm = i,
                WaypointType = i == 0 ? WaypointType.Start : i == 3 ? WaypointType.Finish : WaypointType.Continue
            });
        }
        return pins;
    }
}
