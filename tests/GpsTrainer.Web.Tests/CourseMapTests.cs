using GpsTrainer.Web.Maps;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace GpsTrainer.Web.Tests;

public sealed class CourseMapTests
{
    [Fact]
    public async Task The_default_provider_is_leaflet_and_an_unknown_name_is_refused()
    {
        await using var leaflet = Provider(new Dictionary<string, string?>());
        Assert.IsType<LeafletCourseMap>(leaflet.GetRequiredService<ICourseMap>());

        await using var named = Provider(new Dictionary<string, string?> { ["Map:Provider"] = "leaflet" });
        Assert.IsType<LeafletCourseMap>(named.GetRequiredService<ICourseMap>());

        await using var google = Provider(new Dictionary<string, string?> { ["Map:Provider"] = "Google" });
        var error = Assert.Throws<InvalidOperationException>(() => google.GetRequiredService<ICourseMap>());
        Assert.Contains("GoogleCourseMap", error.Message, StringComparison.Ordinal);
        Assert.Contains("CourseEditor.razor", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Leaflet_mount_update_and_callbacks_use_the_existing_script()
    {
        var js = new RecordingJs();
        var map = new LeafletCourseMap(js);
        GpsTrainer.Web.Maps.GeoCoord? clicked = null;
        var dragged = false;
        var closed = false;
        map.MapClicked += point => { clicked = point; return Task.CompletedTask; };
        map.PinDragged += (_, _) => { dragged = true; return Task.CompletedTask; };
        map.LoopCloseRequested += () => { closed = true; return Task.CompletedTask; };

        var frame = new CourseMapFrame
        {
            Center = new GpsTrainer.Web.Maps.GeoCoord(40.5, -105.1),
            Pins = [new CoursePin(0, 40.5, -105.1, WaypointType.Start, null)],
            Menus = new CourseMapMenus
            {
                StandardFinish = "1 mi FINISH",
                NearStartMeters = 200,
                SamePointMeters = 15,
                IntervalDistances = ["100m"],
                PinTypes = [new MenuChoice("FeedZone", "Feed zone")],
                Connect = [new MenuChoice("Finish", "Finish")],
                MapMenuLast = [new MenuRow("FINISH", "insert", "Finish")],
                MapMenuEarlier = [new MenuRow("ZOOM", "zoom", null)]
            }
        };

        await map.MountAsync("#course-map", frame);
        await map.UpdateAsync(frame);
        await map.AddWaypoint(40.6, -105.2);
        await map.MoveWaypoint(0, 40.7, -105.3);
        await map.CloseLoop();
        await map.UnmountAsync();

        Assert.Equal("gpsTrainerCourseEditor.mount", js.Calls[0].Name);
        Assert.Equal("#course-map", js.Calls[0].Args[0]);
        Assert.Equal(6, js.Calls[0].Args.Length);
        var mounted = System.Text.Json.JsonSerializer.Serialize(js.Calls[0].Args[1]);
        Assert.Contains("Start", mounted, StringComparison.Ordinal);
        var types = System.Text.Json.JsonSerializer.Serialize(js.Calls[0].Args[4]);
        Assert.Contains("Feed zone", types, StringComparison.Ordinal);
        var options = System.Text.Json.JsonSerializer.Serialize(js.Calls[0].Args[5]);
        Assert.Contains("1 mi FINISH", options, StringComparison.Ordinal);
        Assert.Contains("100m", options, StringComparison.Ordinal);
        Assert.Equal("gpsTrainerCourseEditor.setWaypoints", js.Calls[1].Name);
        Assert.Equal(40.6, clicked!.Value.Latitude, 5);
        Assert.True(dragged);
        Assert.True(closed);
        Assert.Equal("gpsTrainerCourseEditor.unmount", js.Calls[^1].Name);
    }

    [Fact]
    public async Task The_fake_map_reports_a_right_click_and_a_pin_click()
    {
        var map = new FakeCourseMap();
        GpsTrainer.Web.Maps.GeoCoord? right = null;
        int? pin = null;
        map.MapRightClicked += point => { right = point; return Task.CompletedTask; };
        map.PinClicked += index => { pin = index; return Task.CompletedTask; };

        await map.SimulateRightClick(40.1, -105.2);
        await map.SimulatePinClick(3);

        Assert.Equal(40.1, right!.Value.Latitude, 5);
        Assert.Equal(3, pin);
    }

    private static ServiceProvider Provider(Dictionary<string, string?> settings)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<IJSRuntime>(new RecordingJs());
        services.AddScoped<ICourseMap>(CourseMaps.Create);
        return services.BuildServiceProvider();
    }

    private sealed class RecordingJs : IJSRuntime
    {
        public List<(string Name, object?[] Args)> Calls { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add((identifier, args ?? []));
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }
}
