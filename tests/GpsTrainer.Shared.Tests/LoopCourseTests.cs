namespace GpsTrainer.Shared.Tests;

public class LoopCourseTests
{
    [Fact]
    public async Task Summit_search_pins_the_high_point()
    {
        var points = new List<GeoCoord>();
        for (var i = 0; i <= 40; i++)
        {
            var altitude = i == 20 ? 1.530 : 1.500;
            var at = LoopFixtures.At(i * 200, 0);
            points.Add(new GeoCoord(at.Latitude, at.Longitude, altitude));
        }
        var elapsed = LoopGeometry.ElapsedMeters(points);

        var summits = await LoopCourseBuilder.FindSummitsAsync(points, elapsed);

        var summit = Assert.Single(summits);
        Assert.InRange(summit.ElapsedMeters, 20 * 200 - 400, 20 * 200 + 400);
        Assert.True(summit.AltitudeKm > 1.51);
    }

    [Fact]
    public void Feed_zones_land_about_every_ten_miles_and_use_a_nearby_park()
    {
        var points = new List<GeoCoord>();
        var miles = 12 * Distance.MetersPerStatuteMile;
        for (var i = 0; i <= 24; i++)
            points.Add(LoopFixtures.At(miles * i / 24.0, 0));
        var elapsed = LoopGeometry.ElapsedMeters(points);
        var tenMiles = LoopGeometry.AtDistance(points, elapsed, 10 * Distance.MetersPerStatuteMile);
        var places = new[]
        {
            new NetworkPlace
            {
                Name = "Lee Martinez Park",
                Kind = "park",
                Latitude = tenMiles.Latitude,
                Longitude = tenMiles.Longitude + 0.001
            }
        };

        var feeds = LoopCourseBuilder.PlaceFeeds(points, elapsed, places);
        var feed = Assert.Single(feeds);
        Assert.InRange(feed.ElapsedMeters, 9.5 * Distance.MetersPerStatuteMile, 10.5 * Distance.MetersPerStatuteMile);
        Assert.Equal("Lee Martinez Park", feed.Label);

        var shortElapsed = LoopGeometry.ElapsedMeters([LoopFixtures.At(0, 0), LoopFixtures.At(5000, 0)]);
        Assert.Empty(LoopCourseBuilder.PlaceFeeds([LoopFixtures.At(0, 0), LoopFixtures.At(5000, 0)], shortElapsed, places));
    }

    [Fact]
    public async Task Built_waypoints_are_a_valid_unsaved_course()
    {
        var points = new List<GeoCoord>();
        for (var step = 0; step <= 60; step++)
        {
            var north = step < 30 ? step * 100 : (60 - step) * 100;
            var at = LoopFixtures.At(step * 150, north);
            var altitude = Math.Abs(step - 30) <= 2 ? 1.540 : 1.500;
            points.Add(new GeoCoord(at.Latitude, at.Longitude, altitude));
        }
        var route = new LoopRoute
        {
            Name = "Fixture loop",
            Points = points,
            EdgeIds = [],
            DistanceMeters = LoopGeometry.LengthMeters(points),
            ElevationKnown = true,
            BikeLegalShare = 1
        };

        var waypoints = await LoopCourseBuilder.BuildAsync(route, []);

        Assert.Null(CourseRules.Validate(waypoints));
        Assert.Equal(WaypointType.Start, waypoints[0].WaypointType);
        Assert.Equal(WaypointType.Finish, waypoints[^1].WaypointType);
        Assert.Equal(0, waypoints[0].ElapsedDistanceKm);
        Assert.Contains(waypoints, waypoint => waypoint.WaypointType == WaypointType.Summit);
    }
}
