namespace GpsTrainer;

/// <summary>
/// Logged-out map center. Signed-in users with a saved home start there instead.
/// </summary>
public static class MapDefaults
{
    public const double FortCollinsLatitude = 40.5853;
    public const double FortCollinsLongitude = -105.0844;

    public const string Attribution =
        "© OpenStreetMap contributors, directions and elevation from openrouteservice by HeiGIT";

    /// <summary>
    /// Center for a map that is not opening an existing course.
    /// Both saved coordinates have to be on the map. Anything else stays at Fort Collins.
    /// </summary>
    public static (double Latitude, double Longitude) OpeningCenter(double? latitude, double? longitude)
    {
        if (latitude is double lat && longitude is double lon && RoutingLimits.IsPosition(lat, lon))
            return (lat, lon);
        return (FortCollinsLatitude, FortCollinsLongitude);
    }
}
