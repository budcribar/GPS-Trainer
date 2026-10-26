namespace GpsTrainer;

public static class LoopGeometry
{
    public static double LengthMeters(IReadOnlyList<GeoCoord> points)
    {
        var total = 0.0;
        for (var i = 1; i < points.Count; i++)
            total += GeoMath.DistanceMeters(points[i - 1].Latitude, points[i - 1].Longitude, points[i].Latitude, points[i].Longitude);
        return total;
    }

    public static double[] ElapsedMeters(IReadOnlyList<GeoCoord> points)
    {
        var elapsed = new double[points.Count];
        for (var i = 1; i < points.Count; i++)
            elapsed[i] = elapsed[i - 1] + GeoMath.DistanceMeters(points[i - 1].Latitude, points[i - 1].Longitude, points[i].Latitude, points[i].Longitude);
        return elapsed;
    }

    public static double ClimbingMeters(IReadOnlyList<GeoCoord> points)
    {
        if (points.Count == 0 || !points.Any(point => point.HasAltitude))
            return 0;
        var waypoints = new List<Waypoint>(points.Count);
        var elapsed = ElapsedMeters(points);
        for (var i = 0; i < points.Count; i++)
        {
            waypoints.Add(new Waypoint
            {
                Latitude = points[i].Latitude,
                Longitude = points[i].Longitude,
                AltitudeKm = points[i].AltitudeKm,
                ElapsedDistanceKm = elapsed[i] / 1000.0
            });
        }
        return ElevationProfile.GainLoss(waypoints).GainKm * 1000.0;
    }

    public static List<GeoCoord> Sample(IReadOnlyList<GeoCoord> points, int maxPoints)
    {
        if (points.Count == 0)
            return [];
        if (points.Count <= maxPoints || maxPoints < 2)
            return points.ToList();
        var elapsed = ElapsedMeters(points);
        var total = elapsed[^1];
        var sampled = new List<GeoCoord>(maxPoints);
        for (var i = 0; i < maxPoints; i++)
        {
            var at = total * i / (maxPoints - 1);
            sampled.Add(AtDistance(points, elapsed, at));
        }
        return sampled;
    }

    public static GeoCoord AtDistance(IReadOnlyList<GeoCoord> points, IReadOnlyList<double> elapsed, double meters)
    {
        if (points.Count == 0)
            return default;
        if (meters <= 0)
            return points[0];
        if (meters >= elapsed[^1])
            return points[^1];
        var index = 1;
        while (index < elapsed.Count && elapsed[index] < meters)
            index++;
        var previous = elapsed[index - 1];
        var span = elapsed[index] - previous;
        var t = span <= 0.01 ? 0 : (meters - previous) / span;
        var a = points[index - 1];
        var b = points[index];
        var altitude = a.HasAltitude && b.HasAltitude
            ? a.AltitudeKm + (b.AltitudeKm - a.AltitudeKm) * t
            : a.HasAltitude ? a.AltitudeKm : b.AltitudeKm;
        return new GeoCoord(
            a.Latitude + (b.Latitude - a.Latitude) * t,
            a.Longitude + (b.Longitude - a.Longitude) * t,
            altitude);
    }

    public static double? InterpolateAltitudeKm(IReadOnlyList<GeoCoord> points, IReadOnlyList<double> elapsedMeters, double elapsedKm)
    {
        if (points.Count == 0)
            return null;
        var point = AtDistance(points, elapsedMeters, elapsedKm * 1000.0);
        return point.HasAltitude ? point.AltitudeKm : null;
    }

    public static (double AlongMeters, double OffsetMeters) Closest(IReadOnlyList<GeoCoord> points, double latitude, double longitude)
    {
        if (points.Count == 0)
            return (0, double.PositiveInfinity);
        var bestAlong = 0.0;
        var bestOffset = GeoMath.DistanceMeters(points[0].Latitude, points[0].Longitude, latitude, longitude);
        var along = 0.0;
        for (var i = 1; i < points.Count; i++)
        {
            var hit = ClosestOnSegment(points[i - 1], points[i], latitude, longitude);
            if (hit.Offset < bestOffset)
            {
                bestOffset = hit.Offset;
                bestAlong = along + hit.Along;
            }
            along += GeoMath.DistanceMeters(points[i - 1].Latitude, points[i - 1].Longitude, points[i].Latitude, points[i].Longitude);
        }
        return (bestAlong, bestOffset);
    }

    public static List<GeoCoord> WithAltitude(IReadOnlyList<GeoCoord> points, Func<double, double, double?>? elevationMeters)
    {
        if (elevationMeters is null)
            return points.ToList();
        var copy = new List<GeoCoord>(points.Count);
        var any = false;
        foreach (var point in points)
        {
            var meters = elevationMeters(point.Latitude, point.Longitude);
            if (meters is double value && double.IsFinite(value))
            {
                any = true;
                copy.Add(new GeoCoord(point.Latitude, point.Longitude, value / 1000.0));
            }
            else
                copy.Add(point);
        }
        return any ? copy : points.ToList();
    }

    public static bool IsPriorityCorridor(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && (name.Contains("Poudre", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Spring Creek", StringComparison.OrdinalIgnoreCase));

    private static (double Along, double Offset) ClosestOnSegment(GeoCoord start, GeoCoord end, double latitude, double longitude)
    {
        var lat = (start.Latitude + end.Latitude) / 2;
        var (x, y) = Project(latitude, longitude, lat);
        var (x1, y1) = Project(start.Latitude, start.Longitude, lat);
        var (x2, y2) = Project(end.Latitude, end.Longitude, lat);
        var dx = x2 - x1;
        var dy = y2 - y1;
        var span = Math.Sqrt(dx * dx + dy * dy);
        if (span < 0.01)
            return (0, Math.Sqrt((x - x1) * (x - x1) + (y - y1) * (y - y1)));
        var t = Math.Clamp(((x - x1) * dx + (y - y1) * dy) / (span * span), 0, 1);
        var px = x1 + t * dx;
        var py = y1 + t * dy;
        return (t * span, Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py)));
    }

    private static (double X, double Y) Project(double latitude, double longitude, double referenceLatitude) =>
        (
            double.DegreesToRadians(longitude) * GeoMath.EarthRadiusKm * 1000.0 * Math.Cos(double.DegreesToRadians(referenceLatitude)),
            double.DegreesToRadians(latitude) * GeoMath.EarthRadiusKm * 1000.0
        );
}
