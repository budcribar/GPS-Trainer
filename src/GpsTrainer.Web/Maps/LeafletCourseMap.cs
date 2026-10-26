using Microsoft.JSInterop;

namespace GpsTrainer.Web.Maps;

/// <summary>
/// Leaflet map for the course editor. It calls the existing <c>gpsTrainerCourseEditor</c> script
/// and forwards the script's callbacks. The script, menus, and gestures stay as they are.
/// </summary>
public sealed class LeafletCourseMap : ICourseMap, IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private DotNetObjectReference<LeafletCourseMap>? _self;

    public LeafletCourseMap(IJSRuntime js) => _js = js;

    public event Func<GeoCoord, Task>? MapClicked;
    public event Func<GeoCoord, Task>? MapRightClicked;
    public event Func<int, Task>? PinClicked;
    public event Func<int, GeoCoord, Task>? PinDragged;
    public event Func<string, GeoCoord, bool, Task>? InsertRequested;
    public event Func<int, string, Task>? TypeChangeRequested;
    public event Func<int, int, string, Task>? ConnectRequested;
    public event Func<int, string, Task>? IntervalRequested;
    public event Func<int, GeoCoord, Task>? StandardFinishRequested;
    public event Func<int, Task>? DeleteRequested;
    public event Func<int, string, Task>? TextChangeRequested;
    public event Func<int, Task>? SummitRequested;
    public event Func<GeoCoord, Task>? SummitAtRequested;
    public event Func<string, double, double, Task>? ProfilePlaceRequested;
    public event Func<string, double, double, Task>? ProfileIntervalRequested;
    public event Func<Task>? LoopCloseRequested;
    public event Func<Task>? UndoRequested;

    public async Task MountAsync(string selector, CourseMapFrame frame)
    {
        _self ??= DotNetObjectReference.Create(this);
        await _js.InvokeVoidAsync(
            "gpsTrainerCourseEditor.mount",
            selector,
            Pins(frame),
            _self,
            new { latitude = frame.Center.Latitude, longitude = frame.Center.Longitude },
            frame.Menus.PinTypes.Select(choice => new { type = choice.Type, label = choice.Label }).ToArray(),
            Options(frame.Menus));
    }

    public Task UpdateAsync(CourseMapFrame frame) =>
        _js.InvokeVoidAsync("gpsTrainerCourseEditor.setWaypoints", Pins(frame), Options(frame.Menus)).AsTask();

    public Task SetViewAsync(GeoCoord center) =>
        _js.InvokeVoidAsync("gpsTrainerCourseEditor.setView", center.Latitude, center.Longitude).AsTask();

    public Task FitAsync() =>
        _js.InvokeVoidAsync("gpsTrainerCourseEditor.fit").AsTask();

    public async Task<double> ProfileViewXAsync(string elementId, double clientX, double clientY) =>
        await _js.InvokeAsync<double>("gpsTrainerCourseEditor.profileViewX", elementId, clientX, clientY);

    public Task OpenProfileMenuAsync(double x, double y, double clientX, double clientY) =>
        _js.InvokeVoidAsync("gpsTrainerCourseEditor.openProfileMenu", x, y, clientX, clientY).AsTask();

    public async Task<string?> ReadElevationUnitsAsync(string accountKey, string sharedKey) =>
        await _js.InvokeAsync<string?>("gpsTrainerCourseEditor.elevationUnits", accountKey, sharedKey);

    public Task RememberElevationUnitsAsync(string key, string value) =>
        _js.InvokeVoidAsync("gpsTrainerCourseEditor.rememberElevationUnits", key, value).AsTask();

    public async Task<bool> ReadShowProfileAsync() =>
        await _js.InvokeAsync<bool>("gpsTrainerCourseEditor.showProfile");

    public Task RememberShowProfileAsync(bool open) =>
        _js.InvokeVoidAsync("gpsTrainerCourseEditor.rememberShowProfile", open).AsTask();

    public Task DownloadAsync(string fileName, string contents) =>
        _js.InvokeVoidAsync("gpsTrainerCourseEditor.download", fileName, contents).AsTask();

    public Task UnmountAsync() =>
        _js.InvokeVoidAsync("gpsTrainerCourseEditor.unmount").AsTask();

    [JSInvokable]
    public Task AddWaypoint(double latitude, double longitude) =>
        MapCallback.Raise(MapClicked, new GeoCoord(latitude, longitude));

    [JSInvokable]
    public Task MapRightClick(double latitude, double longitude) =>
        MapCallback.Raise(MapRightClicked, new GeoCoord(latitude, longitude));

    [JSInvokable]
    public Task PinClick(int index) =>
        MapCallback.Raise(PinClicked, index);

    [JSInvokable]
    public Task InsertWaypoint(string typeName, double latitude, double longitude, bool route) =>
        MapCallback.Raise(InsertRequested, typeName, new GeoCoord(latitude, longitude), route);

    [JSInvokable]
    public Task SetWaypointText(int index, string text) =>
        MapCallback.Raise(TextChangeRequested, index, text);

    [JSInvokable]
    public Task PlaceSummit(int index) =>
        MapCallback.Raise(SummitRequested, index);

    [JSInvokable]
    public Task PlaceSummitAt(double latitude, double longitude) =>
        MapCallback.Raise(SummitAtRequested, new GeoCoord(latitude, longitude));

    [JSInvokable]
    public Task ProfilePlace(string kind, double clientX, double clientY) =>
        MapCallback.Raise(ProfilePlaceRequested, kind, clientX, clientY);

    [JSInvokable]
    public Task ProfileInterval(string distance, double clientX, double clientY) =>
        MapCallback.Raise(ProfileIntervalRequested, distance, clientX, clientY);

    [JSInvokable]
    public Task SetWaypointType(int index, string typeName) =>
        MapCallback.Raise(TypeChangeRequested, index, typeName);

    [JSInvokable]
    public Task DeleteWaypoint(int index) =>
        MapCallback.Raise(DeleteRequested, index);

    [JSInvokable]
    public Task ConnectWaypoint(int selectedIndex, int targetIndex, string typeName) =>
        MapCallback.Raise(ConnectRequested, selectedIndex, targetIndex, typeName);

    [JSInvokable]
    public Task MoveWaypoint(int index, double latitude, double longitude) =>
        MapCallback.Raise(PinDragged, index, new GeoCoord(latitude, longitude));

    [JSInvokable]
    public Task PlaceInterval(int index, string distance) =>
        MapCallback.Raise(IntervalRequested, index, distance);

    [JSInvokable]
    public Task StandardFinish(int selectedIndex, double latitude, double longitude) =>
        MapCallback.Raise(StandardFinishRequested, selectedIndex, new GeoCoord(latitude, longitude));

    [JSInvokable]
    public Task CloseLoop() =>
        MapCallback.Raise(LoopCloseRequested);

    [JSInvokable]
    public Task UndoLastSegment() =>
        MapCallback.Raise(UndoRequested);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await UnmountAsync();
        }
        catch (JSException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        _self?.Dispose();
        _self = null;
    }

    private static object[] Pins(CourseMapFrame frame) =>
        frame.Pins.Select(pin => new
        {
            index = pin.Index,
            latitude = pin.Latitude,
            longitude = pin.Longitude,
            type = pin.Type.ToString(),
            text = pin.Text
        }).ToArray();

    private static object Options(CourseMapMenus menus) => new
    {
        standardFinish = menus.StandardFinish,
        nearStartMeters = menus.NearStartMeters,
        samePointMeters = menus.SamePointMeters,
        intervals = menus.IntervalDistances.ToArray(),
        connect = menus.Connect.Select(choice => new { type = choice.Type, label = choice.Label }).ToArray(),
        mapMenuLast = menus.MapMenuLast.Select(row => new { label = row.Label, action = row.Action, type = row.Type }).ToArray(),
        mapMenuEarlier = menus.MapMenuEarlier.Select(row => new { label = row.Label, action = row.Action, type = row.Type }).ToArray()
    };
}
