using System.Net.Http;
using GpsTrainer.Web.Maps;
using GpsTrainer.Web.Pages;
using GpsTrainer.Web.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace GpsTrainer.Web.Tests;

public sealed class CourseEditorTests : TestContext
{
    private readonly FakeCourseMap _map = new();
    private readonly EditorApi _api = new();
    private readonly Session _session = new();

    public CourseEditorTests()
    {
        _session.Set(new LoginResponse { Token = "token", User = new UserProfileDto { Login = "bud" } });
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_session);
        Services.AddSingleton(new HttpClient(_api) { BaseAddress = new Uri("http://localhost/") });
        Services.AddSingleton<GpsApi>();
        Services.AddSingleton<ICourseMap>(_map);
    }

    [Fact]
    public void A_new_course_opens_in_miles_with_the_2013_pin_menu_and_cycling()
    {
        var cut = RenderComponent<CourseEditor>();

        Assert.Contains("0.00 mi", cut.Find(".distance-readout").TextContent);
        Assert.Equal("km", cut.Find(".units-button").TextContent.Trim());
        Assert.Equal("0/0 ft", GainLoss(cut));
        Assert.Equal("Cycling", SelectedActivity(cut));
        Assert.Equal(9, cut.Find("textarea").GetAttribute("rows") is string rows ? int.Parse(rows) : 0);
        Assert.Empty(cut.FindAll("input[type=number]"));
        Assert.Equal(["Delete", "Connect", "Feed zone", "Summit", "Announcement", "Interval", "Zoom"], CourseMapMenus.PinMenu);
        Assert.Equal(["Feed zone", "Summit", "Announcement"], _map.LastFrame!.Menus.PinTypes.Select(choice => choice.Label).ToArray());
        Assert.Contains(_map.LastFrame.Menus.Connect, choice => choice.Type == "Finish" && choice.Label == "Finish");
        Assert.Contains(_map.LastFrame.Menus.IntervalDistances, distance => distance == "100m");
        Assert.Contains(_map.LastFrame.Menus.MapMenuLast, row => row.Label == "FINISH");
        Assert.Contains(_map.LastFrame.Menus.MapMenuLast, row => row.Label == "ZOOM");
        Assert.DoesNotContain(_map.LastFrame.Menus.PinTypes, choice => choice.Type is "Continue" or "TurnLeft" or "Finish");
    }

    [Fact]
    public async Task A_signed_out_editor_asks_the_server_to_follow_roads()
    {
        _session.Clear();
        var cut = RenderComponent<CourseEditor>();
        Assert.Contains("Sign in before saving.", cut.Find(".editor-signin").TextContent);
        Assert.DoesNotContain("Road-following", cut.Markup);

        await cut.InvokeAsync(() => _map.SimulateClick(40.5853, -105.0844));
        await cut.InvokeAsync(() => _map.SimulateClick(40.5950, -105.0700));

        Assert.Single(_api.Routes);
        Assert.True(_map.LastFrame!.Pins.Count > 2);
    }

    [Fact]
    public async Task The_km_button_switches_the_distance_and_is_remembered()
    {
        var cut = RenderComponent<CourseEditor>();
        await cut.InvokeAsync(() => cut.Find(".units-button").Click());

        Assert.Contains("0.00 km", cut.Find(".distance-readout").TextContent);
        Assert.Equal("mi", cut.Find(".units-button").TextContent.Trim());
        Assert.Equal("0/0 m", GainLoss(cut));
        Assert.Contains(_map.UnitWrites, write => write.Value == "metric");

        var again = RenderComponent<CourseEditor>();
        Assert.Contains("0.00 km", again.Find(".distance-readout").TextContent);
        Assert.Equal("mi", again.Find(".units-button").TextContent.Trim());
    }

    [Fact]
    public void Stored_metric_units_open_the_page_in_kilometers()
    {
        _map.StoredUnits = "metric";
        var cut = RenderComponent<CourseEditor>();
        Assert.Contains("0.00 km", cut.Find(".distance-readout").TextContent);
        Assert.Equal("mi", cut.Find(".units-button").TextContent.Trim());
    }

    [Fact]
    public async Task Import_shows_the_tolerance_only_after_it_is_chosen()
    {
        var cut = RenderComponent<CourseEditor>();
        Assert.Empty(cut.FindAll("input[type=number]"));

        await cut.InvokeAsync(() => ClickButton(cut, "Import GPX"));

        var tolerance = cut.Find("input[type=number]");
        var shown = tolerance.GetAttribute("value") ?? tolerance.GetAttribute("min");
        Assert.Equal("10", shown);
        Assert.NotNull(cut.Find("input[type=file]"));
    }

    [Fact]
    public async Task Clicks_follow_a_faked_road_route_and_undo_removes_that_segment()
    {
        var cut = RenderComponent<CourseEditor>();
        await cut.InvokeAsync(() => _map.SimulateClick(40.5853, -105.0844));
        await cut.InvokeAsync(() => _map.SimulateClick(40.5950, -105.0700));

        Assert.NotEmpty(_api.Routes);
        Assert.Equal("Cycling", _api.Routes[0].Activity);
        Assert.True(_map.LastFrame!.Pins.Count >= 3);
        Assert.Contains("PROFILE", cut.Markup);

        await cut.InvokeAsync(() => ClickButton(cut, "Undo last segment"));

        Assert.Single(_map.LastFrame.Pins);
        Assert.Equal(WaypointType.Start, _map.LastFrame.Pins[0].Type);
        Assert.Contains("Removed the last segment.", cut.Markup);
    }

    [Fact]
    public async Task A_right_click_or_pin_click_does_not_add_a_point_and_a_drag_moves_one()
    {
        var cut = RenderComponent<CourseEditor>();
        await cut.InvokeAsync(() => _map.SimulateClick(40.5853, -105.0844));
        await cut.InvokeAsync(() => _map.SimulateRightClick(40.60, -105.07));
        await cut.InvokeAsync(() => _map.SimulatePinClick(0));
        Assert.Single(_map.LastFrame!.Pins);

        await cut.InvokeAsync(() => _map.SimulateDrag(0, 40.70, -105.20));

        Assert.Equal(40.70, _map.LastFrame.Pins[0].Latitude, 5);
        Assert.Equal(-105.20, _map.LastFrame.Pins[0].Longitude, 5);
        Assert.Contains("Waypoint moved.", cut.Markup);
    }

    [Fact]
    public async Task The_pin_menu_can_delete_connect_set_a_type_place_an_interval_and_ask_for_a_summit()
    {
        var cut = RenderComponent<CourseEditor>();
        await Draw(cut);

        await cut.InvokeAsync(() => _map.SimulateType(1, "FeedZone"));
        Assert.Equal(WaypointType.FeedZone, _map.LastFrame!.Pins[1].Type);
        await cut.InvokeAsync(() => _map.SimulateType(1, "Announcement"));
        Assert.Equal(WaypointType.Announcement, _map.LastFrame.Pins[1].Type);

        var before = _map.LastFrame.Pins.Count;
        await cut.InvokeAsync(() => _map.SimulateInterval(1, "100m"));
        Assert.True(_map.LastFrame.Pins.Count > before);
        Assert.Contains(_map.LastFrame.Pins, pin => pin.Type is WaypointType.StartInterval or WaypointType.FinishInterval);

        await cut.InvokeAsync(() => _map.SimulateSummit(1));
        Assert.Contains("summit", cut.Markup, StringComparison.OrdinalIgnoreCase);

        var last = _map.LastFrame.Pins.Count - 1;
        await cut.InvokeAsync(() => _map.SimulateConnect(0, last, "TurnRight"));
        Assert.Contains(_map.LastFrame.Pins, pin => pin.Type == WaypointType.TurnRight);

        var count = _map.LastFrame.Pins.Count;
        await cut.InvokeAsync(() => _map.SimulateDelete(0));
        Assert.True(_map.LastFrame.Pins.Count < count);
        Assert.Contains("Waypoint deleted.", cut.Markup);
    }

    [Fact]
    public async Task Finish_on_the_start_closes_the_loop_along_the_road()
    {
        var cut = RenderComponent<CourseEditor>();
        await Draw(cut);
        var routesBefore = _api.Routes.Count;

        await cut.InvokeAsync(() => _map.SimulateLoopClose());

        Assert.True(_api.Routes.Count > routesBefore);
        var closing = _api.Routes[^1];
        Assert.Equal(40.5853, closing.ToLatitude, 4);
        Assert.Equal(-105.0844, closing.ToLongitude, 4);
        Assert.Equal(WaypointType.Finish, _map.LastFrame!.Pins[^1].Type);
        Assert.Contains("Loop closed at the start.", cut.Markup);
        Assert.Null(closing.SnapRadiusMeters);
    }

    [Fact]
    public async Task Finish_on_an_off_road_start_follows_the_road_then_snaps_to_the_pin()
    {
        var cut = RenderComponent<CourseEditor>();
        await Draw(cut);
        _api.UnroutableBeforeSnap = 1;
        _api.SnapMissesStart = true;

        await cut.InvokeAsync(() => _map.SimulateLoopClose());

        var closing = _api.Routes.Where(route => Math.Abs(route.ToLatitude - 40.5853) < 0.0001).ToList();
        Assert.Equal(2, closing.Count);
        Assert.Null(closing[0].SnapRadiusMeters);
        Assert.Equal(LoopClose.CloseSnapMeters, closing[1].SnapRadiusMeters);
        var finish = _map.LastFrame!.Pins[^1];
        Assert.Equal(WaypointType.Finish, finish.Type);
        Assert.Equal(40.5853, finish.Latitude, 5);
        Assert.Equal(-105.0844, finish.Longitude, 5);
        Assert.Contains(_map.LastFrame.Pins, pin =>
            pin.Type != WaypointType.Finish
            && GeoMath.DistanceMeters(pin.Latitude, pin.Longitude, 40.5853, -105.0844) > RoutePath.SamePointMeters
            && GeoMath.DistanceMeters(pin.Latitude, pin.Longitude, 40.5853, -105.0844) < 80);
        Assert.Contains("Loop closed at the start.", cut.Markup);
        Assert.DoesNotContain("routable", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("S/F", LoopClose.MarkerGlyph(WaypointType.Start, startAndFinish: true));
    }

    [Fact]
    public async Task Finish_still_closes_when_the_road_search_fails_and_hides_the_router_error()
    {
        var cut = RenderComponent<CourseEditor>();
        await Draw(cut);
        _api.NeverRoutes = true;

        await cut.InvokeAsync(() => _map.SimulateLoopClose());

        var closing = _api.Routes.Where(route => Math.Abs(route.ToLatitude - 40.5853) < 0.0001).ToList();
        Assert.Equal(2, closing.Count);
        Assert.Equal(LoopClose.CloseSnapMeters, closing[1].SnapRadiusMeters);
        Assert.Equal(WaypointType.Finish, _map.LastFrame!.Pins[^1].Type);
        Assert.Equal(40.5853, _map.LastFrame.Pins[^1].Latitude, 5);
        Assert.Equal(-105.0844, _map.LastFrame.Pins[^1].Longitude, 5);
        Assert.Contains("Loop closed at the start.", cut.Markup);
        Assert.DoesNotContain("routable", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("350", cut.Markup);
    }

    [Fact]
    public async Task Clicking_the_profile_opens_its_menu()
    {
        var cut = RenderComponent<CourseEditor>();
        await Draw(cut);

        Assert.NotNull(cut.Find("#PanelProfile"));
        await cut.InvokeAsync(() => cut.Find(".profile-chart").Click(new MouseEventArgs { ClientX = 120, ClientY = 40 }));

        Assert.Equal((120, 40), _map.ProfileMenus.Single());
    }

    private async Task Draw(IRenderedComponent<CourseEditor> cut)
    {
        await cut.InvokeAsync(() => _map.SimulateClick(40.5853, -105.0844));
        await cut.InvokeAsync(() => _map.SimulateClick(40.5950, -105.0700));
    }

    private static string GainLoss(IRenderedComponent<CourseEditor> cut) =>
        cut.FindAll(".detail-row")
            .First(row => row.TextContent.Contains("Altitude", StringComparison.Ordinal))
            .QuerySelector("span")!
            .TextContent
            .Trim();

    private static string SelectedActivity(IRenderedComponent<CourseEditor> cut)
    {
        var select = cut.FindAll("select")[0];
        var value = select.GetAttribute("value");
        var option = select.QuerySelector($"option[value='{value}']") ?? select.QuerySelector("option[selected]");
        return option?.TextContent.Trim() ?? "";
    }

    private static void ClickButton(IRenderedComponent<CourseEditor> cut, string text) =>
        cut.FindAll("button").First(button => button.TextContent.Contains(text, StringComparison.Ordinal)).Click();
}
