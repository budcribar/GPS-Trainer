using GpsTrainer;

namespace GpsTrainer.Server.Loops;

public sealed class FortCollinsNetworkCatalog
{
    private readonly Lazy<LoopNetwork> _network;
    private readonly Lazy<LoopGraph> _graph;

    public FortCollinsNetworkCatalog(IWebHostEnvironment environment)
    {
        var path = Resolve(environment);
        _network = new Lazy<LoopNetwork>(() => LoopNetworkFile.LoadFile(path));
        _graph = new Lazy<LoopGraph>(() => LoopGraphBuilder.Build(_network.Value.Segments));
    }

    public LoopNetwork Network => _network.Value;

    public LoopGraph Graph => _graph.Value;

    public static string Resolve(IWebHostEnvironment environment)
    {
        var candidates = new[]
        {
            System.IO.Path.Combine(environment.ContentRootPath, "Loops", "fort-collins-network.json"),
            System.IO.Path.Combine(AppContext.BaseDirectory, "Loops", "fort-collins-network.json")
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }
}
