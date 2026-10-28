namespace GpsTrainer;

/// <summary>
/// Learning mode keeps the ride in memory until Finish. This writes that track
/// onto a course that does not already have a start and a finish, and queues
/// the course so the next sync uploads the points.
/// </summary>
public static class LearnedCourse
{
    public const double TurnMatchMeters = 40;

    public static bool Commit(
        LocalDatabase db,
        Course course,
        IReadOnlyList<TrackPoint> track,
        double? latitude,
        double? longitude)
    {
        var existing = db.ListWaypoints(course.CourseId);
        if (HasFinishedRoute(existing))
            return false;

        var samples = Samples(track, latitude, longitude);
        if (samples.Count == 0)
            return false;

        var waypoints = Build(course.CourseId, samples, existing);
        course.ElapsedDistanceKm = CourseRules.Elapsed(waypoints).ToKilometers().Value;
        course.WaypointTimestamp = DateTime.UtcNow;
        course.Timestamp = DateTime.UtcNow;
        db.UpsertCourse(course);
        db.ReplaceWaypoints(course.CourseId, waypoints);
        db.RecordAction(ActionType.ModifyCourse, course.CourseId);
        return true;
    }

    public static bool HasFinishedRoute(IReadOnlyList<Waypoint> waypoints) =>
        waypoints.Any(point => point.WaypointType == WaypointType.Start)
        && waypoints.Any(point => point.WaypointType == WaypointType.Finish);

    public static List<Waypoint> Build(Guid courseId, IReadOnlyList<TrackPoint> samples, IReadOnlyList<Waypoint> turns)
    {
        var ordered = samples
            .Where(point => double.IsFinite(point.Latitude) && double.IsFinite(point.Longitude))
            .OrderBy(point => point.TimestampTicks)
            .ToList();
        var waypoints = new List<Waypoint>(ordered.Count);
        var elapsedKm = 0.0;
        var lastTicks = long.MinValue;
        for (var i = 0; i < ordered.Count; i++)
        {
            var sample = ordered[i];
            if (i > 0)
            {
                elapsedKm += GeoMath.DistanceMeters(
                    ordered[i - 1].Latitude,
                    ordered[i - 1].Longitude,
                    sample.Latitude,
                    sample.Longitude) / 1000.0;
            }

            var ticks = sample.TimestampTicks;
            if (ticks <= lastTicks)
                ticks = lastTicks + 1;
            lastTicks = ticks;

            var heading = 0.0;
            if (i + 1 < ordered.Count)
            {
                heading = GeoMath.InitialBearingDegrees(
                    sample.Latitude,
                    sample.Longitude,
                    ordered[i + 1].Latitude,
                    ordered[i + 1].Longitude);
            }
            else if (waypoints.Count > 0)
            {
                heading = waypoints[^1].HeadingDegrees;
            }

            waypoints.Add(new Waypoint
            {
                CourseId = courseId,
                Latitude = sample.Latitude,
                Longitude = sample.Longitude,
                TimestampTicks = ticks,
                ElapsedDistanceKm = i == 0 ? 0 : elapsedKm,
                HeadingDegrees = heading,
                WaypointType = i == 0
                    ? WaypointType.Start
                    : i == ordered.Count - 1 ? WaypointType.Finish : WaypointType.Continue
            });
        }

        ApplyTurns(waypoints, turns);
        return waypoints;
    }

    private static List<TrackPoint> Samples(IReadOnlyList<TrackPoint> track, double? latitude, double? longitude)
    {
        var samples = track
            .Where(point => double.IsFinite(point.Latitude) && double.IsFinite(point.Longitude))
            .ToList();
        if (samples.Count == 0 && latitude is double lat && longitude is double lon
            && double.IsFinite(lat) && double.IsFinite(lon))
        {
            samples.Add(new TrackPoint
            {
                Latitude = lat,
                Longitude = lon,
                TimestampTicks = SoapDateTime.ToUnixTicks(DateTime.UtcNow)
            });
        }
        return samples;
    }

    private static void ApplyTurns(List<Waypoint> waypoints, IReadOnlyList<Waypoint> turns)
    {
        var used = new HashSet<int>();
        foreach (var turn in turns)
        {
            if (!IsTurn(turn.WaypointType))
                continue;
            var match = -1;
            var nearest = TurnMatchMeters;
            for (var i = 1; i < waypoints.Count - 1; i++)
            {
                if (used.Contains(i) || waypoints[i].WaypointType != WaypointType.Continue)
                    continue;
                var meters = GeoMath.DistanceMeters(
                    turn.Latitude, turn.Longitude, waypoints[i].Latitude, waypoints[i].Longitude);
                if (meters < nearest)
                {
                    nearest = meters;
                    match = i;
                }
            }
            if (match < 0)
                continue;
            used.Add(match);
            waypoints[match].WaypointType = turn.WaypointType;
            waypoints[match].TextDescription = turn.TextDescription;
        }
    }

    private static bool IsTurn(WaypointType type) => type is
        WaypointType.TurnLeft or WaypointType.TurnRight or WaypointType.TurnAround
        or WaypointType.BearLeft or WaypointType.BearRight
        or WaypointType.Summit or WaypointType.FeedZone or WaypointType.Announcement;
}
