namespace GpsTrainer;

/// <summary>
/// A course line is start, then the points in order, then the last point.
/// A routing reply that finishes on the start is not stored unless the click
/// itself is the start (an explicit close).
/// </summary>
public static class RoutePath
{
    public const double SamePointMeters = 15;

    public static List<RoutePointDto> WithoutImplicitReturn(
        double startLatitude,
        double startLongitude,
        double destinationLatitude,
        double destinationLongitude,
        IReadOnlyList<RoutePointDto> points)
    {
        var kept = new List<RoutePointDto>(points);
        if (kept.Count == 0)
            return kept;
        if (GeoMath.DistanceMeters(destinationLatitude, destinationLongitude, startLatitude, startLongitude) < SamePointMeters)
            return kept;
        while (kept.Count > 0
            && GeoMath.DistanceMeters(kept[^1].Latitude, kept[^1].Longitude, startLatitude, startLongitude) < SamePointMeters)
            kept.RemoveAt(kept.Count - 1);
        return kept;
    }
}
