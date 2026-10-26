namespace GpsTrainer.Web.Maps;

/// <summary>A point on the course map. Latitude and longitude are degrees.</summary>
public readonly record struct GeoCoord(double Latitude, double Longitude);

/// <summary>One pin the page asks the map to draw. The type is a waypoint type name the menus already use.</summary>
public sealed record CoursePin(int Index, double Latitude, double Longitude, WaypointType Type, string? Text);

public sealed record MenuChoice(string Type, string Label);

public sealed record MenuRow(string Label, string Action, string? Type);

/// <summary>
/// Menu rows the page expects every map provider to offer.
/// The Leaflet script draws Delete, Connect, Interval, and Zoom itself.
/// Feed zone, Summit, and Announcement come from <see cref="PinTypes"/>.
/// A new provider should render <see cref="PinMenu"/> from this same list.
/// </summary>
public sealed class CourseMapMenus
{
    public static readonly string[] PinMenu =
    [
        "Delete", "Connect", "Feed zone", "Summit", "Announcement", "Interval", "Zoom"
    ];

    public string StandardFinish { get; init; } = "";
    public double NearStartMeters { get; init; }
    public double SamePointMeters { get; init; }
    public IReadOnlyList<string> IntervalDistances { get; init; } = [];
    public IReadOnlyList<MenuChoice> PinTypes { get; init; } = [];
    public IReadOnlyList<MenuChoice> Connect { get; init; } = [];
    public IReadOnlyList<MenuRow> MapMenuLast { get; init; } = [];
    public IReadOnlyList<MenuRow> MapMenuEarlier { get; init; } = [];
}

/// <summary>Pins, menus, and the home center for one draw of the editor map.</summary>
public sealed class CourseMapFrame
{
    public IReadOnlyList<CoursePin> Pins { get; init; } = [];
    public CourseMapMenus Menus { get; init; } = new();
    public GeoCoord Center { get; init; }
}
