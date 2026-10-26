using System.Globalization;

namespace GpsTrainer;

public sealed record IntervalSplit(long StartTicks, long FinishTicks)
{
    public TimeSpan Time => new(FinishTicks - StartTicks);
    public string TimeString => IntervalClock.Format(Time);
    public DateTime StartTime => new(StartTicks, DateTimeKind.Utc);
}

/// <summary>
/// Pairs <c>StartInterval</c> / <c>FinishInterval</c> waypoints the way
/// <c>DataLayer.GetIntervals(courseId)</c> did, then times a race by walking its
/// positions with <see cref="TrackInterpolation"/>.
/// </summary>
public static class CourseIntervals
{
    public static IReadOnlyList<(Waypoint Start, Waypoint Finish)> Pair(IEnumerable<Waypoint> waypoints)
    {
        var marks = waypoints
            .Where(w => w.WaypointType is WaypointType.StartInterval or WaypointType.FinishInterval)
            .OrderBy(w => w.TimestampTicks)
            .ToList();
        var pairs = new List<(Waypoint Start, Waypoint Finish)>();
        for (var i = 0; i + 1 < marks.Count; i += 2)
            pairs.Add((marks[i], marks[i + 1]));
        return pairs;
    }

    public static IntervalSplit? Measure(Waypoint start, Waypoint finish, IReadOnlyList<TrackPoint> track)
    {
        if (track.Count < 2)
            return null;
        var ordered = track.OrderBy(p => p.TimestampTicks).ToList();
        var targetLat = start.Latitude;
        var targetLon = start.Longitude;
        long? startTicks = null;
        for (var i = 1; i < ordered.Count; i++)
        {
            var prev = ordered[i - 1];
            var cur = ordered[i];
            var hit = TrackInterpolation.Interpolate(
                new GeoPosition(prev.Latitude, prev.Longitude, false),
                new GeoPosition(cur.Latitude, cur.Longitude, false),
                new GeoPosition(targetLat, targetLon, false),
                new DateTime(prev.TimestampTicks, DateTimeKind.Utc),
                new DateTime(cur.TimestampTicks, DateTimeKind.Utc));
            if (hit == DateTime.MinValue)
                continue;
            if (startTicks is null)
            {
                startTicks = hit.Ticks;
                targetLat = finish.Latitude;
                targetLon = finish.Longitude;
            }
            else
            {
                return new IntervalSplit(startTicks.Value, hit.Ticks);
            }
        }
        return null;
    }
}

public static class IntervalClock
{
    public static string Format(TimeSpan ts)
    {
        var sign = "";
        if (ts.TotalMilliseconds < 0)
        {
            sign = "-";
            ts = ts.Negate();
        }

        var stamp = new DateTime((long)ts.TotalMilliseconds * 10_000, DateTimeKind.Unspecified);
        if (ts.TotalHours < 1.0)
            return sign + "00:" + stamp.ToString("mm:ss", CultureInfo.InvariantCulture);
        return sign + stamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }
}
