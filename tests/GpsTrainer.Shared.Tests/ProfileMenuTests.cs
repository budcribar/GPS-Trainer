using GpsTrainer;

namespace GpsTrainer.Shared.Tests;

public class ProfileMenuTests
{
    [Fact]
    public void Hover_reports_distance_and_altitude_in_the_2013_units()
    {
        var plot = ElevationProfile.Plot(Course(), metric: false, "Horsetooth");
        var start = ElevationProfile.Hover(plot, 68, metric: false);
        var middle = ElevationProfile.Hover(plot, 405, metric: false);

        Assert.EndsWith("ft", start);
        Assert.Contains("mi,", start);
        Assert.Contains("mi,", middle);
        Assert.NotEqual(start, middle);
    }

    [Fact]
    public void A_profile_click_inserts_the_marker_at_that_distance()
    {
        var sketch = new CourseSketch();
        sketch.Replace(Course());
        var edit = sketch.InsertAlongRoute(WaypointType.FeedZone, 1.1);

        Assert.True(edit.Ok);
        var feed = Assert.Single(sketch.Waypoints, point => point.WaypointType == WaypointType.FeedZone);
        Assert.InRange(feed.Latitude, 40.005, 40.015);
        Assert.Equal(40.00, sketch.Waypoints[0].Latitude, 5);
    }

    [Fact]
    public void A_profile_interval_starts_at_the_clicked_distance()
    {
        var sketch = new CourseSketch();
        sketch.Replace(Course());
        CourseEdits.StampRoute(sketch.Waypoints);
        var edit = sketch.PlaceIntervalAt(0.2, "100m", Guid.NewGuid());

        Assert.True(edit.Ok);
        var start = Assert.Single(sketch.Waypoints, point => point.WaypointType == WaypointType.StartInterval);
        var finish = Assert.Single(sketch.Waypoints, point => point.WaypointType == WaypointType.FinishInterval);
        Assert.InRange(start.ElapsedDistanceKm, 0.15, 0.25);
        Assert.InRange(finish.ElapsedDistanceKm - start.ElapsedDistanceKm, 0.09, 0.11);
    }

    private static List<Waypoint> Course() =>
    [
        new() { Latitude = 40.00, Longitude = -105.00, AltitudeKm = 1.600, WaypointType = WaypointType.Start, TimestampTicks = 1 },
        new() { Latitude = 40.01, Longitude = -105.00, AltitudeKm = 1.700, WaypointType = WaypointType.Continue, TimestampTicks = 2 },
        new() { Latitude = 40.02, Longitude = -105.00, AltitudeKm = 1.500, WaypointType = WaypointType.Finish, TimestampTicks = 3 }
    ];
}
