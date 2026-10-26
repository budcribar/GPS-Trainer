using GpsTrainer;

namespace GpsTrainer.Shared.Tests;

public class EditorMenuElevationTests
{
    [Fact]
    public void The_marker_menu_is_the_2013_type_list_and_the_choice_is_stored_on_the_waypoint()
    {
        Assert.Equal(
            [
                WaypointType.Continue, WaypointType.TurnRight, WaypointType.TurnLeft, WaypointType.TurnAround,
                WaypointType.Finish, WaypointType.FeedZone, WaypointType.Summit, WaypointType.Announcement
            ],
            WaypointMenus.MarkerTypes);
        Assert.Equal("Turn right", WaypointMenus.Label(WaypointType.TurnRight));
        Assert.Equal("Feed zone", WaypointMenus.Label(WaypointType.FeedZone));
        Assert.DoesNotContain(WaypointMenus.MarkerTypes, type => type is WaypointType.BearLeft or WaypointType.BearRight);
        Assert.Equal(
            [WaypointType.FeedZone, WaypointType.Summit, WaypointType.Announcement],
            WaypointMenus.PinMenuTypes);
        Assert.Equal("km", ElevationUnits.SwitchLabel(false));
        Assert.Equal("mi", ElevationUnits.SwitchLabel(true));
        Assert.Equal("1.61 km", ElevationUnits.FormatDistance(1.609344, true));
        Assert.Equal("1.00 mi", ElevationUnits.FormatDistance(EditorText.MilesToKilos(1), false));

        var waypoints = new List<Waypoint>
        {
            new() { TimestampTicks = 1, WaypointType = WaypointType.Start, Latitude = 40.5, Longitude = -105.1 },
            new() { TimestampTicks = 2, WaypointType = WaypointType.Continue, Latitude = 40.6, Longitude = -105.2 }
        };
        Assert.True(WaypointMenus.TryApply(waypoints, 1, "Summit"));
        Assert.Equal(WaypointType.Summit, waypoints[1].WaypointType);
        Assert.Equal(40.6, waypoints[1].Latitude);
        Assert.False(WaypointMenus.TryApply(waypoints, 1, "BearLeft"));
        Assert.Equal(WaypointType.Summit, waypoints[1].WaypointType);
        Assert.False(WaypointMenus.TryApply(waypoints, 4, "Finish"));
    }

    [Fact]
    public void Climb_and_descent_skip_unknown_altitudes_and_the_chart_uses_distance_and_altitude()
    {
        var waypoints = new List<Waypoint>
        {
            new() { Latitude = 40.00, Longitude = -105.00, AltitudeKm = 1.500, WaypointType = WaypointType.Start },
            new() { Latitude = 40.01, Longitude = -105.00, AltitudeKm = Altitude.Unknown.Kilometers, WaypointType = WaypointType.Continue },
            new() { Latitude = 40.02, Longitude = -105.00, AltitudeKm = 1.520, WaypointType = WaypointType.Summit },
            new() { Latitude = 40.03, Longitude = -105.00, AltitudeKm = 1.521, WaypointType = WaypointType.Continue },
            new() { Latitude = 40.04, Longitude = -105.00, AltitudeKm = 1.500, WaypointType = WaypointType.Finish }
        };

        var measured = ElevationProfile.GainLoss(waypoints);
        Assert.Equal(0.020, measured.GainKm, 5);
        Assert.Equal(0.020, measured.LossKm, 5);
        Assert.Equal("20/20 m", ElevationProfile.ClimbDescent(measured.GainKm, measured.LossKm, metric: true));
        Assert.Equal("66/66 ft", ElevationProfile.ClimbDescent(measured.GainKm, measured.LossKm, metric: false));

        var metric = ElevationProfile.Plot(waypoints, metric: true, "Horsetooth");
        Assert.True(metric.HasElevation);
        Assert.Equal("Distance(km)", metric.DistanceAxis);
        Assert.Equal("Altitude(m)", metric.AltitudeAxis);
        Assert.Equal("20/20 m", metric.ClimbDescent);
        Assert.Contains("Horsetooth courtesy of www.gpsTrainer.com", metric.Title);
        Assert.Contains(metric.Marks, mark => mark.Color == "#000000" && mark.Label.StartsWith("Summit", StringComparison.Ordinal));
        Assert.DoesNotContain("-10000", metric.Line, StringComparison.Ordinal);

        var imperial = ElevationProfile.Plot(waypoints, metric: false, "Horsetooth");
        Assert.Equal("Distance(mi)", imperial.DistanceAxis);
        Assert.Equal("Altitude(ft)", imperial.AltitudeAxis);
        Assert.Equal("66/66 ft", imperial.ClimbDescent);
    }

    [Fact]
    public void An_open_route_drops_a_trailing_start_unless_the_click_is_the_start()
    {
        var points = new List<RoutePointDto>
        {
            new() { Latitude = 40.59, Longitude = -105.08 },
            new() { Latitude = 40.60, Longitude = -105.07 },
            new() { Latitude = 40.5853, Longitude = -105.0844 }
        };
        var open = RoutePath.WithoutImplicitReturn(40.5853, -105.0844, 40.60, -105.07, points);
        Assert.Equal(2, open.Count);
        Assert.Equal(40.60, open[^1].Latitude, 5);

        var closed = RoutePath.WithoutImplicitReturn(40.5853, -105.0844, 40.5853, -105.0844, points);
        Assert.Equal(3, closed.Count);
        Assert.Equal(40.5853, closed[^1].Latitude, 5);
    }
}
