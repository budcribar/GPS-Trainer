using System.Net;
using System.Text;
using System.Text.Json;
using GpsTrainer.Web.Maps;
using GpsTrainer.Web.Pages;
using GpsTrainer.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GpsTrainer.Web.Tests;

public class CourseSwitchTests : TestContext
{
    private static readonly Guid CourseA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
    private static readonly Guid CourseB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2");

    public CourseSwitchTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddScoped(_ => new HttpClient(new CourseHandler()) { BaseAddress = new Uri("http://localhost/") });
        Services.AddScoped<Session>();
        Services.AddScoped<GpsApi>();
        Services.AddScoped<ICourseMap, LeafletCourseMap>();
    }

    [Fact]
    public void Switching_from_course_A_to_course_B_redraws_B_on_the_map()
    {
        var cut = RenderComponent<CourseEditor>(parameters => parameters.Add(editor => editor.CourseId, CourseA));

        cut.SetParametersAndRender(parameters => parameters.Add(editor => editor.CourseId, CourseB));

        var redraw = LastWaypoints();
        Assert.Contains("41.25", redraw);
        Assert.DoesNotContain("40.11", redraw);
        Assert.Contains("Course B", cut.Markup);
    }

    [Fact]
    public void Opening_a_saved_course_from_the_blank_editor_redraws_that_course()
    {
        var draft = EditorDraft.Serialize(
            new Course { Name = "Pencil draft" },
            [new Waypoint { Latitude = 39.11, Longitude = -105.2, WaypointType = WaypointType.Start, TimestampTicks = 1 }],
            "00:20:00");
        JSInterop.Setup<string?>("gpsTrainerDraft.load").SetResult(draft);

        var cut = RenderComponent<CourseEditor>();
        Assert.Contains("Pencil draft", cut.Markup);
        Assert.Contains("Restored the course you were drawing.", cut.Markup);

        cut.SetParametersAndRender(parameters => parameters.Add(editor => editor.CourseId, CourseB));

        var redraw = LastWaypoints();
        Assert.Contains("41.25", redraw);
        Assert.DoesNotContain("39.11", redraw);
        Assert.Contains("Course B", cut.Markup);
        Assert.DoesNotContain("Restored the course you were drawing.", cut.Markup);
        Assert.Single(JSInterop.Invocations["gpsTrainerDraft.load"]);
        var savedDraft = JSInterop.Invocations["gpsTrainerDraft.save"].Single().Arguments[0]?.ToString();
        Assert.Contains("Pencil draft", savedDraft);
        Assert.DoesNotContain("Course B", savedDraft);
    }

    private string LastWaypoints()
    {
        var call = JSInterop.Invocations["gpsTrainerCourseEditor.setWaypoints"].Last();
        return JsonSerializer.Serialize(call.Arguments[0]);
    }

    private sealed class CourseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            string json;
            if (path.Contains("activity-codes", StringComparison.Ordinal))
                json = """[{"actKey":3,"actValue":"Cycling"}]""";
            else if (path.Contains("/waypoints", StringComparison.Ordinal))
            {
                var latitude = path.Contains(CourseB.ToString(), StringComparison.OrdinalIgnoreCase) ? 41.25 : 40.11;
                json = $$"""[{"latitude":{{latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"longitude":-105.5,"waypointType":11,"timestampTicks":1}]""";
            }
            else if (path.Contains(CourseB.ToString(), StringComparison.OrdinalIgnoreCase))
                json = $$"""{"courseId":"{{CourseB}}","name":"Course B","difficulty":0}""";
            else if (path.Contains(CourseA.ToString(), StringComparison.OrdinalIgnoreCase))
                json = $$"""{"courseId":"{{CourseA}}","name":"Course A","difficulty":0}""";
            else
                json = "[]";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
