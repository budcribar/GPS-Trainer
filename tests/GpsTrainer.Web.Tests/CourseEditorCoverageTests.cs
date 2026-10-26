using System.Net;
using System.Net.Http.Json;
using System.Text;
using GpsTrainer.Web.Maps;
using GpsTrainer.Web.Pages;
using GpsTrainer.Web.Services;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace GpsTrainer.Web.Tests;

/// <summary>
/// The 2013 editor menus from DefineCourse.aspx (popupmenu2, popupmenu3, and the pushpin menu),
/// driven through <see cref="FakeCourseMap"/> the same way the Leaflet script reports a choice.
/// </summary>
public sealed class CourseEditorCoverageTests : TestContext
{
    private readonly FakeCourseMap _map = new();
    private readonly CoverageApi _api = new();
    private readonly Session _session = new();

    public CourseEditorCoverageTests()
    {
        _session.Set(new LoginResponse { Token = "token", User = new UserProfileDto { Login = "rider" } });
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_session);
        Services.AddSingleton(new HttpClient(_api) { BaseAddress = new Uri("http://localhost/") });
        Services.AddSingleton<GpsApi>();
        Services.AddSingleton<ICourseMap>(_map);
    }

    [Fact]
    public void The_map_menus_match_the_2013_rows()
    {
        RenderComponent<CourseEditor>();
        var menus = _map.LastFrame!.Menus;

        Assert.Equal(
            ["CONTINUE", "TURN RIGHT", "TURN LEFT", "ZOOM", "MOVE"],
            menus.MapMenuEarlier.Select(row => row.Label).ToArray());
        Assert.Equal(
            ["CONTINUE", "ROUTE", "TURN RIGHT", "TURN LEFT", "FINISH"],
            menus.MapMenuLast.Select(row => row.Label).Take(5).ToArray());
        Assert.Contains(menus.MapMenuLast, row => row.Action == "finish" && row.Label.EndsWith("FINISH", StringComparison.Ordinal));
        Assert.Contains(menus.MapMenuLast, row => row.Label == "ZOOM" && row.Action == "zoom");
        Assert.Contains(menus.MapMenuLast, row => row.Label == "MOVE" && row.Action == "move");
        Assert.Equal(["100m", "200m", "400m", "500m", "800m", "1000m", "1mi"], menus.IntervalDistances.ToArray());
        Assert.Equal(
            ["Continue", "TurnRight", "TurnLeft", "TurnAround", "Finish"],
            menus.Connect.Select(choice => choice.Type).ToArray());
        Assert.Equal(["Delete", "Connect", "Feed zone", "Summit", "Announcement", "Interval", "Zoom"], CourseMapMenus.PinMenu);
    }

    [Fact]
    public async Task Each_map_menu_type_and_each_interval_distance_lands_on_the_course()
    {
        var cut = RenderComponent<CourseEditor>();
        await Draw(cut);
        await cut.InvokeAsync(() => _map.SimulateClick(40.62, -105.05));

        foreach (var type in new[] { "Continue", "TurnRight", "TurnLeft", "Finish" })
        {
            await cut.InvokeAsync(() => _map.SimulateInsert(type, 40.63, -105.04, type == "Continue"));
            Assert.Equal(Enum.Parse<WaypointType>(type), _map.LastFrame!.Pins[^1].Type);
            await cut.InvokeAsync(() => _map.SimulateUndo());
        }

        foreach (var distance in CourseEdits.IntervalDistances)
        {
            var before = _map.LastFrame!.Pins.Count;
            await cut.InvokeAsync(() => _map.SimulateInterval(0, distance));
            Assert.True(_map.LastFrame.Pins.Count > before, cut.Markup);
            Assert.Contains(_map.LastFrame.Pins, pin => pin.Type is WaypointType.StartInterval or WaypointType.FinishInterval);
            await cut.InvokeAsync(() => _map.SimulateUndo());
        }
    }

    [Fact]
    public async Task Connect_offers_every_2013_link_and_a_standard_finish_extends_the_course()
    {
        var cut = RenderComponent<CourseEditor>();
        await Draw(cut);
        var last = _map.LastFrame!.Pins.Count - 1;

        foreach (var type in new[] { "Continue", "TurnRight", "TurnLeft", "TurnAround", "Finish" })
        {
            await cut.InvokeAsync(() => _map.SimulateConnect(last, 0, type));
            Assert.Contains(_map.LastFrame.Pins, pin => pin.Type == Enum.Parse<WaypointType>(type));
        }

        var lengthBefore = _map.LastFrame.Pins.Count;
        await cut.InvokeAsync(() => _map.SimulateStandardFinish(last, 40.70, -105.05));
        Assert.True(_map.LastFrame.Pins.Count > lengthBefore);
        Assert.Equal(WaypointType.Finish, _map.LastFrame.Pins.Last(pin => pin.Type == WaypointType.Finish).Type);
        Assert.Contains("Standard finish added.", cut.Markup);
    }

    [Fact]
    public async Task A_summit_is_placed_at_the_high_point_when_elevation_is_known()
    {
        var cut = RenderComponent<CourseEditor>();
        await Draw(cut);

        await cut.InvokeAsync(() => _map.SimulateSummitAt(40.590, -105.077));

        Assert.Contains(_map.LastFrame!.Pins, pin => pin.Type == WaypointType.Summit);
        Assert.Contains("Summit placed at the high point.", cut.Markup);
    }

    [Fact]
    public async Task The_profile_menu_places_a_turn_around_a_feed_zone_and_an_announcement()
    {
        var cut = RenderComponent<CourseEditor>();
        await Draw(cut);
        Assert.NotNull(cut.Find("#course-profile"));

        await cut.InvokeAsync(() => _map.SimulateProfilePlace("TurnAround", 200, 40));
        await cut.InvokeAsync(() => _map.SimulateProfilePlace("FeedZone", 240, 40));
        await cut.InvokeAsync(() => _map.SimulateProfilePlace("Announcement", 280, 40));

        Assert.Contains(_map.LastFrame!.Pins, pin => pin.Type == WaypointType.TurnAround);
        Assert.Contains(_map.LastFrame.Pins, pin => pin.Type == WaypointType.FeedZone);
        Assert.Contains(_map.LastFrame.Pins, pin => pin.Type == WaypointType.Announcement);
    }

    [Fact]
    public async Task The_profile_interval_row_places_each_2013_distance_on_the_chart()
    {
        var cut = RenderComponent<CourseEditor>();
        await Draw(cut);

        foreach (var distance in CourseEdits.IntervalDistances)
        {
            var before = _map.LastFrame!.Pins.Count;
            await cut.InvokeAsync(() => _map.SimulateProfileInterval(distance, 180, 40));
            Assert.True(_map.LastFrame.Pins.Count > before, distance);
            Assert.Contains("Interval placed on the route.", cut.Markup);
            await cut.InvokeAsync(() => _map.SimulateUndo());
        }
    }

    [Fact]
    public async Task Hovering_the_profile_shows_the_2013_distance_and_altitude_popup()
    {
        var cut = RenderComponent<CourseEditor>();
        await Draw(cut);

        await cut.InvokeAsync(() =>
            cut.Find("#course-profile").TriggerEvent("onmousemove", new MouseEventArgs { ClientX = 200, ClientY = 80 }));

        Assert.Matches(@"\d+\.\d+mi,\d+ft", cut.Find(".profile-hover").TextContent);
    }

    [Fact]
    public async Task A_turn_marker_keeps_the_text_typed_into_its_popup()
    {
        var cut = RenderComponent<CourseEditor>();
        await Draw(cut);

        await cut.InvokeAsync(() => _map.SimulateInsert("TurnRight", 40.63, -105.04, true));
        await cut.InvokeAsync(() => _map.SimulateText(_map.LastFrame!.Pins.Count - 1, "Water at the corner"));

        Assert.Contains("Turn text saved with the marker.", cut.Markup);
        Assert.Contains("Water at the corner", _map.LastFrame!.Pins.Last().Text);
    }

    [Fact]
    public async Task Gpx_export_downloads_the_course_and_import_replaces_it()
    {
        var cut = RenderComponent<CourseEditor>();
        await Draw(cut);

        await cut.InvokeAsync(() => ClickButton(cut, "Download GPX"));
        Assert.EndsWith(".gpx", _map.Downloads.Single().Name, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<gpx", _map.Downloads.Single().Text, StringComparison.OrdinalIgnoreCase);

        await cut.InvokeAsync(() => ClickButton(cut, "Import GPX"));
        var upload = InputFileContent.CreateFromText("<gpx/>", "imported.gpx");
        await cut.InvokeAsync(() => cut.FindComponent<InputFile>().UploadFiles(upload));

        Assert.Contains("Imported loop", cut.Markup);
        Assert.Contains(_map.LastFrame!.Pins, pin => Math.Abs(pin.Latitude - 40.5) < 0.0001);
    }

    [Fact]
    public async Task Saving_and_reopening_the_course_redraws_the_saved_points()
    {
        var cut = RenderComponent<CourseEditor>();
        await Draw(cut);
        await cut.InvokeAsync(() => cut.Find("#PanelDetails input").Change("Hill loop"));
        await cut.InvokeAsync(() => ClickButton(cut, "Save"));
        Assert.Contains("Saved.", cut.Markup);
        Assert.NotEqual(Guid.Empty, _api.SavedCourseId);

        var again = RenderComponent<CourseEditor>(parameters => parameters.Add(editor => editor.CourseId, _api.SavedCourseId));

        Assert.Contains("Hill loop", again.Markup);
        Assert.Contains(_map.LastFrame!.Pins, pin => Math.Abs(pin.Latitude - 40.5853) < 0.0001);
        Assert.DoesNotContain("Restored the course you were drawing.", again.Markup);
    }

    [Fact]
    public async Task Switching_courses_redraws_the_fake_map_with_the_selected_course()
    {
        _api.Seed(CoverageApi.CourseA, "Course A", 40.11, -105.11);
        _api.Seed(CoverageApi.CourseB, "Course B", 41.25, -105.25);
        var cut = RenderComponent<CourseEditor>(parameters => parameters.Add(editor => editor.CourseId, CoverageApi.CourseA));
        Assert.Contains(_map.LastFrame!.Pins, pin => Math.Abs(pin.Latitude - 40.11) < 0.0001);

        cut.SetParametersAndRender(parameters => parameters.Add(editor => editor.CourseId, CoverageApi.CourseB));

        Assert.Contains(_map.LastFrame!.Pins, pin => Math.Abs(pin.Latitude - 41.25) < 0.0001);
        Assert.DoesNotContain(_map.LastFrame.Pins, pin => Math.Abs(pin.Latitude - 40.11) < 0.0001);
        Assert.Contains(_map.Views, view => Math.Abs(view.Latitude - 41.25) < 0.0001);
    }

    [Fact]
    public async Task Course_and_help_dialogs_remember_separate_spots()
    {
        var cut = RenderComponent<CourseEditor>();
        await cut.InvokeAsync(() => cut.Instance.RememberDialog("help", 30, 44));
        await cut.InvokeAsync(() => cut.Instance.RememberDialog("course", 70, 18));

        Assert.Contains("left:30px", cut.Find("#HelpPanel").GetAttribute("style"));
        Assert.Contains("left:70px", cut.Find("#PanelDetails").GetAttribute("style"));
        Assert.Contains(JSInterop.Invocations["gpsTrainerDialogs.save"], call =>
            Equals(call.Arguments[0], EditorDialogLayout.HelpKey) && (call.Arguments[1]?.ToString() ?? "").Contains("30"));
    }

    private async Task Draw(IRenderedComponent<CourseEditor> cut)
    {
        await cut.InvokeAsync(() => _map.SimulateClick(40.5853, -105.0844));
        await cut.InvokeAsync(() => _map.SimulateClick(40.5950, -105.0700));
    }

    private static void ClickButton(IRenderedComponent<CourseEditor> cut, string text) =>
        cut.FindAll("button").First(button => button.TextContent.Contains(text, StringComparison.Ordinal)).Click();

    private sealed class CoverageApi : HttpMessageHandler
    {
        public static readonly Guid CourseA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
        public static readonly Guid CourseB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2");
        private readonly Dictionary<Guid, (Course Course, List<Waypoint> Waypoints)> _saved = [];

        public Guid SavedCourseId { get; private set; }

        public void Seed(Guid id, string name, double latitude, double longitude) =>
            _saved[id] = (new Course { CourseId = id, Name = name, Difficulty = 0 },
            [
                new Waypoint { CourseId = id, Latitude = latitude, Longitude = longitude, WaypointType = WaypointType.Start, TimestampTicks = 1 }
            ]);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.Contains("activity-codes", StringComparison.Ordinal))
                return Json(new[] { new ActivityCode { ActKey = 3, ActValue = "Cycling" } });
            if (path.Contains("directions", StringComparison.Ordinal))
            {
                var body = await request.Content!.ReadFromJsonAsync<RouteRequestDto>(cancellationToken) ?? new RouteRequestDto();
                return Json(new RouteResponseDto
                {
                    Routed = true,
                    Points =
                    [
                        new RoutePointDto
                        {
                            Latitude = (body.FromLatitude + body.ToLatitude) / 2,
                            Longitude = (body.FromLongitude + body.ToLongitude) / 2,
                            AltitudeKm = 1.60
                        },
                        new RoutePointDto
                        {
                            Latitude = body.ToLatitude,
                            Longitude = body.ToLongitude,
                            AltitudeKm = 1.55
                        }
                    ]
                });
            }
            if (path.Contains("elevation", StringComparison.Ordinal))
            {
                var body = await request.Content!.ReadFromJsonAsync<ElevationLineRequestDto>(cancellationToken)
                    ?? new ElevationLineRequestDto();
                return Json(new ElevationLineResponseDto
                {
                    Found = true,
                    Points = body.Points.Select(point => new ElevationSampleDto
                    {
                        Latitude = point.Latitude,
                        Longitude = point.Longitude,
                        ElapsedKm = point.ElapsedKm,
                        AltitudeKm = 1.50 + 0.08 * Math.Sin(point.ElapsedKm * 4)
                    }).ToList()
                });
            }
            if (path.Contains("/export/", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (path.Contains("import/gpx", StringComparison.Ordinal))
            {
                return Json(new GpxImportResult
                {
                    Course = new Course { Name = "Imported loop", Difficulty = 0 },
                    Waypoints =
                    [
                        new Waypoint { Latitude = 40.5, Longitude = -105.1, WaypointType = WaypointType.Start, TimestampTicks = 1 },
                        new Waypoint { Latitude = 40.51, Longitude = -105.1, WaypointType = WaypointType.Finish, TimestampTicks = 2 }
                    ]
                });
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/api/courses", StringComparison.Ordinal))
            {
                var course = await request.Content!.ReadFromJsonAsync<Course>(cancellationToken) ?? new Course();
                if (course.CourseId == Guid.Empty)
                    course.CourseId = Guid.NewGuid();
                SavedCourseId = course.CourseId;
                _saved[course.CourseId] = (course, []);
                return Json(course);
            }
            if (request.Method == HttpMethod.Put && path.Contains("/waypoints", StringComparison.Ordinal))
            {
                var id = Guid.Parse(path.Split('/', StringSplitOptions.RemoveEmptyEntries)[^2]);
                var points = await request.Content!.ReadFromJsonAsync<List<Waypoint>>(cancellationToken) ?? [];
                if (_saved.TryGetValue(id, out var existing))
                    _saved[id] = (existing.Course, points);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            if (path.Contains("/waypoints", StringComparison.Ordinal))
            {
                var id = Guid.Parse(path.Split('/', StringSplitOptions.RemoveEmptyEntries)[^2]);
                return Json(_saved.TryGetValue(id, out var existing) ? existing.Waypoints : []);
            }
            if (path.Contains("/api/courses/", StringComparison.Ordinal))
            {
                var id = Guid.Parse(path.Split('/', StringSplitOptions.RemoveEmptyEntries)[^1]);
                return _saved.TryGetValue(id, out var existing)
                    ? Json(existing.Course)
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json<T>(T body) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
    }
}
