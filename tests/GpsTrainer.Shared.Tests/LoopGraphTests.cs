namespace GpsTrainer.Shared.Tests;

public class LoopGraphTests
{
    [Fact]
    public void Endpoints_within_snap_distance_become_one_intersection()
    {
        var segments = new[]
        {
            LoopFixtures.Line("Spring Creek Trail", LoopFacility.Trail, LoopSurface.Paved, (0, 0), (500, 0)),
            LoopFixtures.Line("Spring Creek Trail", LoopFacility.Trail, LoopSurface.Paved, (520, 0), (1000, 0))
        };

        var graph = LoopGraphBuilder.Build(segments, new LoopGraphOptions { SnapMeters = 40, LinkMeters = 0 });
        var start = graph.Nearest(LoopFixtures.At(0, 0).Latitude, LoopFixtures.At(0, 0).Longitude, 50);
        var end = graph.Nearest(LoopFixtures.At(1000, 0).Latitude, LoopFixtures.At(1000, 0).Longitude, 50);

        Assert.Equal(3, graph.Nodes.Count);
        Assert.True(LoopFixtures.Reaches(graph, start, end));
    }

    [Fact]
    public void Endpoints_far_apart_stay_disconnected_without_a_link()
    {
        var segments = new[]
        {
            LoopFixtures.Line("A", LoopFacility.Trail, LoopSurface.Paved, (0, 0), (400, 0)),
            LoopFixtures.Line("B", LoopFacility.Trail, LoopSurface.Paved, (700, 0), (1100, 0))
        };

        var graph = LoopGraphBuilder.Build(segments, new LoopGraphOptions { SnapMeters = 40, LinkMeters = 0 });
        var start = graph.Nearest(LoopFixtures.At(0, 0).Latitude, LoopFixtures.At(0, 0).Longitude, 50);
        var end = graph.Nearest(LoopFixtures.At(1100, 0).Latitude, LoopFixtures.At(1100, 0).Longitude, 50);

        Assert.False(LoopFixtures.Reaches(graph, start, end));
    }

    [Fact]
    public void A_line_passing_a_nearby_node_is_split_and_connected()
    {
        var segments = new[]
        {
            LoopFixtures.Line("Poudre River Trail", LoopFacility.Trail, LoopSurface.Paved, (0, 0), (1000, 0)),
            LoopFixtures.Line("College Avenue", LoopFacility.Lane, LoopSurface.Paved, (500, 20), (500, 400))
        };

        var graph = LoopGraphBuilder.Build(segments, new LoopGraphOptions { SnapMeters = 40, LinkMeters = 0 });
        var trailEnd = graph.Nearest(LoopFixtures.At(0, 0).Latitude, LoopFixtures.At(0, 0).Longitude, 50);
        var laneEnd = graph.Nearest(LoopFixtures.At(500, 400).Latitude, LoopFixtures.At(500, 400).Longitude, 50);

        Assert.True(LoopFixtures.Reaches(graph, trailEnd, laneEnd));
        Assert.Contains(graph.Edges, edge => edge.Facility == LoopFacility.Trail);
        Assert.Contains(graph.Edges, edge => edge.Facility == LoopFacility.Lane);
    }

    [Fact]
    public void Duplicate_edges_between_the_same_corners_keep_the_named_paved_trail()
    {
        var segments = new[]
        {
            LoopFixtures.Line("", LoopFacility.Lane, LoopSurface.Paved, (0, 0), (400, 0)),
            LoopFixtures.Line("Poudre River Trail", LoopFacility.Trail, LoopSurface.Paved, (0, 0), (400, 0))
        };

        var graph = LoopGraphBuilder.Build(segments, new LoopGraphOptions { SnapMeters = 40, LinkMeters = 0 });

        Assert.Single(graph.Edges);
        Assert.Equal(LoopFacility.Trail, graph.Edges[0].Facility);
        Assert.Equal("Poudre River Trail", graph.Edges[0].Name);
    }
}
