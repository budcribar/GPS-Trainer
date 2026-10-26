namespace GpsTrainer;

/// <summary>
/// FINISH on the start pin, or on the map beside the start, closes the loop.
/// The closing point is a Finish at the start. A click just outside the start
/// pin at street zoom counts as "near".
/// </summary>
public static class LoopClose
{
    public const double NearStartMeters = 200;

    /// <summary>
    /// How far FINISH may look for a road when the start pin is off the network.
    /// The closing point is still the exact start, not the snapped road point.
    /// </summary>
    public const double CloseSnapMeters = 10_000;

    public static Waypoint? Start(IEnumerable<Waypoint> waypoints)
    {
        var start = waypoints.FirstOrDefault(point => point.WaypointType == WaypointType.Start);
        if (start is not null)
            return start;
        return waypoints.FirstOrDefault(point => !CourseEdits.IsInterval(point));
    }

    public static bool HasOpenLoop(IEnumerable<Waypoint> waypoints)
    {
        var start = Start(waypoints);
        var last = waypoints.LastOrDefault(point => !CourseEdits.IsInterval(point));
        if (start is null || last is null || ReferenceEquals(start, last))
            return false;
        var routePoints = 0;
        foreach (var point in waypoints)
        {
            if (!CourseEdits.IsInterval(point))
                routePoints++;
        }
        if (routePoints < 2)
            return false;
        return GeoMath.DistanceMeters(last.Latitude, last.Longitude, start.Latitude, start.Longitude) >= RoutePath.SamePointMeters;
    }

    public static bool IsNearStart(IEnumerable<Waypoint> waypoints, double latitude, double longitude)
    {
        var start = Start(waypoints);
        if (start is null)
            return false;
        return GeoMath.DistanceMeters(latitude, longitude, start.Latitude, start.Longitude) <= NearStartMeters;
    }

    /// <summary>
    /// After the closing Finish is stored, copy the start altitude onto it when the
    /// router left it unknown, then stamp distance so the summary and profile update.
    /// </summary>
    public static void Publish(Course course, List<Waypoint> waypoints)
    {
        var start = Start(waypoints);
        var last = waypoints.LastOrDefault(point => !CourseEdits.IsInterval(point));
        if (start is not null && last is not null
            && last.WaypointType == WaypointType.Finish
            && GeoMath.DistanceMeters(last.Latitude, last.Longitude, start.Latitude, start.Longitude) < RoutePath.SamePointMeters
            && !Known(last.AltitudeKm)
            && Known(start.AltitudeKm))
        {
            last.AltitudeKm = start.AltitudeKm;
        }
        CourseEdits.StampRoute(waypoints);
        course.ElapsedDistanceKm = CourseEdits.LengthKm(waypoints);
    }

    /// <summary>
    /// The default snap missed a road. A wider search can still follow roads
    /// toward the start; other failures should not be retried.
    /// </summary>
    public static bool NeedsWiderSnap(RouteResponseDto? route) =>
        route is not { Routed: true }
        && (route?.Message ?? "").Contains("routable", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Keep the road geometry, then make the last stored point the exact start.
    /// A road end already on the start is moved onto it. A road end farther away
    /// stays, and the exact start is appended so the loop still closes there.
    /// </summary>
    public static List<RoutePointDto> PinEndToStart(IReadOnlyList<RoutePointDto> points, double latitude, double longitude)
    {
        var pinned = new List<RoutePointDto>(points.Count + 1);
        foreach (var point in points)
            pinned.Add(Copy(point));
        if (pinned.Count > 0
            && GeoMath.DistanceMeters(pinned[^1].Latitude, pinned[^1].Longitude, latitude, longitude) < RoutePath.SamePointMeters)
        {
            pinned[^1].Latitude = latitude;
            pinned[^1].Longitude = longitude;
            return pinned;
        }

        pinned.Add(new RoutePointDto
        {
            Latitude = latitude,
            Longitude = longitude,
            WaypointType = WaypointType.Finish.ToString(),
            AltitudeKm = Altitude.Unknown.Kilometers
        });
        return pinned;
    }

    /// <summary>2013 draws StartFinish.png when the finish sits on the start pin.</summary>
    public static bool ShowsStartFinish(IEnumerable<Waypoint> waypoints)
    {
        var start = Start(waypoints);
        if (start is null || start.WaypointType != WaypointType.Start)
            return false;
        foreach (var point in waypoints)
        {
            if (point.WaypointType == WaypointType.Finish
                && GeoMath.DistanceMeters(point.Latitude, point.Longitude, start.Latitude, start.Longitude) < RoutePath.SamePointMeters)
                return true;
        }
        return false;
    }

    /// <summary>Glyph drawn on a pin. The start pin reads S/F once the loop is closed on it.</summary>
    public static string MarkerGlyph(WaypointType type, bool startAndFinish)
    {
        if (type == WaypointType.Start && startAndFinish)
            return "S/F";
        return type switch
        {
            WaypointType.Start => "S",
            WaypointType.Finish => "F",
            WaypointType.TurnLeft or WaypointType.BearLeft => "L",
            WaypointType.TurnRight or WaypointType.BearRight => "R",
            WaypointType.TurnAround => "U",
            WaypointType.Summit => "▲",
            WaypointType.FeedZone => "Z",
            WaypointType.Announcement => "A",
            WaypointType.StartInterval or WaypointType.FinishInterval => "I",
            _ => "•"
        };
    }

    private static RoutePointDto Copy(RoutePointDto point) => new()
    {
        Latitude = point.Latitude,
        Longitude = point.Longitude,
        WaypointType = point.WaypointType,
        Text = point.Text,
        AltitudeKm = point.AltitudeKm
    };

    private static bool Known(double altitudeKm) =>
        double.IsFinite(altitudeKm) && altitudeKm != Altitude.Unknown.Kilometers;
}
