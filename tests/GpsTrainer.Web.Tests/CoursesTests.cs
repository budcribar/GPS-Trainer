using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GpsTrainer.Web.Pages;
using GpsTrainer.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GpsTrainer.Web.Tests;

public sealed class CoursesTests : TestContext
{
    private readonly CoursesApi _api = new();
    private readonly Session _session = new();
    private readonly Guid _loop = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private readonly Guid _run = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private readonly Guid _mystery = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    public CoursesTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_session);
        Services.AddSingleton(new HttpClient(_api) { BaseAddress = new Uri("http://localhost/") });
        Services.AddSingleton<GpsApi>();
        _api.Activities.AddRange(
        [
            new ActivityCode { ActKey = 1, ActValue = "Running" },
            new ActivityCode { ActKey = 3, ActValue = "Cycling" },
            new ActivityCode { ActKey = 5, ActValue = "XCountry Skate" },
            new ActivityCode { ActKey = 8, ActValue = "XCountry Classic" }
        ]);
        _api.Courses.Add(new Course { CourseId = _loop, Name = "Poudre loop", Login = "bud", ActivityCode = 3, ElapsedDistanceKm = 18.4 });
        _api.Courses.Add(new Course { CourseId = _run, Name = "Spring creek", Login = "bud", ActivityCode = 1, ElapsedDistanceKm = 8.2 });
        _api.Courses.Add(new Course { CourseId = _mystery, Name = "Mystery outing", Login = "someone", ActivityCode = 42, ElapsedDistanceKm = 3.5 });
        _api.WithIntervals.Add(_loop);
    }

    [Fact]
    public void A_signed_out_visitor_is_asked_to_sign_in()
    {
        var cut = RenderComponent<Courses>();

        Assert.Contains("gpstrainer-icon.svg", cut.Find(".courses-mark").GetAttribute("src"));
        Assert.Equal("Courses", cut.Find("h1").TextContent);
        Assert.Contains("Sign in to see the courses enabled for download on your login.", cut.Markup);
        Assert.Empty(cut.FindAll("table"));
        Assert.Contains("New course", cut.Find("a[href='editor']").TextContent);
        Assert.Contains("Generate a Fort Collins loop", cut.Find("a[href='generate']").TextContent);
        Assert.Contains("Find a course", cut.Find("a[href='find']").TextContent);
        Assert.DoesNotContain("#E8B836", CardRule(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("border-top", CardRule(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Activity_names_come_from_the_activity_codes_and_unknown_codes_stay_words()
    {
        SignIn();
        var cut = RenderComponent<Courses>();

        var rows = cut.FindAll("tbody tr");
        Assert.Equal(3, rows.Count);
        Assert.Equal("Cycling", rows[0].QuerySelector(".courses-activity")!.TextContent);
        Assert.Equal("Running", rows[1].QuerySelector(".courses-activity")!.TextContent);
        Assert.Equal("Unknown activity", rows[2].QuerySelector(".courses-activity")!.TextContent);
        Assert.DoesNotContain(">42<", cut.Markup);
        Assert.DoesNotContain(">3<", cut.Markup);
        Assert.Equal("18.40", rows[0].QuerySelector(".courses-distance")!.TextContent);
        Assert.Contains("Distance km", cut.Find("thead").TextContent);
    }

    [Fact]
    public void The_intervals_link_appears_only_when_the_course_has_intervals()
    {
        SignIn();
        var cut = RenderComponent<Courses>();

        var rows = cut.FindAll("tbody tr");
        Assert.Equal($"intervals/{_loop}", rows[0].QuerySelector("a[href^='intervals/']")!.GetAttribute("href"));
        Assert.Equal("Intervals", rows[0].QuerySelector("a[href^='intervals/']")!.TextContent);
        Assert.Empty(rows[1].QuerySelectorAll("a[href^='intervals/']"));
        Assert.Empty(rows[2].QuerySelectorAll("a[href^='intervals/']"));
        Assert.Equal(3, cut.FindAll(".courses-gpx").Count);
        Assert.Equal($"editor/{_loop}", rows[0].QuerySelector(".courses-name a")!.GetAttribute("href"));
    }

    [Fact]
    public void Delete_is_offered_only_for_the_signed_in_owners_courses()
    {
        SignIn();
        var cut = RenderComponent<Courses>();

        var rows = cut.FindAll("tbody tr");
        Assert.Equal("Delete", rows[0].QuerySelector(".courses-delete")!.TextContent);
        Assert.Equal("Delete", rows[1].QuerySelector(".courses-delete")!.TextContent);
        Assert.Empty(rows[2].QuerySelectorAll(".courses-delete"));
    }

    [Fact]
    public void Cancel_leaves_the_course_and_confirm_removes_only_that_course()
    {
        SignIn();
        var cut = RenderComponent<Courses>();

        cut.Find("tbody tr .courses-delete").Click();
        var dialog = cut.Find("[role='dialog']");
        Assert.Equal("Delete course Poudre loop? This can't be undone", dialog.QuerySelector("#courses-confirm-title")!.TextContent);
        Assert.Equal("Cancel", dialog.QuerySelector(".courses-confirm-cancel")!.TextContent);
        Assert.Equal("Delete", dialog.QuerySelector(".courses-confirm-delete")!.TextContent);

        dialog.QuerySelector(".courses-confirm-cancel")!.Click();
        Assert.Empty(cut.FindAll("[role='dialog']"));
        Assert.Equal(3, cut.FindAll("tbody tr").Count);
        Assert.Empty(_api.Deleted);

        cut.Find("tbody tr .courses-delete").Click();
        cut.Find(".courses-confirm-delete").Click();

        Assert.Equal(new[] { _loop }, _api.Deleted);
        Assert.Equal($"/api/courses/{_loop}?login=bud", _api.DeletedPaths[0]);
        Assert.Empty(cut.FindAll("[role='dialog']"));
        var rows = cut.FindAll("tbody tr");
        Assert.Equal(2, rows.Count);
        Assert.DoesNotContain("Poudre loop", cut.Markup);
        Assert.Contains("Spring creek", rows[0].TextContent);
        Assert.Contains("Mystery outing", rows[1].TextContent);
    }

    [Fact]
    public void A_failed_delete_keeps_the_course_and_says_so()
    {
        SignIn();
        _api.RejectDelete = true;
        var cut = RenderComponent<Courses>();

        cut.Find("tbody tr .courses-delete").Click();
        cut.Find(".courses-confirm-delete").Click();

        Assert.Equal(3, cut.FindAll("tbody tr").Count);
        Assert.Contains("Poudre loop", cut.Markup);
        Assert.Equal("That course was not deleted.", cut.Find("#courses-note").TextContent);
        Assert.Empty(cut.FindAll("[role='dialog']"));
    }

    [Fact]
    public void An_empty_list_keeps_the_existing_empty_state()
    {
        SignIn();
        _api.Courses.Clear();

        var cut = RenderComponent<Courses>();

        Assert.Contains("No courses. Record one in the phone app, or start one in the editor.", cut.Markup);
        Assert.Empty(cut.FindAll("table"));
    }

    private void SignIn() =>
        _session.Set(new LoginResponse { Token = "token", User = new UserProfileDto { Login = "bud" } });

    private static string CardRule()
    {
        var css = File.ReadAllText(Path.Combine(RepoRoot(), "src", "GpsTrainer.Web", "wwwroot", "css", "app.css"));
        var start = css.IndexOf(".courses {", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = css.IndexOf('}', start);
        return css[start..end];
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GPS-Trainer.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private sealed class CoursesApi : HttpMessageHandler
    {
        public List<Course> Courses { get; } = [];
        public List<ActivityCode> Activities { get; } = [];
        public HashSet<Guid> WithIntervals { get; } = [];
        public List<Guid> Deleted { get; } = [];
        public List<string> DeletedPaths { get; } = [];
        public bool RejectDelete { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.EndsWith("/activity-codes", StringComparison.OrdinalIgnoreCase))
                return Json(Activities);
            if (path.EndsWith("/intervals", StringComparison.OrdinalIgnoreCase))
            {
                var id = Guid.Parse(path.Split('/', StringSplitOptions.RemoveEmptyEntries)[^2]);
                var body = WithIntervals.Contains(id)
                    ? new[] { new CourseIntervalDto { Name = "1 mi" } }
                    : Array.Empty<CourseIntervalDto>();
                return Json(body);
            }
            if (request.Method == HttpMethod.Delete && path.Contains("/courses/", StringComparison.OrdinalIgnoreCase))
            {
                var idText = path.Split('/', StringSplitOptions.RemoveEmptyEntries)[^1];
                var id = Guid.Parse(idText);
                Deleted.Add(id);
                DeletedPaths.Add(path + request.RequestUri?.Query);
                if (RejectDelete)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
                Courses.RemoveAll(course => course.CourseId == id);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
            if (path.EndsWith("/courses", StringComparison.OrdinalIgnoreCase))
                return Json(Courses);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static Task<HttpResponseMessage> Json<T>(T body) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(body, options: new JsonSerializerOptions(JsonSerializerDefaults.Web))
            });
    }
}
