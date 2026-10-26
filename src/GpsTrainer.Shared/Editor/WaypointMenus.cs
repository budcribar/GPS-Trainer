namespace GpsTrainer;

/// <summary>
/// Types offered by the 2013 pushpin and map menus when a point was marked:
/// Continue, Turn right, Turn left, Turn around, Finish, Feed zone, Summit, Announcement.
/// Bear left and bear right are maneuver results, not menu labels.
/// </summary>
public static class WaypointMenus
{
    public static readonly WaypointType[] MarkerTypes =
    [
        WaypointType.Continue,
        WaypointType.TurnRight,
        WaypointType.TurnLeft,
        WaypointType.TurnAround,
        WaypointType.Finish,
        WaypointType.FeedZone,
        WaypointType.Summit,
        WaypointType.Announcement
    ];

    /// <summary>
    /// Rows on the marker itself. Continue, turns, and Finish stay on the map menu and under Connect.
    /// </summary>
    public static readonly WaypointType[] PinMenuTypes =
    [
        WaypointType.FeedZone,
        WaypointType.Summit,
        WaypointType.Announcement
    ];

    public static string Label(WaypointType type) => type switch
    {
        WaypointType.TurnRight => "Turn right",
        WaypointType.TurnLeft => "Turn left",
        WaypointType.TurnAround => "Turn around",
        WaypointType.FeedZone => "Feed zone",
        _ => type.ToString()
    };

    public static bool TryApply(IList<Waypoint> waypoints, int index, string? typeName)
    {
        if (index < 0 || index >= waypoints.Count || string.IsNullOrWhiteSpace(typeName))
            return false;
        if (!Enum.TryParse<WaypointType>(typeName, ignoreCase: true, out var type))
            return false;
        if (Array.IndexOf(MarkerTypes, type) < 0)
            return false;
        waypoints[index].WaypointType = type;
        return true;
    }
}
