namespace GpsTrainer.Shared.Tests;

public class EditorPortTests
{
    [Fact]
    public void String_helpers_match_the_course_editor()
    {
        Assert.False(EditorText.IsEmpty("asdf"));
        Assert.True(EditorText.IsEmpty(""));
        Assert.False(EditorText.IsNumeric(""));
        Assert.True(EditorText.IsNumeric("0"));
        Assert.True(EditorText.IsNumeric("."));
        Assert.True(EditorText.IsNumeric("1."));
        Assert.False(EditorText.IsNumeric("a"));
        Assert.Equal(0.001, EditorText.ParseDistanceKm("1 m"));
        Assert.Equal(1, EditorText.ParseDistanceKm("1 km"));
        Assert.Equal(0.002, EditorText.ParseDistanceKm("2 m"));
        Assert.Equal(2, EditorText.ParseDistanceKm("2 km"));
        Assert.Null(EditorText.ParseDistanceKm("2"));
        Assert.Equal(2, EditorText.ParseDistanceKm("2km"));
        Assert.Equal(10, EditorText.ParseDistanceKm("10 km"));
        Assert.Equal(EditorText.MilesToKilos(1), EditorText.ParseDistanceKm("1 mi"));
        Assert.Equal(0.002, EditorText.ParseDistanceKm("2m"));
        Assert.Equal(1, EditorText.ParseDistanceKm("1000m"));
        Assert.True(Math.Abs(EditorText.ParseDistanceKm("2ft")!.Value - 0.0006096) < 0.0001);
        Assert.True(Math.Abs(EditorText.ParseDistanceKm("2mi")!.Value - 3.218688) < 0.0001);
        Assert.False(EditorText.EndsWithSuffix("", ""));
        Assert.True(EditorText.EndsWithSuffix("abc", "bc"));
        Assert.True(EditorText.EndsWithSuffix("abc", "abc"));
        Assert.True(EditorText.EndsWithSuffix("a+c", "a+c"));
        Assert.True(EditorText.EndsWithSuffix("a.c", ".c"));
        Assert.Equal("", EditorText.StripHtml(""));
        Assert.Equal("", EditorText.StripHtml("<h>"));
        Assert.Equal("dog", EditorText.StripHtml("<h>dog</h>"));
        Assert.Equal("Elapsed", EditorText.Format("Elapsed", 10));
        Assert.Equal("Elapsed", EditorText.Format("Elapsed"));
        Assert.Equal("Elapsed{0}", EditorText.Format("Elapsed{0}"));
        Assert.Equal("Elapsed10", EditorText.Format("Elapsed{0}", 10));
    }

    [Fact]
    public void Number_helpers_match_the_course_editor()
    {
        Assert.Equal("03", EditorText.Pad(3, 2));
        Assert.Equal("003", EditorText.Pad(3, 3));
        Assert.Equal(Math.PI, EditorText.ToRadians(180));
        Assert.Equal(0, EditorText.ToRadians(0));
        Assert.Equal(0, EditorText.ToDegrees(0));
        Assert.Equal(1.0 / Math.PI, EditorText.ToDegrees(1.0 / 180));
    }

    [Fact]
    public void TimeSpan_helpers_match_the_course_editor()
    {
        Assert.Equal(EditorTimeSpan.TicksPerSecond, EditorTimeSpan.Parse("00:00:01").Ticks);
        Assert.Equal(EditorTimeSpan.TicksPerSecond * (3600 + 123), EditorTimeSpan.Parse("01:02:03").Ticks);
        Assert.Equal("00:00:01", EditorTimeSpan.Parse("00:00:01").ToString());
        Assert.Equal("01:02:03", EditorTimeSpan.Parse("01:02:03").ToString());
        Assert.Equal("100:00:00", EditorTimeSpan.Parse("100:00:00").ToString());
        Assert.Equal("05:00:00", EditorTimeSpan.FromHours(5).ToString());
        Assert.Equal("10:00:00", EditorTimeSpan.FromHours(10).ToString());
        Assert.Equal("20:00:00", EditorTimeSpan.FromHours(20).ToString());
        Assert.Equal("30:00:00", EditorTimeSpan.FromHours(30).ToString());
        Assert.Equal("100:00:00", EditorTimeSpan.FromHours(100).ToString());
        Assert.Equal(100, EditorTimeSpan.FromHours(100).TotalHours());
        Assert.Equal(60L * 60 * 1000 * 10000, EditorTimeSpan.FromHours(1).Ticks);
    }

    [Fact]
    public void Position_distance_is_about_111_km_per_degree()
    {
        var p1 = new EditorPin { Latitude = 40, Longitude = -104 };
        var p2 = new EditorPin { Latitude = 41, Longitude = -104 };
        Assert.Equal(111.2, Math.Round(p1.DistanceTo(p2) * 10) / 10);
    }

    [Fact]
    public void Course_insert_delete_and_move_match_TestCourse()
    {
        var course = new EditorCourse { Name = "test" };
        Assert.Equal(0, Math.Round(course.ElapsedDistanceKm));
        Assert.True(course.NoWaypointsEntered);
        Assert.Null(course.Start);
        Assert.Null(course.Finish);

        var wp1 = Pin(40, -104, WaypointType.Start, "wp1");
        var wp2 = At(wp1, 0, 1, WaypointType.Continue, "wp2");
        var wp3 = At(wp2, 0, 1, WaypointType.Finish, "wp3");
        course.Insert(null, wp1);
        course.Insert(wp1, wp2);
        course.Insert(wp2, wp3);
        var wp4 = At(wp1, 0, 0.5, WaypointType.StartInterval, "wp4");
        course.Insert(wp1, wp4);

        var names = course.Waypoints().Select(p => p.TextDescription).ToArray();
        Assert.Equal(new[] { "wp1", "wp4", "wp2", "wp3" }, names);
        Assert.Same(wp1, course.FindClosest(wp1.Longitude, wp1.Latitude));
        Assert.Same(wp2, course.FindClosest(wp2.Longitude, wp2.Latitude));
        Assert.Same(wp3, course.FindClosest(wp3.Longitude, wp3.Latitude));
        Assert.Same(wp4, course.FindClosest(wp4.Longitude, wp4.Latitude));

        var forward = course.MoveAlongRoute(wp4, 0.6);
        Assert.Same(wp2, course.FindClosest(forward!.Longitude, forward.Latitude));
        var back = course.MoveAlongRoute(wp4, -0.6);
        Assert.Same(wp1, course.FindClosest(back!.Longitude, back.Latitude));
        var end = course.MoveAlongRoute(wp4, 3);
        Assert.Same(wp3, course.FindClosest(end!.Longitude, end.Latitude));

        var one = new EditorCourse { Name = "test" };
        var only = new EditorPin { TimestampTicks = 0, Latitude = 40, Longitude = -104, WaypointType = WaypointType.Start };
        one.Insert(null, only);
        Assert.Equal("Course is too short", one.Validate());
        Assert.False(one.NoWaypointsEntered);
        Assert.Null(one.CurrentRouteSegment());
        one.Select(only);
        Assert.Equal(0, one.CurrentRouteSegment());
        Assert.Same(only, one.Start);
        Assert.Same(only, one.Finish);
        Assert.Null(one.Next(only));
        Assert.Null(one.Previous(only));

        var finish = At(only, 0, 1, WaypointType.Finish, "");
        finish.TimestampTicks = 1;
        one.Insert(only, finish);
        Assert.Equal("", one.Validate());
        Assert.Equal(1, Math.Round(one.ElapsedDistanceKm));
        Assert.Same(only, one.Start);
        Assert.Same(finish, one.Finish);
        Assert.Same(finish, one.Next(only));
        Assert.Same(only, one.Previous(finish));
        Assert.Equal(0, one.AltitudeGainKm());
        Assert.Equal(0, one.AltitudeLossKm());

        one.Delete(only);
        Assert.Same(finish, one.Start);
        Assert.Same(finish, one.Finish);
        Assert.Equal(0, Math.Round(one.ElapsedDistanceKm));

        one.Insert(null, only);
        Assert.Equal(1, Math.Round(one.ElapsedDistanceKm));
        one.Delete(finish);
        Assert.Same(only, one.Start);
        Assert.Equal(WaypointType.Start, only.WaypointType);

        var three = new EditorCourse { Name = "test" };
        var a = new EditorPin { TimestampTicks = 0, Latitude = 40, Longitude = -104, WaypointType = WaypointType.Start };
        var b = At(a, 0, 1, WaypointType.Continue, "");
        b.TimestampTicks = 1;
        var c = At(b, 0, 1, WaypointType.Finish, "");
        c.TimestampTicks = 2;
        three.Insert(null, a);
        three.Insert(a, b);
        three.Insert(b, c);
        Assert.Equal(2, Math.Round(three.ElapsedDistanceKm));
        Assert.Equal(0, three.RouteIndex(a));
        Assert.Equal(1, three.RouteIndex(b));
        Assert.Equal(2, three.RouteIndex(c));
        three.Delete(b);
        Assert.Same(c, three.Next(a));
        Assert.Same(a, three.Previous(c));
        Assert.Equal(2, Math.Round(three.ElapsedDistanceKm));

        var start = new EditorPin { Latitude = 40, Longitude = -105, WaypointType = WaypointType.StartInterval, TimestampTicks = 10 };
        var endPin = new EditorPin { Latitude = 40.01, Longitude = -105, WaypointType = WaypointType.FinishInterval, TimestampTicks = 20 };
        var marked = new EditorCourse { Name = "intervals" };
        marked.Append(new EditorPin { TimestampTicks = 1, Latitude = 40, Longitude = -105.01, WaypointType = WaypointType.Start });
        marked.Append(start);
        marked.Append(endPin);
        marked.Append(new EditorPin { TimestampTicks = 30, Latitude = 40.02, Longitude = -105, WaypointType = WaypointType.Finish });
        Assert.Same(start, marked.FindStartInterval(endPin));
        Assert.Same(endPin, marked.FindFinishInterval(start));
        Assert.Equal(1, EditorCourse.CountType(marked.Waypoints(), WaypointType.StartInterval));
    }

    private static EditorPin Pin(double lat, double lon, WaypointType type, string text) => new()
    {
        Latitude = lat,
        Longitude = lon,
        WaypointType = type,
        TextDescription = text
    };

    private static EditorPin At(EditorPin from, double bearing, double km, WaypointType type, string text)
    {
        var moved = from.TranslateTo(bearing, km);
        moved.WaypointType = type;
        moved.TextDescription = text;
        return moved;
    }
}
