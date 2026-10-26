namespace GpsTrainer;

/// <summary>
/// Joins trail and bike-lane lines into one undirected graph. Endpoints within
/// <see cref="LoopGraphOptions.SnapMeters"/> become one intersection, and a line
/// that passes that close to another intersection is split there. Short links
/// close the gaps between a trail end and the nearest bike lane.
/// </summary>
public static class LoopGraphBuilder
{
    public static LoopGraph Build(IEnumerable<NetworkSegment> segments, LoopGraphOptions? options = null)
    {
        options ??= LoopGraphOptions.Default;
        var lines = new List<RawLine>();
        foreach (var segment in segments)
        {
            var points = Clean(segment.Points);
            if (points.Count < 2)
                continue;
            lines.Add(new RawLine(points, segment));
        }

        var endpoints = new List<GeoCoord>(lines.Count * 2);
        foreach (var line in lines)
        {
            endpoints.Add(line.Points[0]);
            endpoints.Add(line.Points[^1]);
        }

        var parent = new int[endpoints.Count];
        for (var i = 0; i < parent.Length; i++)
            parent[i] = i;
        var grid = new Dictionary<(int, int), List<int>>();
        for (var i = 0; i < endpoints.Count; i++)
            Add(grid, endpoints[i], i, options.SnapMeters);
        for (var i = 0; i < endpoints.Count; i++)
        {
            foreach (var other in Near(grid, endpoints[i], options.SnapMeters))
            {
                if (other <= i)
                    continue;
                if (GeoMath.DistanceMeters(endpoints[i].Latitude, endpoints[i].Longitude, endpoints[other].Latitude, endpoints[other].Longitude) <= options.SnapMeters)
                    Union(parent, i, other);
            }
        }

        var rootToNode = new Dictionary<int, int>();
        var members = new List<List<GeoCoord>>();
        var endpointNode = new int[endpoints.Count];
        for (var i = 0; i < endpoints.Count; i++)
        {
            var root = Find(parent, i);
            if (!rootToNode.TryGetValue(root, out var id))
            {
                id = members.Count;
                rootToNode[root] = id;
                members.Add([]);
            }
            endpointNode[i] = id;
            members[id].Add(endpoints[i]);
        }

        var nodes = new List<LoopNode>(members.Count);
        for (var i = 0; i < members.Count; i++)
        {
            var lat = members[i].Average(point => point.Latitude);
            var lon = members[i].Average(point => point.Longitude);
            nodes.Add(new LoopNode(i, lat, lon));
        }

        var nodeGrid = new Dictionary<(int, int), List<int>>();
        for (var i = 0; i < nodes.Count; i++)
            Add(nodeGrid, new GeoCoord(nodes[i].Latitude, nodes[i].Longitude, 0), i, options.SnapMeters);

        var drafts = new List<EdgeDraft>();
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            var startNode = endpointNode[lineIndex * 2];
            var endNode = endpointNode[lineIndex * 2 + 1];
            var elapsed = LoopGeometry.ElapsedMeters(line.Points);
            var splits = FindSplits(line.Points, elapsed, nodes, nodeGrid, startNode, endNode, options.SnapMeters);
            if (startNode == endNode && splits.Count == 0 && elapsed[^1] > 80 && line.Points.Count >= 3)
            {
                var farIndex = FarthestIndex(line.Points);
                var synthetic = new LoopNode(nodes.Count, line.Points[farIndex].Latitude, line.Points[farIndex].Longitude);
                nodes.Add(synthetic);
                Add(nodeGrid, line.Points[farIndex], synthetic.Id, options.SnapMeters);
                splits.Add((elapsed[farIndex], synthetic.Id));
            }

            var chain = new List<(double Along, int Node)>(splits.Count + 2) { (0, startNode) };
            chain.AddRange(splits);
            chain.Add((elapsed[^1], endNode));
            chain.Sort((left, right) => left.Along.CompareTo(right.Along));
            for (var i = 1; i < chain.Count; i++)
            {
                if (chain[i].Node == chain[i - 1].Node || chain[i].Along - chain[i - 1].Along < 12)
                    continue;
                var geometry = Slice(line.Points, elapsed, chain[i - 1].Along, chain[i].Along, nodes[chain[i - 1].Node], nodes[chain[i].Node]);
                var length = LoopGeometry.LengthMeters(geometry);
                if (length < 12)
                    continue;
                drafts.Add(new EdgeDraft(chain[i - 1].Node, chain[i].Node, length, line.Segment.Facility, line.Segment.Surface, line.Segment.Name, line.Segment.Source, geometry));
            }
        }

        var edges = Deduplicate(drafts);
        AddLinks(nodes, edges, options);
        for (var i = 0; i < edges.Count; i++)
            edges[i].Id = i;

        var outgoing = new List<int>[nodes.Count];
        for (var i = 0; i < outgoing.Length; i++)
            outgoing[i] = [];
        foreach (var edge in edges)
        {
            if ((uint)edge.A >= (uint)outgoing.Length || (uint)edge.B >= (uint)outgoing.Length || edge.A == edge.B)
                continue;
            outgoing[edge.A].Add(edge.Id);
            outgoing[edge.B].Add(edge.Id);
        }

        return new LoopGraph(nodes, edges, outgoing.Select(list => list.ToArray()).ToArray());
    }

    private static List<GeoCoord> Clean(IReadOnlyList<GeoCoord> points)
    {
        var cleaned = new List<GeoCoord>(points.Count);
        foreach (var point in points)
        {
            if (!RoutingLimits.IsPosition(point.Latitude, point.Longitude))
                continue;
            if (cleaned.Count > 0
                && GeoMath.DistanceMeters(cleaned[^1].Latitude, cleaned[^1].Longitude, point.Latitude, point.Longitude) < 1)
                continue;
            cleaned.Add(point);
        }
        return cleaned;
    }

    private static List<(double Along, int Node)> FindSplits(
        IReadOnlyList<GeoCoord> points,
        IReadOnlyList<double> elapsed,
        List<LoopNode> nodes,
        Dictionary<(int, int), List<int>> nodeGrid,
        int startNode,
        int endNode,
        double snapMeters)
    {
        var seen = new HashSet<int>();
        var hits = new List<(double Along, double Offset, int Node)>();
        for (var i = 0; i < points.Count; i++)
            Consider(points[i]);
        for (var i = 1; i < points.Count; i++)
        {
            var span = elapsed[i] - elapsed[i - 1];
            var steps = Math.Max(1, (int)Math.Ceiling(span / Math.Max(snapMeters, 1)));
            for (var step = 1; step < steps; step++)
            {
                var t = step / (double)steps;
                Consider(new GeoCoord(
                    points[i - 1].Latitude + (points[i].Latitude - points[i - 1].Latitude) * t,
                    points[i - 1].Longitude + (points[i].Longitude - points[i - 1].Longitude) * t,
                    Altitude.Unknown.Kilometers));
            }
        }

        void Consider(GeoCoord probe)
        {
            foreach (var nodeId in Near(nodeGrid, probe, snapMeters))
            {
                if (!seen.Add(nodeId) || nodeId == startNode || nodeId == endNode)
                    continue;
                var node = nodes[nodeId];
                var closest = LoopGeometry.Closest(points, node.Latitude, node.Longitude);
                if (closest.OffsetMeters > snapMeters)
                    continue;
                if (closest.AlongMeters < snapMeters || closest.AlongMeters > elapsed[^1] - snapMeters)
                    continue;
                hits.Add((closest.AlongMeters, closest.OffsetMeters, nodeId));
            }
        }

        hits.Sort((left, right) => left.Along.CompareTo(right.Along));
        var kept = new List<(double Along, int Node)>();
        foreach (var hit in hits)
        {
            if (kept.Count > 0 && Math.Abs(kept[^1].Along - hit.Along) < 15)
                continue;
            kept.Add((hit.Along, hit.Node));
        }
        return kept;
    }

    private static int FarthestIndex(IReadOnlyList<GeoCoord> points)
    {
        var best = 1;
        var bestDistance = 0.0;
        for (var i = 1; i < points.Count - 1; i++)
        {
            var distance = GeoMath.DistanceMeters(points[0].Latitude, points[0].Longitude, points[i].Latitude, points[i].Longitude);
            if (distance > bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }
        return best;
    }

    private static List<GeoCoord> Slice(
        IReadOnlyList<GeoCoord> points,
        IReadOnlyList<double> elapsed,
        double from,
        double to,
        LoopNode start,
        LoopNode end)
    {
        var geometry = new List<GeoCoord> { new(start.Latitude, start.Longitude, Altitude.Unknown.Kilometers) };
        for (var i = 1; i < points.Count - 1; i++)
        {
            if (elapsed[i] <= from + 1 || elapsed[i] >= to - 1)
                continue;
            geometry.Add(points[i]);
        }
        geometry.Add(new GeoCoord(end.Latitude, end.Longitude, Altitude.Unknown.Kilometers));
        return geometry;
    }

    private static List<LoopEdge> Deduplicate(List<EdgeDraft> drafts)
    {
        var best = new Dictionary<(int, int), EdgeDraft>();
        foreach (var draft in drafts)
        {
            var key = draft.A < draft.B ? (draft.A, draft.B) : (draft.B, draft.A);
            if (!best.TryGetValue(key, out var existing) || Rank(draft) < Rank(existing))
                best[key] = draft;
        }
        return best.Values.Select(draft => new LoopEdge
        {
            A = draft.A,
            B = draft.B,
            LengthMeters = draft.LengthMeters,
            Facility = draft.Facility,
            Surface = draft.Surface,
            Name = draft.Name,
            Source = draft.Source,
            Geometry = draft.Geometry
        }).ToList();
    }

    private static int Rank(EdgeDraft draft)
    {
        var named = string.IsNullOrWhiteSpace(draft.Name) ? 1 : 0;
        var kind = draft.Facility switch
        {
            LoopFacility.Trail when draft.Surface == LoopSurface.Paved => 0,
            LoopFacility.Trail when draft.Surface == LoopSurface.Unknown => 2,
            LoopFacility.Trail => 4,
            LoopFacility.Lane => 3,
            _ => 6
        };
        return kind * 2 + named;
    }

    private static void AddLinks(List<LoopNode> nodes, List<LoopEdge> edges, LoopGraphOptions options)
    {
        if (options.LinkMeters <= 0 || nodes.Count == 0)
            return;
        var hasTrail = new bool[nodes.Count];
        var hasLane = new bool[nodes.Count];
        var degree = new int[nodes.Count];
        var pairs = new HashSet<(int, int)>();
        foreach (var edge in edges)
        {
            Touch(edge.A);
            Touch(edge.B);
            pairs.Add(Key(edge.A, edge.B));
            void Touch(int node)
            {
                if ((uint)node >= (uint)nodes.Count)
                    return;
                degree[node]++;
                if (edge.Facility == LoopFacility.Trail)
                    hasTrail[node] = true;
                else if (edge.Facility == LoopFacility.Lane)
                    hasLane[node] = true;
            }
        }

        var grid = new Dictionary<(int, int), List<int>>();
        for (var i = 0; i < nodes.Count; i++)
            Add(grid, new GeoCoord(nodes[i].Latitude, nodes[i].Longitude, 0), i, options.LinkMeters);

        var links = new List<(double Distance, int A, int B)>();
        for (var a = 0; a < nodes.Count; a++)
        {
            foreach (var b in Near(grid, new GeoCoord(nodes[a].Latitude, nodes[a].Longitude, 0), options.LinkMeters))
            {
                if (b <= a || pairs.Contains(Key(a, b)))
                    continue;
                var distance = GeoMath.DistanceMeters(nodes[a].Latitude, nodes[a].Longitude, nodes[b].Latitude, nodes[b].Longitude);
                if (distance < 8 || distance > options.LinkMeters || !Useful(a, b))
                    continue;
                links.Add((distance, a, b));
            }
        }

        links.Sort((left, right) => left.Distance.CompareTo(right.Distance));
        var added = new int[nodes.Count];
        foreach (var link in links)
        {
            if (added[link.A] >= options.MaxLinksPerNode || added[link.B] >= options.MaxLinksPerNode)
                continue;
            if (!pairs.Add(Key(link.A, link.B)))
                continue;
            edges.Add(new LoopEdge
            {
                A = link.A,
                B = link.B,
                LengthMeters = link.Distance,
                Facility = LoopFacility.Link,
                Surface = LoopSurface.Unknown,
                Name = "",
                Source = "link",
                Geometry =
                [
                    new GeoCoord(nodes[link.A].Latitude, nodes[link.A].Longitude, Altitude.Unknown.Kilometers),
                    new GeoCoord(nodes[link.B].Latitude, nodes[link.B].Longitude, Altitude.Unknown.Kilometers)
                ]
            });
            added[link.A]++;
            added[link.B]++;
        }

        bool Useful(int a, int b)
        {
            var trailToLane = (hasTrail[a] && hasLane[b]) || (hasLane[a] && hasTrail[b]);
            if (trailToLane)
                return true;
            var sameTrail = hasTrail[a] && hasTrail[b] && (degree[a] <= 1 || degree[b] <= 1);
            var sameLane = hasLane[a] && hasLane[b] && (degree[a] <= 1 || degree[b] <= 1);
            return sameTrail || sameLane;
        }
    }

    private static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);

    private static void Add(Dictionary<(int, int), List<int>> grid, GeoCoord point, int id, double meters)
    {
        var key = Cell(point.Latitude, point.Longitude, meters);
        if (!grid.TryGetValue(key, out var list))
        {
            list = [];
            grid[key] = list;
        }
        list.Add(id);
    }

    private static IEnumerable<int> Near(Dictionary<(int, int), List<int>> grid, GeoCoord point, double meters)
    {
        var (row, column) = Cell(point.Latitude, point.Longitude, meters);
        for (var r = row - 1; r <= row + 1; r++)
        {
            for (var c = column - 1; c <= column + 1; c++)
            {
                if (!grid.TryGetValue((r, c), out var list))
                    continue;
                foreach (var id in list)
                    yield return id;
            }
        }
    }

    private static (int Row, int Column) Cell(double latitude, double longitude, double meters)
    {
        var height = Math.Max(meters, 1) / 111_320.0;
        var width = Math.Max(meters, 1) / (111_320.0 * Math.Cos(latitude * Math.PI / 180.0));
        return ((int)Math.Floor(latitude / height), (int)Math.Floor(longitude / Math.Max(width, 1e-9)));
    }

    private static int Find(int[] parent, int index)
    {
        while (parent[index] != index)
        {
            parent[index] = parent[parent[index]];
            index = parent[index];
        }
        return index;
    }

    private static void Union(int[] parent, int left, int right)
    {
        var a = Find(parent, left);
        var b = Find(parent, right);
        if (a != b)
            parent[b] = a;
    }

    private sealed record RawLine(List<GeoCoord> Points, NetworkSegment Segment);

    private sealed record EdgeDraft(
        int A,
        int B,
        double LengthMeters,
        LoopFacility Facility,
        LoopSurface Surface,
        string Name,
        string Source,
        List<GeoCoord> Geometry);
}

public sealed class LoopGraph
{
    private readonly int[][] _outgoing;

    public LoopGraph(IReadOnlyList<LoopNode> nodes, IReadOnlyList<LoopEdge> edges, int[][] outgoing)
    {
        Nodes = nodes;
        Edges = edges;
        _outgoing = outgoing;
    }

    public IReadOnlyList<LoopNode> Nodes { get; }
    public IReadOnlyList<LoopEdge> Edges { get; }

    public ReadOnlySpan<int> From(int node) =>
        (uint)node < (uint)_outgoing.Length ? _outgoing[node] : ReadOnlySpan<int>.Empty;

    public int Nearest(double latitude, double longitude, double maxMeters)
    {
        var best = -1;
        var bestDistance = maxMeters;
        for (var i = 0; i < Nodes.Count; i++)
        {
            var distance = GeoMath.DistanceMeters(latitude, longitude, Nodes[i].Latitude, Nodes[i].Longitude);
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }
        return best;
    }
}
