namespace GpsTrainer.Shared.Tests;

public class FortCollinsNetworkTests
{
    [Fact]
    public void Checked_in_network_builds_trail_and_bike_lane_loops()
    {
        var path = FindNetwork();
        var network = LoopNetworkFile.LoadFile(path);
        Assert.NotEmpty(network.Segments);
        Assert.Contains(network.Segments, segment => segment.Source == "cotrex" && segment.Facility == LoopFacility.Trail);
        Assert.Contains(network.Segments, segment => segment.Source == "osm" && segment.Facility == LoopFacility.Lane);

        var graph = LoopGraphBuilder.Build(network.Segments);
        Assert.True(graph.Edges.Count > 100);

        var downtown = new GeoCoord(MapDefaults.FortCollinsLatitude, MapDefaults.FortCollinsLongitude, Altitude.Unknown.Kilometers);
        var trails = LoopGenerator.Generate(graph, new LoopRequest(downtown.Latitude, downtown.Longitude, 12 * Distance.MetersPerStatuteMile, ClimbPreference.Flat, LoopRouteType.TrailFirst));
        var lanes = LoopGenerator.Generate(graph, new LoopRequest(downtown.Latitude, downtown.Longitude, 10 * Distance.MetersPerStatuteMile, ClimbPreference.Flat, LoopRouteType.BikeLanes));

        Assert.NotEmpty(trails);
        Assert.NotEmpty(lanes);
        Assert.True(trails[0].TrailShare >= 0.35, Describe(trails));
        Assert.Contains(trails, route => route.CorridorShare >= 0.25 || route.Name.Contains("Poudre", StringComparison.OrdinalIgnoreCase) || route.Name.Contains("Spring Creek", StringComparison.OrdinalIgnoreCase));
        Assert.True(lanes[0].LaneShare >= 0.35, Describe(lanes));
        Assert.True(trails[0].BacktrackShare < 0.45, Describe(trails));
        Assert.InRange(trails[0].DistanceMeters, 8 * Distance.MetersPerStatuteMile, 16 * Distance.MetersPerStatuteMile);
        Assert.InRange(lanes[0].DistanceMeters, 6 * Distance.MetersPerStatuteMile, 14 * Distance.MetersPerStatuteMile);
    }

    private static string Describe(IReadOnlyList<LoopRoute> routes) =>
        string.Join(" | ", routes.Select(route =>
            $"{route.Name}: {route.DistanceMeters / Distance.MetersPerStatuteMile:0.0} mi trail {route.TrailShare:0%} lane {route.LaneShare:0%} corridor {route.CorridorShare:0%} back {route.BacktrackShare:0%}"));

    private static string FindNetwork()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "GpsTrainer.Server", "Loops", "fort-collins-network.json");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("fort-collins-network.json");
    }
}
