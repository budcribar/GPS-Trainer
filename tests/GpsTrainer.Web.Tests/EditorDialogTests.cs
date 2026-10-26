using System.Net;
using System.Text;
using GpsTrainer.Web.Maps;
using GpsTrainer.Web.Pages;
using GpsTrainer.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GpsTrainer.Web.Tests;

public class EditorDialogLayoutTests
{
    [Fact]
    public void A_wide_screen_position_resets_on_a_phone()
    {
        var saved = new DialogSpot { Left = 900, Top = 10 };
        Assert.True(EditorDialogLayout.IsOffScreen(saved, 390, 700, 200, 180));

        var placed = EditorDialogLayout.Place(saved, course: true, 390, 700, 200, 180);

        Assert.Equal(182, placed.Left);
        Assert.Equal(EditorDialogLayout.Margin, placed.Top);
    }

    [Fact]
    public void A_dialog_taller_than_the_viewport_is_clamped_instead_of_reset()
    {
        var saved = new DialogSpot { Left = 24, Top = 40 };
        Assert.False(EditorDialogLayout.IsOffScreen(saved, 390, 500, 200, 700));

        var placed = EditorDialogLayout.Place(saved, course: true, 390, 500, 200, 700);

        Assert.Equal(24, placed.Left);
        Assert.Equal(EditorDialogLayout.Margin, placed.Top);
    }

    [Fact]
    public void Help_opens_at_the_2013_spot_beside_the_map()
    {
        var placed = EditorDialogLayout.Place(null, course: false, 1400, 900, 352, 340);

        Assert.Equal(EditorDialogLayout.HelpDefaultLeft, placed.Left);
        Assert.Equal(EditorDialogLayout.HelpDefaultTop, placed.Top);
    }

    [Fact]
    public void A_saved_help_spot_over_a_control_resets_beside_the_map()
    {
        var saved = new DialogSpot { Left = 8, Top = 8 };
        var zoom = new DialogRect { Left = 90, Top = 70, Width = 120, Height = 36 };
        var placed = EditorDialogLayout.Place(saved, course: false, 1400, 900, 352, 340, [zoom]);

        Assert.Equal(EditorDialogLayout.HelpDefaultLeft, placed.Left);
        Assert.Equal(EditorDialogLayout.HelpDefaultTop, placed.Top);
        Assert.False(EditorDialogLayout.Hits(placed, 352, 340, [zoom]));
    }

    [Fact]
    public void A_negative_position_resets_toward_the_2013_help_spot()
    {
        var saved = new DialogSpot { Left = 24, Top = -4 };
        var placed = EditorDialogLayout.Place(saved, course: false, 800, 600, 200, 180);

        Assert.Equal(592, placed.Left);
        Assert.Equal(412, placed.Top);
    }

    [Fact]
    public void A_spot_that_still_fits_is_kept()
    {
        var saved = new DialogSpot { Left = 40, Top = 30 };
        var placed = EditorDialogLayout.Place(saved, course: true, 800, 600, 200, 180);

        Assert.Equal(40, placed.Left);
        Assert.Equal(30, placed.Top);
    }

    [Fact]
    public void Positions_round_trip_through_json()
    {
        var json = EditorDialogLayout.Serialize(12.5, 40);
        Assert.True(EditorDialogLayout.TryParse(json, out var spot));
        Assert.Equal(12.5, spot.Left);
        Assert.Equal(40, spot.Top);
        Assert.False(EditorDialogLayout.TryParse(" ", out _));
    }
}

public class EditorDialogComponentTests : TestContext
{
    public EditorDialogComponentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddScoped(_ => new HttpClient(new ActivityHandler()) { BaseAddress = new Uri("http://localhost/") });
        Services.AddScoped<Session>();
        Services.AddScoped<GpsApi>();
        Services.AddScoped<ICourseMap, LeafletCourseMap>();
    }

    [Fact]
    public void Course_and_help_open_close_and_reopen_over_the_map()
    {
        var cut = RenderComponent<CourseEditor>();

        Assert.NotNull(cut.Find("#course-map"));
        Assert.Contains("editor-dialog", cut.Find("#PanelDetails").ClassName);
        Assert.Contains("editor-dialog", cut.Find("#HelpPanel").ClassName);
        Assert.Contains(JSInterop.Invocations, call => call.Identifier == "gpsTrainerDialogs.watch");

        cut.Find("[aria-label='Close course']").Click();
        cut.Find("[aria-label='Close help']").Click();
        Assert.Empty(cut.FindAll("#PanelDetails"));
        Assert.Empty(cut.FindAll("#HelpPanel"));
        Assert.NotNull(cut.Find("#course-map"));

        cut.Find(".editor-menu button").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#PanelDetails")));
    }

    [Fact]
    public async Task Drag_position_is_stored_for_the_next_session()
    {
        var cut = RenderComponent<CourseEditor>();

        await cut.InvokeAsync(() => cut.Instance.RememberDialog("course", 48, 64));

        var save = JSInterop.Invocations["gpsTrainerDialogs.save"].Last();
        Assert.Equal(EditorDialogLayout.CourseKey, save.Arguments[0]);
        Assert.Contains("48", save.Arguments[1]?.ToString());
        Assert.Contains("64", save.Arguments[1]?.ToString());
        Assert.Contains("left:48px", cut.Find("#PanelDetails").GetAttribute("style"));
        Assert.Contains("top:64px", cut.Find("#PanelDetails").GetAttribute("style"));
    }

    [Fact]
    public void An_off_screen_saved_spot_resets_when_the_dialog_opens()
    {
        JSInterop.Setup<string?>("gpsTrainerDialogs.load", EditorDialogLayout.CourseKey)
            .SetResult("""{"left":900,"top":12}""");
        JSInterop.Setup<DialogSize?>("gpsTrainerDialogs.measure", _ => true).SetResult(new DialogSize
        {
            Width = 200,
            Height = 180,
            ViewportWidth = 390,
            ViewportHeight = 700
        });

        var cut = RenderComponent<CourseEditor>();

        cut.WaitForAssertion(() =>
        {
            var style = cut.Find("#PanelDetails").GetAttribute("style") ?? "";
            Assert.Contains("left:182px", style);
            Assert.Contains("top:8px", style);
            Assert.DoesNotContain("900", style);
        });
    }

    [Fact]
    public void A_toolbar_saved_spot_resets_when_help_opens()
    {
        JSInterop.Setup<string?>("gpsTrainerDialogs.load", EditorDialogLayout.HelpKey)
            .SetResult("""{"left":8,"top":8}""");
        JSInterop.Setup<DialogSize?>("gpsTrainerDialogs.measure", _ => true).SetResult(new DialogSize
        {
            Width = 352,
            Height = 340,
            ViewportWidth = 1400,
            ViewportHeight = 900,
            Blocked = [new DialogRect { Left = 90, Top = 80, Width = 110, Height = 36 }]
        });

        var cut = RenderComponent<CourseEditor>();

        cut.WaitForAssertion(() =>
        {
            var style = cut.Find("#HelpPanel").GetAttribute("style") ?? "";
            Assert.Contains("left:800px", style);
            Assert.Contains("top:545px", style);
        });
    }

    [Fact]
    public void Reflow_resets_a_spot_that_no_longer_fits()
    {
        JSInterop.Setup<string?>("gpsTrainerDialogs.load", EditorDialogLayout.HelpKey)
            .SetResult("""{"left":20,"top":-2}""");
        JSInterop.Setup<DialogSize?>("gpsTrainerDialogs.measure", _ => true).SetResult(new DialogSize
        {
            Width = 200,
            Height = 180,
            ViewportWidth = 800,
            ViewportHeight = 600
        });
        var cut = RenderComponent<CourseEditor>();

        cut.InvokeAsync(() => cut.Instance.ReflowDialogs());

        cut.WaitForAssertion(() =>
        {
            var style = cut.Find("#HelpPanel").GetAttribute("style") ?? "";
            Assert.Contains("left:592px", style);
            Assert.Contains("top:412px", style);
        });
    }

    private sealed class ActivityHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = request.RequestUri?.AbsolutePath.Contains("activity-codes", StringComparison.Ordinal) == true
                ? """[{"actKey":3,"actValue":"Cycling"}]"""
                : "[]";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
