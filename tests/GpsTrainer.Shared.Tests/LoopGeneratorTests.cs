namespace GpsTrainer.Shared.Tests;

public class LoopGeneratorTests
{
    [Fact]
    public void A_square_trail_becomes_a_loop_near_the_target_distance()
    {
        var graph = LoopGraphBuilder.Build(LoopFixtures.Square("Poudre River Trail", LoopFacility.Trail, 0, 0, 2000), LoopFixtures.Tight);
        var start = LoopFixtures.At(0, 0);
        var routes = LoopGenerator.Generate(graph, new LoopRequest(start.Latitude, start.Longitude, 8000, ClimbPreference.Flat, LoopRouteType.TrailFirst));

        var loop = Assert.Single(routes);
        Assert.InRange(loop.DistanceMeters, 7600, 8400);
        Assert.True(loop.BacktrackShare < 0.05);
        Assert.Equal(1, loop.BikeLegalShare, 3);
        Assert.Contains("Poudre River Trail", loop.Name);
        Assert.True(loop.CorridorShare > 0.9);
    }

    [Fact]
    public void Trail_first_prefers_the_trail_square_and_bike_lanes_prefer_the_lane_square()
    {
        var segments = LoopFixtures.Square("Poudre River Trail", LoopFacility.Trail, 0, 0, 2000)
            .Concat(LoopFixtures.Square("College Avenue", LoopFacility.Lane, -2000, -2000, 2000))
            .ToList();
        var graph = LoopGraphBuilder.Build(segments, LoopFixtures.Tight);
        var start = LoopFixtures.At(0, 0);

        var trails = LoopGenerator.Generate(graph, new LoopRequest(start.Latitude, start.Longitude, 8000, ClimbPreference.Flat, LoopRouteType.TrailFirst));
        var lanes = LoopGenerator.Generate(graph, new LoopRequest(start.Latitude, start.Longitude, 8000, ClimbPreference.Flat, LoopRouteType.BikeLanes));

        Assert.Contains("Poudre", trails[0].Name);
        Assert.True(trails[0].TrailShare > lanes[0].TrailShare);
        Assert.Contains("College", lanes[0].Name);
        Assert.True(lanes[0].LaneShare > trails[0].LaneShare);
    }

    [Fact]
    public void The_closest_distance_ranks_above_a_much_shorter_loop()
    {
        var segments = LoopFixtures.Square("Short", LoopFacility.Trail, 0, 0, 1000)
            .Concat(LoopFixtures.Square("Target", LoopFacility.Trail, 0, 0, 2000))
            .ToList();
        var graph = LoopGraphBuilder.Build(segments, new LoopGraphOptions { SnapMeters = 40, LinkMeters = 0 });
        var start = LoopFixtures.At(0, 0);
        var routes = LoopGenerator.Generate(graph, new LoopRequest(start.Latitude, start.Longitude, 8000, ClimbPreference.Flat, LoopRouteType.TrailFirst), take: 3);

        Assert.NotEmpty(routes);
        Assert.InRange(routes[0].DistanceMeters, 7600, 8400);
        Assert.True(routes[0].Score >= routes[^1].Score);
    }

    [Fact]
    public void A_real_loop_outranks_an_out_and_back()
    {
        var segments = LoopFixtures.Square("Spring Creek Trail", LoopFacility.Trail, 0, 0, 2000)
            .Append(LoopFixtures.Line("Spur", LoopFacility.Trail, LoopSurface.Paved, (2000, 2000), (2000, 6000)))
            .ToList();
        var graph = LoopGraphBuilder.Build(segments, new LoopGraphOptions { SnapMeters = 40, LinkMeters = 0 });
        var start = LoopFixtures.At(0, 0);
        var routes = LoopGenerator.Generate(graph, new LoopRequest(start.Latitude, start.Longitude, 8000, ClimbPreference.Moderate, LoopRouteType.TrailFirst));

        Assert.NotEmpty(routes);
        Assert.True(routes[0].BacktrackShare < 0.2);
        Assert.Contains("Spring Creek", routes[0].Name);
    }

    [Fact]
    public void Elevation_samples_mark_the_route_as_known()
    {
        var graph = LoopGraphBuilder.Build(LoopFixtures.Square("Mason Trail", LoopFacility.Trail, 0, 0, 1500), LoopFixtures.Tight);
        var start = LoopFixtures.At(0, 0);
        var routes = LoopGenerator.Generate(
            graph,
            new LoopRequest(start.Latitude, start.Longitude, 6000, ClimbPreference.Hilly, LoopRouteType.TrailFirst),
            (latitude, _) => 1500 + (latitude - LoopFixtures.OriginLatitude) * 111_320 * 0.04);

        var loop = Assert.Single(routes);
        Assert.True(loop.ElevationKnown);
        Assert.True(loop.ClimbingMeters > 10);
    }

    [Fact]
    public void A_start_far_from_the_network_returns_no_loop()
    {
        var graph = LoopGraphBuilder.Build(LoopFixtures.Square("Poudre River Trail", LoopFacility.Trail, 0, 0, 500), LoopFixtures.Tight);
        var far = LoopFixtures.At(5000, 5000);
        var routes = LoopGenerator.Generate(graph, new LoopRequest(far.Latitude, far.Longitude, 2000, ClimbPreference.Flat, LoopRouteType.TrailFirst));

        Assert.Empty(routes);
    }
}
