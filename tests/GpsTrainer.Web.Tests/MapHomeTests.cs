using System.Net;
using System.Net.Http.Json;
using GpsTrainer.Web.Maps;
using GpsTrainer.Web.Pages;
using GpsTrainer.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GpsTrainer.Web.Tests;

public sealed class MapHomeTests : TestContext
{
    private static readonly Guid SavedCourse = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
    private readonly HomeHandler _api = new();
    private readonly FakeCourseMap _map = new();
    private readonly Session _session = new();

    public MapHomeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_session);
        Services.AddSingleton(new HttpClient(_api) { BaseAddress = new Uri("http://localhost/") });
        Services.AddSingleton<GpsApi>();
        Services.AddSingleton<ICourseMap>(_map);
    }

    [Fact]
    public void A_signed_in_editor_with_a_saved_home_opens_there()
    {
        SignIn(37.7749, -122.4194);

        RenderComponent<CourseEditor>();

        Assert.Equal(37.7749, _map.LastFrame!.Center.Latitude, 5);
        Assert.Equal(-122.4194, _map.LastFrame.Center.Longitude, 5);
        Assert.Empty(_map.LastFrame.Pins);
    }

    [Fact]
    public void A_signed_in_editor_without_a_home_stays_at_Fort_Collins()
    {
        SignIn(null, null);

        RenderComponent<CourseEditor>();

        Assert.Equal(MapDefaults.FortCollinsLatitude, _map.LastFrame!.Center.Latitude, 5);
        Assert.Equal(MapDefaults.FortCollinsLongitude, _map.LastFrame.Center.Longitude, 5);
    }

    [Fact]
    public void A_signed_out_editor_stays_at_Fort_Collins()
    {
        RenderComponent<CourseEditor>();

        Assert.Equal(MapDefaults.FortCollinsLatitude, _map.LastFrame!.Center.Latitude, 5);
        Assert.Equal(MapDefaults.FortCollinsLongitude, _map.LastFrame.Center.Longitude, 5);
        Assert.Empty(_map.LastFrame.Pins);
    }

    [Fact]
    public void An_existing_course_opens_on_that_course_not_the_saved_home()
    {
        SignIn(37.7749, -122.4194);
        _api.CourseLatitude = 41.25;
        _api.CourseLongitude = -107.5;

        RenderComponent<CourseEditor>(parameters => parameters.Add(editor => editor.CourseId, SavedCourse));

        Assert.Equal(41.25, _map.LastFrame!.Pins[0].Latitude, 5);
        Assert.Equal(-107.5, _map.LastFrame.Pins[0].Longitude, 5);
        Assert.Equal(41.25, _map.LastFrame.Center.Latitude, 5);
        Assert.Equal(-107.5, _map.LastFrame.Center.Longitude, 5);
        Assert.DoesNotContain(_map.Views, view => Math.Abs(view.Latitude - 37.7749) < 0.01);
    }

    [Fact]
    public void A_new_course_after_an_existing_one_returns_to_the_saved_home()
    {
        SignIn(37.7749, -122.4194);
        _api.CourseLatitude = 41.25;
        _api.CourseLongitude = -107.5;
        var cut = RenderComponent<CourseEditor>(parameters => parameters.Add(editor => editor.CourseId, SavedCourse));

        cut.SetParametersAndRender(parameters => parameters.Add(editor => editor.CourseId, Guid.Empty));

        Assert.Empty(_map.LastFrame!.Pins);
        Assert.Equal(37.7749, _map.LastFrame.Center.Latitude, 5);
        Assert.Contains(_map.Views, view => Math.Abs(view.Latitude - 37.7749) < 0.0001 && Math.Abs(view.Longitude - (-122.4194)) < 0.0001);
    }

    [Fact]
    public void The_route_generator_opens_on_the_saved_home()
    {
        SignIn(37.7749, -122.4194);

        var cut = RenderComponent<GenerateRoute>();

        var mount = JSInterop.Invocations["gpsTrainerRouteGenerator.mount"].Single();
        Assert.Equal(37.7749, Convert.ToDouble(mount.Arguments[1]), 5);
        Assert.Equal(-122.4194, Convert.ToDouble(mount.Arguments[2]), 5);
        Assert.Contains("saved map location", cut.Markup);
    }

    [Fact]
    public void The_route_generator_without_a_home_stays_at_Fort_Collins()
    {
        var cut = RenderComponent<GenerateRoute>();

        var mount = JSInterop.Invocations["gpsTrainerRouteGenerator.mount"].Single();
        Assert.Equal(MapDefaults.FortCollinsLatitude, Convert.ToDouble(mount.Arguments[1]), 5);
        Assert.Equal(MapDefaults.FortCollinsLongitude, Convert.ToDouble(mount.Arguments[2]), 5);
        Assert.Contains("downtown Fort Collins", cut.Markup);
    }

    private void SignIn(double? latitude, double? longitude)
    {
        _api.HomeLatitude = latitude;
        _api.HomeLongitude = longitude;
        _session.Set(new LoginResponse
        {
            Token = "token",
            User = new UserProfileDto { Login = "bud", Latitude = latitude, Longitude = longitude }
        });
    }

    private sealed class HomeHandler : HttpMessageHandler
    {
        public double? HomeLatitude { get; set; }
        public double? HomeLongitude { get; set; }
        public double? CourseLatitude { get; set; }
        public double? CourseLongitude { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.Contains("activity-codes", StringComparison.Ordinal))
                return Json(new[] { new ActivityCode { ActKey = 3, ActValue = "Cycling" } });
            if (path.Contains("/waypoints", StringComparison.Ordinal))
            {
                Waypoint[] points = CourseLatitude is double latitude && CourseLongitude is double longitude
                    ? [new Waypoint { CourseId = SavedCourse, Latitude = latitude, Longitude = longitude, WaypointType = WaypointType.Start, TimestampTicks = 1 }]
                    : [];
                return Json(points);
            }
            if (path.Contains("/users/", StringComparison.Ordinal))
                return Json(new UserProfileDto { Login = "bud", Latitude = HomeLatitude, Longitude = HomeLongitude });
            if (path.Contains("/courses/", StringComparison.Ordinal))
                return Json(new Course { CourseId = SavedCourse, Name = "Saved loop", Login = "bud" });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static Task<HttpResponseMessage> Json<T>(T body) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) });
    }
}
