namespace GpsTrainer.Shared.Tests;

public class FortCollinsExampleExportTests
{
    [Fact]
    public async Task Writes_gpx_when_GPSTRAINER_EXPORT_LOOPS_is_set()
    {
        var directory = Environment.GetEnvironmentVariable("GPSTRAINER_EXPORT_LOOPS");
        if (string.IsNullOrWhiteSpace(directory))
            return;

        Directory.CreateDirectory(directory);
        var network = LoopNetworkFile.LoadFile(NetworkPath());
        var graph = LoopGraphBuilder.Build(network.Segments);
        var requests = new (string File, LoopRequest Request)[]
        {
            ("poudre-spring-10mi", new LoopRequest(40.5925, -105.0768, Miles(10), ClimbPreference.Flat, LoopRouteType.TrailFirst)),
            ("spring-creek-12mi", new LoopRequest(40.5550, -105.0800, Miles(12), ClimbPreference.Flat, LoopRouteType.TrailFirst)),
            ("poudre-15mi", new LoopRequest(40.5910, -105.1100, Miles(15), ClimbPreference.Moderate, LoopRouteType.TrailFirst)),
            ("mason-power-18mi", new LoopRequest(40.5702, -105.0748, Miles(18), ClimbPreference.Flat, LoopRouteType.TrailFirst)),
            ("lanes-8mi", new LoopRequest(MapDefaults.FortCollinsLatitude, MapDefaults.FortCollinsLongitude, Miles(8), ClimbPreference.Flat, LoopRouteType.BikeLanes)),
            ("lanes-14mi", new LoopRequest(40.5750, -105.0850, Miles(14), ClimbPreference.Moderate, LoopRouteType.BikeLanes))
        };

        var summary = new List<string>();
        var mapRoutes = new List<string>();
        foreach (var (file, request) in requests)
        {
            var routes = LoopGenerator.Generate(graph, request, take: 1);
            if (routes.Count == 0)
            {
                summary.Add(file + "\tNO LOOP");
                continue;
            }
            var route = routes[0];
            var waypoints = await LoopCourseBuilder.BuildAsync(route, network.Places);
            var gpx = GpxCourse.Export(route.Name, waypoints, []);
            await File.WriteAllTextAsync(Path.Combine(directory, file + ".gpx"), gpx);
            var miles = route.DistanceMeters / Distance.MetersPerStatuteMile;
            summary.Add(string.Join('\t',
                file,
                route.Name,
                miles.ToString("0.00"),
                route.ClimbingMeters.ToString("0"),
                (route.BikeLegalShare * 100).ToString("0"),
                (route.TrailShare * 100).ToString("0"),
                (route.LaneShare * 100).ToString("0"),
                (route.BacktrackShare * 100).ToString("0"),
                route.ElevationKnown ? "yes" : "no"));
            var line = string.Join(",", route.Points.Select(point =>
                $"[{point.Latitude.ToString("0.00000", System.Globalization.CultureInfo.InvariantCulture)},{point.Longitude.ToString("0.00000", System.Globalization.CultureInfo.InvariantCulture)}]"));
            mapRoutes.Add($$"""{"file":"{{file}}","name":{{System.Text.Json.JsonSerializer.Serialize(route.Name)}},"color":"{{Color(file)}}","points":[{{line}}]}""");
        }

        await File.WriteAllLinesAsync(Path.Combine(directory, "summary.tsv"), summary);
        await File.WriteAllTextAsync(Path.Combine(directory, "routes.json"), "[\n" + string.Join(",\n", mapRoutes) + "\n]\n");
        Assert.True(summary.Count(line => !line.Contains("NO LOOP", StringComparison.Ordinal)) >= 5);
        Assert.Contains(summary, line => line.Contains("Poudre", StringComparison.OrdinalIgnoreCase) || line.Contains("Spring Creek", StringComparison.OrdinalIgnoreCase));
    }

    private static double Miles(double miles) => miles * Distance.MetersPerStatuteMile;

    private static string Color(string file) => file.StartsWith("lanes", StringComparison.Ordinal) ? "#8b1e3f" : "#1d4e89";

    private static string NetworkPath()
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
