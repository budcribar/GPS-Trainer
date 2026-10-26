namespace GpsTrainer.Shared.Tests;

internal static class LoopFixtures
{
    public const double OriginLatitude = 40.55;
    public const double OriginLongitude = -105.08;

    public static GeoCoord At(double eastMeters, double northMeters)
    {
        var latitude = OriginLatitude + northMeters / 111_320.0;
        var longitude = OriginLongitude + eastMeters / (111_320.0 * Math.Cos(OriginLatitude * Math.PI / 180.0));
        return GeoCoord.At(latitude, longitude);
    }

    public static NetworkSegment Line(string name, LoopFacility facility, LoopSurface surface, params (double East, double North)[] points) =>
        new()
        {
            Name = name,
            Facility = facility,
            Surface = surface,
            Source = "fixture",
            Points = points.Select(point => At(point.East, point.North)).ToList()
        };

    public static IEnumerable<NetworkSegment> Square(string name, LoopFacility facility, double east, double north, double side)
    {
        (double East, double North) a = (east, north);
        var b = (east + side, north);
        var c = (east + side, north + side);
        var d = (east, north + side);
        var surface = LoopSurface.Paved;
        yield return Line(name, facility, surface, a, b);
        yield return Line(name, facility, surface, b, c);
        yield return Line(name, facility, surface, c, d);
        yield return Line(name, facility, surface, d, a);
    }

    public static LoopGraphOptions Tight { get; } = new()
    {
        SnapMeters = 40,
        LinkMeters = 90,
        MaxLinksPerNode = 2
    };

    public static bool Reaches(LoopGraph graph, int start, int goal)
    {
        var seen = new HashSet<int> { start };
        var queue = new Queue<int>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node == goal)
                return true;
            foreach (var edgeId in graph.From(node))
            {
                var next = graph.Edges[edgeId].Other(node);
                if (seen.Add(next))
                    queue.Enqueue(next);
            }
        }
        return false;
    }
}
