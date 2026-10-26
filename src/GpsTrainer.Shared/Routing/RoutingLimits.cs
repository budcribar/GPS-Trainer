namespace GpsTrainer;

public static class RoutingLimits
{
    public const double MinSegmentMeters = 8;
    public const double MaxSegmentMeters = 150_000;
    public const int MaxStoredPoints = 800;
    public const int MaxAddressLength = 200;
    public const int MaxInstructionLength = 240;

    public static bool IsPosition(double latitude, double longitude) =>
        double.IsFinite(latitude) && double.IsFinite(longitude)
        && latitude is >= -90 and <= 90
        && longitude is >= -180 and <= 180;

    /// <summary>
    /// Returns a user-facing message when a segment must not be sent to the router.
    /// Too-close and off-map clicks should not be stored as a duplicate straight segment.
    /// </summary>
    public static string? RejectSegment(double fromLatitude, double fromLongitude, double toLatitude, double toLongitude)
    {
        if (!IsPosition(fromLatitude, fromLongitude) || !IsPosition(toLatitude, toLongitude))
            return "Those coordinates are not on the map.";
        var meters = GeoMath.DistanceMeters(fromLatitude, fromLongitude, toLatitude, toLongitude);
        if (meters < MinSegmentMeters)
            return "That point is too close to the previous one.";
        if (meters > MaxSegmentMeters)
            return "That segment is longer than 150 km.";
        return null;
    }

    public static bool AllowsStraightFallback(string? message) =>
        message is not null
        && !message.Contains("too close", StringComparison.OrdinalIgnoreCase)
        && !message.Contains("not on the map", StringComparison.OrdinalIgnoreCase);
}
