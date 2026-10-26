using System.Net;
using System.Text;
using GpsTrainer.Web.Maps;
using GpsTrainer.Web.Pages;
using GpsTrainer.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GpsTrainer.Web.Tests;

public class CourseEditorLayoutTests : TestContext
{
    public CourseEditorLayoutTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddScoped(_ => new HttpClient(new ActivityHandler()) { BaseAddress = new Uri("http://localhost/") });
        Services.AddScoped<Session>();
        Services.AddScoped<GpsApi>();
        Services.AddScoped<ICourseMap, LeafletCourseMap>();
    }

    [Fact]
    public void The_map_fills_the_editor_and_the_profile_sits_below_it()
    {
        var draft = EditorDraft.Serialize(
            new Course { Name = "Window course" },
            [
                new Waypoint { Latitude = 40.0, Longitude = -105.0, WaypointType = WaypointType.Start, TimestampTicks = 1 },
                new Waypoint { Latitude = 40.02, Longitude = -105.0, WaypointType = WaypointType.Finish, TimestampTicks = 2 }
            ],
            "00:40:00");
        JSInterop.Setup<string?>("gpsTrainerDraft.load").SetResult(draft);
        JSInterop.Setup<bool>("gpsTrainerCourseEditor.showProfile").SetResult(true);
        JSInterop.Setup<string>("gpsTrainerCourseEditor.elevationUnits", _ => true).SetResult("english");

        var cut = RenderComponent<CourseEditor>();

        var map = cut.Find("#course-map");
        var profile = cut.Find("#PanelProfile");
        Assert.Contains("course-map-fill", map.ClassName);
        Assert.Contains("profile-below", profile.ClassName);
        Assert.Null(map.QuerySelector("#PanelProfile"));
        Assert.Contains("editor-shell", cut.Find(".editor-shell").ClassName);
        Assert.Contains("Window course", cut.Markup);
        Assert.Contains("Restored the course you were drawing.", cut.Markup);
        var bar = cut.Find(".editor-bar");
        Assert.Contains("editor-brand", bar.InnerHtml);
        Assert.Contains("SmallLogo.jpg", bar.InnerHtml);
        Assert.Contains("gps-trainer", bar.TextContent);
        Assert.Contains("domain-com", bar.InnerHtml);
        Assert.Contains("Zoom To", bar.TextContent);
        Assert.DoesNotContain("gpsTrainer", bar.TextContent);
        Assert.Contains("editor-dialog", cut.Find("#PanelDetails").ClassName);
        Assert.DoesNotContain("with-side", cut.Find(".editor-columns").ClassName);

        var css = File.ReadAllText(Path.Combine(RepoRoot(), "src", "GpsTrainer.Web", "wwwroot", "css", "app.css"));
        Assert.Contains(".profile-panel { position: static;", css, StringComparison.Ordinal);
        Assert.Contains("62dvh", css, StringComparison.Ordinal);
        Assert.Contains("100dvh", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".profile-panel { position: absolute;", css, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GPS-Trainer.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
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
