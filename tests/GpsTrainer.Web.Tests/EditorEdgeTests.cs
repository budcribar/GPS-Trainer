using GpsTrainer.Web.Maps;
using GpsTrainer.Web.Pages;
using GpsTrainer.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GpsTrainer.Web.Tests;

/// <summary>
/// Editor saves and marker edits against the fake map.
/// The KnownBug test asserts the distance that should be stored.
/// </summary>
public sealed class EditorEdgeTests : TestContext
{
    private readonly FakeCourseMap _map = new();
    private readonly EditorApi _api = new();
    private readonly Session _session = new();

    public EditorEdgeTests()
    {
        _session.Set(new LoginResponse { Token = "token", User = new UserProfileDto { Login = "bud" } });
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_session);
        Services.AddSingleton(new HttpClient(_api) { BaseAddress = new Uri("http://localhost/") });
        Services.AddSingleton<GpsApi>();
        Services.AddSingleton<ICourseMap>(_map);
    }

    [Fact]
    public async Task Saving_a_drawn_course_stores_its_distance_and_an_empty_name_becomes_untitled()
    {
        var cut = Render();
        await Draw(cut);
        await cut.InvokeAsync(() => ClickButton(cut, "Save"));

        Assert.Contains("Saved.", cut.Markup);
        Assert.NotEmpty(_api.SavedCourses);
        Assert.Equal("Untitled course", _api.SavedCourses[0].Name);
        Assert.True(_api.SavedCourses[^1].ElapsedDistanceKm > 0.5, "distance was " + _api.SavedCourses[^1].ElapsedDistanceKm);
        Assert.NotEmpty(_api.SavedWaypoints);
        Assert.Equal(WaypointType.Start, _api.SavedWaypoints[0][0].WaypointType);
    }

    [Fact]
    public async Task A_name_with_markup_is_stored_as_text()
    {
        var cut = Render();
        await Draw(cut);
        await cut.InvokeAsync(() => cut.Find("#PanelDetails input").Change("<b>North</b>"));
        await cut.InvokeAsync(() => ClickButton(cut, "Save"));
        Assert.Equal("<b>North</b>", _api.SavedCourses[0].Name);
        Assert.Equal("<b>North</b>", cut.Find("#PanelDetails input").GetAttribute("value"));
    }

    [Fact]
    public async Task KnownBug_Ctrl_Z_on_an_empty_course_says_there_is_nothing_to_undo()
    {
        var cut = Render();
        await cut.InvokeAsync(() => _map.SimulateUndo());
        Assert.Contains("Nothing to undo.", cut.Markup);
    }

    [Fact]
    public async Task Turn_text_delete_of_the_start_and_a_too_close_click_follow_the_menu()
    {
        var cut = Render();
        await Draw(cut);
        var before = _map.LastFrame!.Pins.Count;
        await cut.InvokeAsync(() => _map.SimulateType(before - 1, "TurnRight"));
        Assert.Contains("Turn right", cut.Markup);
        await cut.InvokeAsync(() => _map.SimulateText(before - 1, "<img src=x>"));
        Assert.Contains("Turn text saved", cut.Markup);
        Assert.Equal("<img src=x>", _map.LastFrame.Pins.Last(pin => pin.Type == WaypointType.TurnRight).Text);

        await cut.InvokeAsync(() => _map.SimulateDelete(0));
        Assert.Contains("Waypoint deleted.", cut.Markup);
        Assert.Equal(WaypointType.Start, _map.LastFrame.Pins[0].Type);
        Assert.Equal(before - 1, _map.LastFrame.Pins.Count);

        var count = _map.LastFrame.Pins.Count;
        await cut.InvokeAsync(() => _map.SimulateClick(_map.LastFrame.Pins[^1].Latitude, _map.LastFrame.Pins[^1].Longitude));
        Assert.Contains("too close", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(count, _map.LastFrame.Pins.Count);
    }

    [Fact]
    public async Task Finish_on_the_start_draws_the_combined_marker_and_a_later_move_separates_them()
    {
        var cut = Render();
        await Draw(cut);
        await cut.InvokeAsync(() => _map.SimulateLoopClose());
        Assert.Contains("Loop closed at the start.", cut.Markup);
        Assert.Contains(_map.LastFrame!.Pins, pin => pin.Type == WaypointType.Finish);
        Assert.True(LoopClose.ShowsStartFinish(_map.LastFrame.Pins.Select(pin => new Waypoint
        {
            Latitude = pin.Latitude,
            Longitude = pin.Longitude,
            WaypointType = pin.Type
        })));

        var finish = _map.LastFrame.Pins.Select((pin, index) => (pin, index)).Last(item => item.pin.Type == WaypointType.Finish);
        await cut.InvokeAsync(() => _map.SimulateDrag(finish.index, 40.70, -105.20));
        Assert.Contains("Waypoint moved.", cut.Markup);
        Assert.False(LoopClose.ShowsStartFinish(_map.LastFrame.Pins.Select(pin => new Waypoint
        {
            Latitude = pin.Latitude,
            Longitude = pin.Longitude,
            WaypointType = pin.Type
        })));
    }

    [Fact]
    public async Task KnownBug_Saving_after_an_interval_keeps_the_course_distance()
    {
        var cut = Render();
        await Draw(cut);
        await cut.InvokeAsync(() => _map.SimulateInterval(0, "100m"));
        Assert.Contains(_map.LastFrame!.Pins, pin => pin.Type == WaypointType.StartInterval);
        Assert.Contains("Interval placed", cut.Markup);

        await cut.InvokeAsync(() => cut.Find("#PanelDetails input").Change("Interval loop"));
        await cut.InvokeAsync(() => ClickButton(cut, "Save"));

        Assert.Contains("Saved.", cut.Markup);
        Assert.True(
            _api.SavedCourses[^1].ElapsedDistanceKm > 0.5,
            "The course list would show 0.00. Stored distance was " + _api.SavedCourses[^1].ElapsedDistanceKm + ".");
    }

    private IRenderedComponent<CourseEditor> Render() => RenderComponent<CourseEditor>();

    private async Task Draw(IRenderedComponent<CourseEditor> cut)
    {
        await cut.InvokeAsync(() => _map.SimulateClick(40.5853, -105.0844));
        await cut.InvokeAsync(() => _map.SimulateClick(40.5950, -105.0700));
    }

    private static void ClickButton(IRenderedComponent<CourseEditor> cut, string text) =>
        cut.FindAll("button").First(button => button.TextContent.Contains(text, StringComparison.Ordinal)).Click();
}
