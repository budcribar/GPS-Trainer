namespace GpsTrainer;

/// <summary><c>DataLayer.Validate</c> messages, unchanged.</summary>
public static class CourseRules
{
    public static string? Validate(IReadOnlyList<Waypoint> waypoints)
    {
        if (Elapsed(waypoints).ToMeters() <= 50)
            return "Course is too short";
        if (waypoints.Count < 2)
            return "A start and finish waypoint must be specified";
        if (waypoints[0].WaypointType != WaypointType.Start)
            return "First waypoint must be start";
        if (waypoints[0].ElapsedDistanceKm != 0.0)
            return "ElapsedDistanceKM should be 0 on start waypoint";
        if (waypoints[^1].WaypointType != WaypointType.Finish)
            return "Last waypoint must be finish";
        if (waypoints.Count(w => w.WaypointType == WaypointType.Start) != 1)
            return "There must be only one start waypoint";
        if (waypoints.Count(w => w.WaypointType == WaypointType.Finish) != 1)
            return "There must be only one finish waypoint";

        var open = false;
        Waypoint? previous = null;
        foreach (var wp in waypoints)
        {
            if (previous is not null && wp.ElapsedDistanceKm < previous.ElapsedDistanceKm)
                return $"Elapsed Distance is not sequential {previous.TimestampTicks}:{previous.ElapsedDistanceKm} {wp.TimestampTicks}:{wp.ElapsedDistanceKm}";
            previous = wp;
            if (wp.TimestampTicks < 0)
                return "Negative TimestampTicks found";
            if (wp.WaypointType == WaypointType.StartInterval)
            {
                if (open)
                    return "Overlapping Start Interval Found";
                open = true;
            }
            if (wp.WaypointType == WaypointType.FinishInterval)
            {
                if (!open)
                    return "Finish Interval Found Before Start Interval";
                open = false;
            }
        }
        if (open)
            return "Must have a matching Finish Interval for each Start Interval";
        return null;
    }

    public static Distance Elapsed(IReadOnlyList<Waypoint> waypoints)
    {
        if (waypoints.Count < 2)
            return Distance.FromMeters(0);
        var total = Distance.FromMeters(0);
        for (var i = 1; i < waypoints.Count; i++)
            total = total.Add(waypoints[i - 1].Position.DistanceTo(new GeoPosition(waypoints[i].Latitude, waypoints[i].Longitude, false)));
        return total;
    }

    /// <summary>
    /// <c>WebService.SelectEssentialWaypoints</c>: keep a Continue only when the
    /// heading has moved more than 6 degrees or the point is at least 250 m
    /// further along the course.
    /// </summary>
    public static List<Waypoint> Essential(IReadOnlyList<Waypoint> candidates)
    {
        const double headingDelta = 6;
        var minContinue = Distance.FromMeters(250);
        var results = new List<Waypoint>();
        double? prevHeading = null;
        var prevElapsed = Distance.Empty;
        foreach (var wp in candidates)
        {
            var heading = wp.HeadingDegrees;
            var elapsed = Distance.FromKilometers(wp.ElapsedDistanceKm);
            if (prevHeading is null)
            {
                results.Add(wp);
                prevHeading = heading;
                prevElapsed = elapsed;
                continue;
            }

            var deltaHeading = Math.Abs(HeadingDelta(prevHeading.Value, heading));
            var deltaDist = elapsed.Subtract(prevElapsed);
            if (wp.WaypointType != WaypointType.Continue || deltaDist.CompareTo(minContinue) >= 0 || deltaHeading > headingDelta)
            {
                results.Add(wp);
                prevHeading = heading;
                prevElapsed = elapsed;
            }
        }
        return results;
    }

    private static double HeadingDelta(double a, double b)
    {
        var d = a - b;
        while (d > 180) d -= 360;
        while (d < -180) d += 360;
        return d;
    }
}

public static class SiteIdentity
{
    public const string Domain = "gps-trainer.com";
    public const string PublicUrl = "https://gps-trainer.com";
    public const string SupportEmail = "support@gpsTrainer.com";
    public const string InfoEmail = "info@gpsTrainer.com";
}
