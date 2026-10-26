namespace GpsTrainer;

/// <summary>
/// Great-circle helpers. <c>Position.TranslateTo</c> used a sphere of radius 6371 km.
/// Android <c>Location.distanceTo</c> is WGS84; this haversine is the portable stand-in.
/// </summary>
public static class GeoMath
{
    public const double EarthRadiusKm = 6371.0;

    public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        var r = EarthRadiusKm * 1000.0;
        var p1 = DegreesToRadians(lat1);
        var p2 = DegreesToRadians(lat2);
        var dp = DegreesToRadians(lat2 - lat1);
        var dl = DegreesToRadians(lon2 - lon1);
        var a = Math.Sin(dp / 2) * Math.Sin(dp / 2)
            + Math.Cos(p1) * Math.Cos(p2) * Math.Sin(dl / 2) * Math.Sin(dl / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return r * c;
    }

    public static (double Latitude, double Longitude) Translate(double lat, double lon, double bearingDegrees, double distanceKm)
    {
        var brng = DegreesToRadians(bearingDegrees);
        var lat1 = DegreesToRadians(lat);
        var lon1 = DegreesToRadians(lon);
        var lat2 = Math.Asin(Math.Sin(lat1) * Math.Cos(distanceKm / EarthRadiusKm)
            + Math.Cos(lat1) * Math.Sin(distanceKm / EarthRadiusKm) * Math.Cos(brng));
        var lon2 = lon1 + Math.Atan2(
            Math.Sin(brng) * Math.Sin(distanceKm / EarthRadiusKm) * Math.Cos(lat1),
            Math.Cos(distanceKm / EarthRadiusKm) - Math.Sin(lat1) * Math.Sin(lat2));
        return (RadiansToDegrees(lat2), RadiansToDegrees(lon2));
    }

    public static double InitialBearingDegrees(double lat1, double lon1, double lat2, double lon2)
    {
        var p1 = DegreesToRadians(lat1);
        var p2 = DegreesToRadians(lat2);
        var dl = DegreesToRadians(lon2 - lon1);
        var y = Math.Sin(dl) * Math.Cos(p2);
        var x = Math.Cos(p1) * Math.Sin(p2) - Math.Sin(p1) * Math.Cos(p2) * Math.Cos(dl);
        var bearing = RadiansToDegrees(Math.Atan2(y, x));
        return (bearing + 360.0) % 360.0;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
    private static double RadiansToDegrees(double radians) => radians * 180.0 / Math.PI;
}

public sealed class GeoPosition
{
    public static readonly GeoPosition Empty = new(0, 0, true);

    public double Latitude { get; }
    public double Longitude { get; }
    public bool IsEmpty { get; }

    public GeoPosition(double latitude, double longitude, bool? isEmpty = null)
    {
        Latitude = latitude;
        Longitude = longitude;
        IsEmpty = isEmpty ?? (latitude == 0 && longitude == 0);
    }

    public Distance DistanceTo(GeoPosition other) =>
        Distance.FromMeters(GeoMath.DistanceMeters(Latitude, Longitude, other.Latitude, other.Longitude));
}
