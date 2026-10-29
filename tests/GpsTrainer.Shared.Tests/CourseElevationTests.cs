namespace GpsTrainer.Shared.Tests;

public class CourseElevationTests
{
    [Fact]
    public void Unknown_altitudes_are_filled_and_known_ones_stay()
    {
        var start = new Waypoint
        {
            Latitude = 40.01,
            Longitude = -105.27,
            WaypointType = WaypointType.Start,
            AltitudeKm = Altitude.Unknown.Kilometers
        };
        var sea = new Waypoint
        {
            Latitude = 40.02,
            Longitude = -105.27,
            WaypointType = WaypointType.Continue,
            AltitudeKm = 0
        };
        var finish = new Waypoint
        {
            Latitude = 40.03,
            Longitude = -105.27,
            WaypointType = WaypointType.Finish,
            AltitudeKm = Altitude.Unknown.Kilometers
        };
        var interval = new Waypoint
        {
            Latitude = 40.015,
            Longitude = -105.27,
            WaypointType = WaypointType.StartInterval,
            AltitudeKm = Altitude.Unknown.Kilometers
        };
        var points = new List<Waypoint> { start, sea, interval, finish };

        Assert.True(CourseElevation.NeedsLookup(points));
        Assert.Equal(2, CourseElevation.MissingCount(points));
        Assert.Equal(2, CourseElevation.Apply(points, [1.525, 1.610]));

        Assert.Equal(1.525, start.AltitudeKm);
        Assert.Equal(0, sea.AltitudeKm);
        Assert.Equal(Altitude.Unknown.Kilometers, interval.AltitudeKm);
        Assert.Equal(1.610, finish.AltitudeKm);
        Assert.False(CourseElevation.NeedsLookup(points));
        var climb = ElevationProfile.GainLoss(points);
        Assert.True(climb.GainKm > 0);
    }

    [Fact]
    public void A_failed_lookup_does_not_invent_an_altitude()
    {
        var point = new Waypoint
        {
            WaypointType = WaypointType.Start,
            AltitudeKm = Altitude.Unknown.Kilometers
        };
        Assert.Equal(0, CourseElevation.Apply([point], [null, Altitude.Unknown.Kilometers]));
        Assert.Equal(Altitude.Unknown.Kilometers, point.AltitudeKm);
    }
}
