using GpsTrainer.Web.Maps;

namespace GpsTrainer.Web.Tests;

/// <summary>
/// Map stand-in for the course editor tests. It records what the page asked for
/// and can play back clicks, right-clicks, pin clicks, and drags.
/// </summary>
public sealed class FakeCourseMap : ICourseMap
{
    public string? StoredUnits { get; set; } = "english";
    public bool ShowProfile { get; set; } = true;
    public double ProfileX { get; set; } = 200;
    public CourseMapFrame? LastFrame { get; private set; }
    public List<string> Calls { get; } = [];
    public List<(string Key, string Value)> UnitWrites { get; } = [];
    public List<(double X, double Y)> ProfileMenus { get; } = [];
    public List<GpsTrainer.Web.Maps.GeoCoord> Views { get; } = [];
    public List<(string Name, string Text)> Downloads { get; } = [];

    public event Func<GpsTrainer.Web.Maps.GeoCoord, Task>? MapClicked;
    public event Func<GpsTrainer.Web.Maps.GeoCoord, Task>? MapRightClicked;
    public event Func<int, Task>? PinClicked;
    public event Func<int, GpsTrainer.Web.Maps.GeoCoord, Task>? PinDragged;
    public event Func<string, GpsTrainer.Web.Maps.GeoCoord, bool, Task>? InsertRequested;
    public event Func<int, string, Task>? TypeChangeRequested;
    public event Func<int, int, string, Task>? ConnectRequested;
    public event Func<int, string, Task>? IntervalRequested;
    public event Func<int, GpsTrainer.Web.Maps.GeoCoord, Task>? StandardFinishRequested;
    public event Func<int, Task>? DeleteRequested;
    public event Func<int, string, Task>? TextChangeRequested;
    public event Func<int, Task>? SummitRequested;
    public event Func<GpsTrainer.Web.Maps.GeoCoord, Task>? SummitAtRequested;
    public event Func<string, double, double, Task>? ProfilePlaceRequested;
    public event Func<string, double, double, Task>? ProfileIntervalRequested;
    public event Func<Task>? LoopCloseRequested;
    public event Func<Task>? UndoRequested;

    public Task MountAsync(string selector, CourseMapFrame frame)
    {
        Calls.Add("mount " + selector);
        LastFrame = frame;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(CourseMapFrame frame)
    {
        Calls.Add("update");
        LastFrame = frame;
        return Task.CompletedTask;
    }

    public Task SetViewAsync(GpsTrainer.Web.Maps.GeoCoord center)
    {
        Views.Add(center);
        return Task.CompletedTask;
    }

    public Task FitAsync()
    {
        Calls.Add("fit");
        return Task.CompletedTask;
    }

    public Task<double> ProfileViewXAsync(string elementId, double clientX, double clientY) =>
        Task.FromResult(ProfileX);

    public Task OpenProfileMenuAsync(double x, double y, double clientX, double clientY)
    {
        ProfileMenus.Add((x, y));
        return Task.CompletedTask;
    }

    public Task<string?> ReadElevationUnitsAsync(string accountKey, string sharedKey) =>
        Task.FromResult(StoredUnits);

    public Task RememberElevationUnitsAsync(string key, string value)
    {
        UnitWrites.Add((key, value));
        StoredUnits = value;
        return Task.CompletedTask;
    }

    public Task<bool> ReadShowProfileAsync() => Task.FromResult(ShowProfile);

    public Task RememberShowProfileAsync(bool open)
    {
        ShowProfile = open;
        Calls.Add(open ? "profile open" : "profile closed");
        return Task.CompletedTask;
    }

    public Task DownloadAsync(string fileName, string contents)
    {
        Downloads.Add((fileName, contents));
        return Task.CompletedTask;
    }

    public Task UnmountAsync()
    {
        Calls.Add("unmount");
        return Task.CompletedTask;
    }

    public Task SimulateClick(double latitude, double longitude) =>
        MapCallback.Raise(MapClicked, new GpsTrainer.Web.Maps.GeoCoord(latitude, longitude));

    public Task SimulateRightClick(double latitude, double longitude) =>
        MapCallback.Raise(MapRightClicked, new GpsTrainer.Web.Maps.GeoCoord(latitude, longitude));

    public Task SimulatePinClick(int index) =>
        MapCallback.Raise(PinClicked, index);

    public Task SimulateDrag(int index, double latitude, double longitude) =>
        MapCallback.Raise(PinDragged, index, new GpsTrainer.Web.Maps.GeoCoord(latitude, longitude));

    public Task SimulateDelete(int index) =>
        MapCallback.Raise(DeleteRequested, index);

    public Task SimulateConnect(int selectedIndex, int targetIndex, string typeName) =>
        MapCallback.Raise(ConnectRequested, selectedIndex, targetIndex, typeName);

    public Task SimulateType(int index, string typeName) =>
        MapCallback.Raise(TypeChangeRequested, index, typeName);

    public Task SimulateInterval(int index, string distance) =>
        MapCallback.Raise(IntervalRequested, index, distance);

    public Task SimulateSummit(int index) =>
        MapCallback.Raise(SummitRequested, index);

    public Task SimulateLoopClose() =>
        MapCallback.Raise(LoopCloseRequested);

    public Task SimulateUndo() =>
        MapCallback.Raise(UndoRequested);

    public Task SimulateInsert(string typeName, double latitude, double longitude, bool route) =>
        MapCallback.Raise(InsertRequested, typeName, new GpsTrainer.Web.Maps.GeoCoord(latitude, longitude), route);

    public Task SimulateText(int index, string text) =>
        MapCallback.Raise(TextChangeRequested, index, text);

    public Task SimulateSummitAt(double latitude, double longitude) =>
        MapCallback.Raise(SummitAtRequested, new GpsTrainer.Web.Maps.GeoCoord(latitude, longitude));

    public Task SimulateStandardFinish(int index, double latitude, double longitude) =>
        MapCallback.Raise(StandardFinishRequested, index, new GpsTrainer.Web.Maps.GeoCoord(latitude, longitude));

    public Task SimulateProfilePlace(string kind, double clientX, double clientY) =>
        MapCallback.Raise(ProfilePlaceRequested, kind, clientX, clientY);

    public Task SimulateProfileInterval(string distance, double clientX, double clientY) =>
        MapCallback.Raise(ProfileIntervalRequested, distance, clientX, clientY);
}
