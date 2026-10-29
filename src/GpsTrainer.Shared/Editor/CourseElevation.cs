namespace GpsTrainer;

/// <summary>
/// Fills unknown waypoint altitudes from an elevation lookup. Known altitudes,
/// including sea level, are left alone. Interval markers are not samples.
/// </summary>
public static class CourseElevation
{
    public const int BatchSize = 200;

    public static bool NeedsLookup(IReadOnlyList<Waypoint> waypoints)
    {
        foreach (var point in waypoints)
        {
            if (!CourseEdits.IsInterval(point) && !SummitSearch.IsKnown(point.AltitudeKm))
                return true;
        }
        return false;
    }

    public static int MissingCount(IReadOnlyList<Waypoint> waypoints)
    {
        var count = 0;
        foreach (var point in waypoints)
        {
            if (!CourseEdits.IsInterval(point) && !SummitSearch.IsKnown(point.AltitudeKm))
                count++;
        }
        return count;
    }

    /// <summary>
    /// Writes looked-up altitudes onto the unknown route points, in order.
    /// Returns how many points received a real altitude.
    /// </summary>
    public static int Apply(IList<Waypoint> waypoints, IReadOnlyList<double?> altitudes)
    {
        var index = 0;
        var written = 0;
        foreach (var point in waypoints)
        {
            if (CourseEdits.IsInterval(point) || SummitSearch.IsKnown(point.AltitudeKm))
                continue;
            if (index >= altitudes.Count)
                break;
            if (altitudes[index] is double known && SummitSearch.IsKnown(known))
            {
                point.AltitudeKm = known;
                written++;
            }
            index++;
        }
        return written;
    }
}
