namespace GpsTrainer;

/// <summary>
/// Decides whether a map tap should call the authenticated routing endpoint.
/// The device never sees a provider key. Trail, mountain, and cross-country
/// courses stay freehand unless the rider turns road-following on.
/// </summary>
public static class CourseMapTap
{
    public static bool TryCreateRouteRequest(
        CourseSketch sketch,
        double latitude,
        double longitude,
        bool followRoads,
        string? activity,
        WaypointType requestedType,
        out RouteRequestDto? request,
        out string? blocked)
    {
        request = null;
        blocked = null;
        if (requestedType is WaypointType.StartInterval or WaypointType.FinishInterval)
            return false;

        var previous = sketch.LastRoutePoint();
        if (!followRoads || previous is null)
            return false;

        var rejected = RoutingLimits.RejectSegment(previous.Latitude, previous.Longitude, latitude, longitude);
        if (rejected is not null && !RoutingLimits.AllowsStraightFallback(rejected))
        {
            blocked = rejected;
            return false;
        }

        request = new RouteRequestDto
        {
            FromLatitude = previous.Latitude,
            FromLongitude = previous.Longitude,
            ToLatitude = latitude,
            ToLongitude = longitude,
            Activity = activity
        };
        return true;
    }

    /// <summary>Same elapsed-distance walk the web editor uses when it saves a course.</summary>
    public static double StampElapsed(IList<Waypoint> waypoints)
    {
        var distance = 0.0;
        GeoPosition? previous = null;
        foreach (var wp in waypoints)
        {
            var here = new GeoPosition(wp.Latitude, wp.Longitude, false);
            if (previous is not null)
                distance += previous.DistanceTo(here).ToKilometers().Value;
            wp.ElapsedDistanceKm = distance;
            previous = here;
        }

        return distance;
    }
}

/// <summary>Parses the custom URL the map WebView raises when the rider taps.</summary>
public static class MapTapUrl
{
    public static bool TryParse(string? url, out double latitude, out double longitude)
    {
        latitude = 0;
        longitude = 0;
        if (string.IsNullOrEmpty(url) || url.IndexOf("gpstrainer", StringComparison.OrdinalIgnoreCase) < 0)
            return false;
        var query = url.IndexOf('?', StringComparison.Ordinal);
        if (query < 0)
            return false;
        double? lat = null;
        double? lon = null;
        foreach (var part in url[(query + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2)
                continue;
            if (!double.TryParse(pair[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
                continue;
            if (pair[0] == "lat")
                lat = value;
            else if (pair[0] is "lng" or "lon")
                lon = value;
        }

        if (lat is null || lon is null)
            return false;
        latitude = lat.Value;
        longitude = lon.Value;
        return true;
    }
}
