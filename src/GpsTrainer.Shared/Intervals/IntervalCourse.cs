namespace GpsTrainer;

/// <summary>
/// A course interval stored on its own, not only as a pair of loose waypoints.
/// The recovered <c>Interval</c> was that pair: a <c>StartInterval</c> waypoint and the
/// following <c>FinishInterval</c> waypoint, ordered by <c>TimestampTicks</c>.
/// <see cref="StartElapsedKm"/> and <see cref="FinishElapsedKm"/> are the same
/// elapsed-distance anchors those waypoints carried, so a route edit can slide the
/// markers back onto the polyline.
/// </summary>
public sealed class CourseIntervalDefinition
{
    public Guid IntervalId { get; set; }
    public Guid CourseId { get; set; }
    public int Ordinal { get; set; }
    public string Name { get; set; } = "";
    public double StartElapsedKm { get; set; }
    public double FinishElapsedKm { get; set; }
    public long StartTimestampTicks { get; set; }
    public long FinishTimestampTicks { get; set; }
    public double StartLatitude { get; set; }
    public double StartLongitude { get; set; }
    public double FinishLatitude { get; set; }
    public double FinishLongitude { get; set; }
    public double StartAltitudeKm { get; set; }
    public double FinishAltitudeKm { get; set; }
    public double StartHeadingDegrees { get; set; }
    public double FinishHeadingDegrees { get; set; }

    public double LengthKm => FinishElapsedKm - StartElapsedKm;
}

/// <summary>
/// Strava workouts are not stored. If one is ever needed it is built from
/// <see cref="CourseIntervalDefinition"/> at the moment of export.
/// </summary>
public interface IStravaWorkoutExporter
{
    string Describe(IReadOnlyList<CourseIntervalDefinition> intervals);
}

public readonly record struct RouteSample(double Latitude, double Longitude, double ElapsedKm, double AltitudeKm, double HeadingDegrees);

/// <summary>
/// Places, moves, and reprojects intervals on a course polyline.
/// <c>MoveIntervalAlongRoute</c> deleted the old marker ticks and inserted the new ones.
/// <see cref="Move"/> does that, after computing the new points with the same
/// elapsed-distance walk the editor used.
/// </summary>
public static class IntervalCourse
{
    public static List<Waypoint> EnsureElapsed(IReadOnlyList<Waypoint> waypoints)
    {
        var ordered = waypoints.OrderBy(w => w.TimestampTicks).ToList();
        if (ordered.Count == 0)
            return ordered;
        if (ordered.Any(w => w.ElapsedDistanceKm > 0))
            return ordered.OrderBy(w => w.ElapsedDistanceKm).ThenBy(w => w.TimestampTicks).ToList();
        var distance = 0.0;
        for (var i = 0; i < ordered.Count; i++)
        {
            if (i > 0)
            {
                distance += GeoMath.DistanceMeters(
                    ordered[i - 1].Latitude, ordered[i - 1].Longitude,
                    ordered[i].Latitude, ordered[i].Longitude) / 1000.0;
            }
            ordered[i].ElapsedDistanceKm = distance;
        }
        return ordered;
    }

    public static List<Waypoint> RouteLine(IEnumerable<Waypoint> waypoints) =>
        EnsureElapsed(waypoints.Where(w => w.WaypointType is not WaypointType.StartInterval and not WaypointType.FinishInterval).ToList());

    public static IReadOnlyList<CourseIntervalDefinition> FromMarkers(Guid courseId, IEnumerable<Waypoint> waypoints)
    {
        var pairs = CourseIntervals.Pair(waypoints);
        var list = new List<CourseIntervalDefinition>();
        var ordinal = 1;
        foreach (var (start, finish) in pairs)
        {
            list.Add(new CourseIntervalDefinition
            {
                IntervalId = Guid.NewGuid(),
                CourseId = courseId,
                Ordinal = ordinal++,
                Name = start.TextDescription ?? "",
                StartElapsedKm = start.ElapsedDistanceKm,
                FinishElapsedKm = finish.ElapsedDistanceKm,
                StartTimestampTicks = start.TimestampTicks,
                FinishTimestampTicks = finish.TimestampTicks,
                StartLatitude = start.Latitude,
                StartLongitude = start.Longitude,
                FinishLatitude = finish.Latitude,
                FinishLongitude = finish.Longitude,
                StartAltitudeKm = start.AltitudeKm,
                FinishAltitudeKm = finish.AltitudeKm,
                StartHeadingDegrees = start.HeadingDegrees,
                FinishHeadingDegrees = finish.HeadingDegrees
            });
        }
        return list;
    }

    public static RouteSample AtDistance(IReadOnlyList<Waypoint> route, double elapsedKm)
    {
        var line = EnsureElapsed(route);
        if (line.Count == 0)
            throw new ArgumentException("The course has no waypoints.");
        if (elapsedKm <= line[0].ElapsedDistanceKm)
            return Sample(line[0], line.Count > 1 ? line[1] : line[0], line[0].ElapsedDistanceKm);
        if (elapsedKm >= line[^1].ElapsedDistanceKm)
            return Sample(line[^1], line[^1], line[^1].ElapsedDistanceKm);
        for (var i = 1; i < line.Count; i++)
        {
            if (line[i].ElapsedDistanceKm < elapsedKm)
                continue;
            var previous = line[i - 1];
            var next = line[i];
            var span = next.ElapsedDistanceKm - previous.ElapsedDistanceKm;
            var fraction = span <= 0 ? 0 : (elapsedKm - previous.ElapsedDistanceKm) / span;
            var heading = GeoMath.InitialBearingDegrees(previous.Latitude, previous.Longitude, next.Latitude, next.Longitude);
            return new RouteSample(
                previous.Latitude + (next.Latitude - previous.Latitude) * fraction,
                previous.Longitude + (next.Longitude - previous.Longitude) * fraction,
                elapsedKm,
                previous.AltitudeKm + (next.AltitudeKm - previous.AltitudeKm) * fraction,
                heading);
        }
        return Sample(line[^1], line[^1], line[^1].ElapsedDistanceKm);
    }

    public static RouteSample Nearest(IReadOnlyList<Waypoint> route, double latitude, double longitude)
    {
        var line = EnsureElapsed(route);
        if (line.Count == 0)
            throw new ArgumentException("The course has no waypoints.");
        if (line.Count == 1)
            return Sample(line[0], line[0], line[0].ElapsedDistanceKm);
        var best = double.MaxValue;
        var chosen = Sample(line[0], line[1], line[0].ElapsedDistanceKm);
        for (var i = 1; i < line.Count; i++)
        {
            var hit = Project(line[i - 1], line[i], latitude, longitude);
            if (hit.DistanceMeters < best)
            {
                best = hit.DistanceMeters;
                chosen = hit.Sample;
            }
        }
        return chosen;
    }

    public static (IReadOnlyList<Waypoint> Waypoints, CourseIntervalDefinition Interval) Place(
        IReadOnlyList<Waypoint> waypoints, Guid courseId, string name, double startKm, double finishKm)
    {
        if (finishKm <= startKm)
            throw new ArgumentException("Finish must be after the start.");
        var route = RouteLine(waypoints);
        var start = AtDistance(route, startKm);
        var finish = AtDistance(route, finishKm);
        var used = route.Select(w => w.TimestampTicks).ToHashSet();
        var startTicks = FreshTick(used);
        var finishTicks = FreshTick(used);
        var interval = new CourseIntervalDefinition
        {
            IntervalId = Guid.NewGuid(),
            CourseId = courseId,
            Ordinal = CourseIntervals.Pair(waypoints).Count + 1,
            Name = name ?? "",
            StartElapsedKm = start.ElapsedKm,
            FinishElapsedKm = finish.ElapsedKm,
            StartTimestampTicks = startTicks,
            FinishTimestampTicks = finishTicks,
            StartLatitude = start.Latitude,
            StartLongitude = start.Longitude,
            FinishLatitude = finish.Latitude,
            FinishLongitude = finish.Longitude,
            StartAltitudeKm = start.AltitudeKm,
            FinishAltitudeKm = finish.AltitudeKm,
            StartHeadingDegrees = start.HeadingDegrees,
            FinishHeadingDegrees = finish.HeadingDegrees
        };
        return (WithMarkers(route, FromMarkers(courseId, waypoints).Append(interval).ToList()), interval);
    }

    /// <summary>
    /// Same effect as <c>WebService.MoveIntervalAlongRoute</c>: the previous marker ticks
    /// are removed and the new start and finish are inserted on the route.
    /// </summary>
    public static IReadOnlyList<Waypoint> Move(
        IReadOnlyList<Waypoint> waypoints, CourseIntervalDefinition interval, double newStartKm, double newFinishKm)
    {
        if (newFinishKm <= newStartKm)
            throw new ArgumentException("Finish must be after the start.");
        if (!waypoints.Any(w => w.TimestampTicks == interval.StartTimestampTicks)
            || !waypoints.Any(w => w.TimestampTicks == interval.FinishTimestampTicks))
            throw new ArgumentException("Unable to delete waypoint");
        var others = FromMarkers(interval.CourseId, waypoints)
            .Where(item => item.StartTimestampTicks != interval.StartTimestampTicks)
            .ToList();
        var route = RouteLine(waypoints.Where(w =>
            w.TimestampTicks != interval.StartTimestampTicks && w.TimestampTicks != interval.FinishTimestampTicks));
        var start = AtDistance(route, newStartKm);
        var finish = AtDistance(route, newFinishKm);
        // Seed with every previous tick, including the markers just removed, so the
        // replacement pair gets new identities the way MoveIntervalAlongRoute did.
        var used = waypoints.Select(w => w.TimestampTicks).ToHashSet();
        interval.StartElapsedKm = start.ElapsedKm;
        interval.FinishElapsedKm = finish.ElapsedKm;
        interval.StartTimestampTicks = FreshTick(used);
        interval.FinishTimestampTicks = FreshTick(used);
        interval.StartLatitude = start.Latitude;
        interval.StartLongitude = start.Longitude;
        interval.FinishLatitude = finish.Latitude;
        interval.FinishLongitude = finish.Longitude;
        interval.StartAltitudeKm = start.AltitudeKm;
        interval.FinishAltitudeKm = finish.AltitudeKm;
        interval.StartHeadingDegrees = start.HeadingDegrees;
        interval.FinishHeadingDegrees = finish.HeadingDegrees;
        others.Add(interval);
        others = others.OrderBy(item => item.StartElapsedKm).ToList();
        for (var i = 0; i < others.Count; i++)
            others[i].Ordinal = i + 1;
        return WithMarkers(route, others);
    }

    public static void Reproject(IReadOnlyList<Waypoint> route, CourseIntervalDefinition interval)
    {
        var start = AtDistance(RouteLine(route), interval.StartElapsedKm);
        var finish = AtDistance(RouteLine(route), interval.FinishElapsedKm);
        interval.StartElapsedKm = start.ElapsedKm;
        interval.FinishElapsedKm = finish.ElapsedKm;
        interval.StartLatitude = start.Latitude;
        interval.StartLongitude = start.Longitude;
        interval.FinishLatitude = finish.Latitude;
        interval.FinishLongitude = finish.Longitude;
        interval.StartAltitudeKm = start.AltitudeKm;
        interval.FinishAltitudeKm = finish.AltitudeKm;
        interval.StartHeadingDegrees = start.HeadingDegrees;
        interval.FinishHeadingDegrees = finish.HeadingDegrees;
    }

    public static List<Waypoint> WithMarkers(IReadOnlyList<Waypoint> route, IReadOnlyList<CourseIntervalDefinition> intervals)
    {
        var line = RouteLine(route);
        foreach (var interval in intervals.OrderBy(item => item.StartElapsedKm))
        {
            line.Add(Marker(interval, start: true));
            line.Add(Marker(interval, start: false));
        }
        return line.OrderBy(w => w.ElapsedDistanceKm).ThenBy(w => w.TimestampTicks).ToList();
    }

    public static IReadOnlyList<string> Describe(IEnumerable<Waypoint> waypoints) =>
        FromMarkers(Guid.Empty, waypoints).Select(interval =>
        {
            var name = string.IsNullOrWhiteSpace(interval.Name) ? "Interval " + interval.Ordinal : interval.Name;
            return $"{name}: {interval.StartElapsedKm:0.00}–{interval.FinishElapsedKm:0.00} km ({interval.LengthKm:0.00} km)";
        }).ToList();

    public static Waypoint Marker(CourseIntervalDefinition interval, bool start) => new()
    {
        CourseId = interval.CourseId,
        TimestampTicks = start ? interval.StartTimestampTicks : interval.FinishTimestampTicks,
        Latitude = start ? interval.StartLatitude : interval.FinishLatitude,
        Longitude = start ? interval.StartLongitude : interval.FinishLongitude,
        AltitudeKm = start ? interval.StartAltitudeKm : interval.FinishAltitudeKm,
        HeadingDegrees = start ? interval.StartHeadingDegrees : interval.FinishHeadingDegrees,
        ElapsedDistanceKm = start ? interval.StartElapsedKm : interval.FinishElapsedKm,
        WaypointType = start ? WaypointType.StartInterval : WaypointType.FinishInterval,
        TextDescription = start ? interval.Name : ""
    };

    private static RouteSample Sample(Waypoint point, Waypoint toward, double elapsedKm)
    {
        var heading = point.HeadingDegrees;
        if (toward.TimestampTicks != point.TimestampTicks)
            heading = GeoMath.InitialBearingDegrees(point.Latitude, point.Longitude, toward.Latitude, toward.Longitude);
        return new RouteSample(point.Latitude, point.Longitude, elapsedKm, point.AltitudeKm, heading);
    }

    private static (RouteSample Sample, double DistanceMeters) Project(Waypoint a, Waypoint b, double latitude, double longitude)
    {
        var originLat = Degrees(a.Latitude);
        var bx = MetersX(a.Longitude, b.Longitude, originLat);
        var by = MetersY(a.Latitude, b.Latitude);
        var px = MetersX(a.Longitude, longitude, originLat);
        var py = MetersY(a.Latitude, latitude);
        var ab2 = bx * bx + by * by;
        var t = ab2 <= 0 ? 0 : Math.Clamp((px * bx + py * by) / ab2, 0, 1);
        var qx = bx * t;
        var qy = by * t;
        var elapsed = a.ElapsedDistanceKm + (b.ElapsedDistanceKm - a.ElapsedDistanceKm) * t;
        var sample = new RouteSample(
            a.Latitude + (b.Latitude - a.Latitude) * t,
            a.Longitude + (b.Longitude - a.Longitude) * t,
            elapsed,
            a.AltitudeKm + (b.AltitudeKm - a.AltitudeKm) * t,
            GeoMath.InitialBearingDegrees(a.Latitude, a.Longitude, b.Latitude, b.Longitude));
        var distance = Math.Sqrt((px - qx) * (px - qx) + (py - qy) * (py - qy));
        return (sample, distance);
    }

    private static long FreshTick(ISet<long> used)
    {
        var tick = used.Count == 0 ? 1 : used.Max() + 1;
        while (!used.Add(tick))
            tick++;
        return tick;
    }

    private static double Degrees(double degrees) => degrees * Math.PI / 180.0;
    private static double MetersX(double lon1, double lon2, double latRadians) =>
        GeoMath.EarthRadiusKm * 1000.0 * Math.Cos(latRadians) * Degrees(lon2 - lon1);
    private static double MetersY(double lat1, double lat2) =>
        GeoMath.EarthRadiusKm * 1000.0 * Degrees(lat2 - lat1);
}
