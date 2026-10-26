namespace GpsTrainer.Web.Maps;

/// <summary>
/// The course editor map. The page talks only to this. Nothing here names Leaflet or Google.
/// <see cref="CourseMaps"/> picks the implementation from <c>Map:Provider</c> (default <c>Leaflet</c>).
///
/// To add a provider, implement this interface, load that provider's own script, and return it from
/// <see cref="CourseMaps.Create"/> when <c>Map:Provider</c> matches. Leave <c>CourseEditor.razor</c> alone.
/// Raise <see cref="MapClicked"/>, <see cref="MapRightClicked"/>, <see cref="PinClicked"/>, and <see cref="PinDragged"/>
/// for those gestures. Raise the menu events when the user picks a row the page has to apply.
/// The Leaflet script still draws its own menus and reports the chosen row; it does not send a raw right-click.
/// </summary>
public interface ICourseMap
{
    Task MountAsync(string selector, CourseMapFrame frame);
    Task UpdateAsync(CourseMapFrame frame);
    Task SetViewAsync(GeoCoord center);
    Task FitAsync();
    Task<double> ProfileViewXAsync(string elementId, double clientX, double clientY);
    Task OpenProfileMenuAsync(double x, double y, double clientX, double clientY);
    Task<string?> ReadElevationUnitsAsync(string accountKey, string sharedKey);
    Task RememberElevationUnitsAsync(string key, string value);
    Task<bool> ReadShowProfileAsync();
    Task RememberShowProfileAsync(bool open);
    Task DownloadAsync(string fileName, string contents);
    Task UnmountAsync();

    event Func<GeoCoord, Task>? MapClicked;
    event Func<GeoCoord, Task>? MapRightClicked;
    event Func<int, Task>? PinClicked;
    event Func<int, GeoCoord, Task>? PinDragged;

    event Func<string, GeoCoord, bool, Task>? InsertRequested;
    event Func<int, string, Task>? TypeChangeRequested;
    event Func<int, int, string, Task>? ConnectRequested;
    event Func<int, string, Task>? IntervalRequested;
    event Func<int, GeoCoord, Task>? StandardFinishRequested;
    event Func<int, Task>? DeleteRequested;
    event Func<int, string, Task>? TextChangeRequested;
    event Func<int, Task>? SummitRequested;
    event Func<GeoCoord, Task>? SummitAtRequested;
    event Func<string, double, double, Task>? ProfilePlaceRequested;
    event Func<string, double, double, Task>? ProfileIntervalRequested;
    event Func<Task>? LoopCloseRequested;
    event Func<Task>? UndoRequested;
}
