namespace GpsTrainer;

/// <summary>
/// Builds candidate loops by leaving the start along the bike network and
/// coming back on a different set of edges. Trail-first searches also run
/// with a lower cost on the Poudre River Trail and Spring Creek Trail.
/// </summary>
public static class LoopGenerator
{
    public static IReadOnlyList<LoopRoute> Generate(
        LoopGraph graph,
        LoopRequest request,
        Func<double, double, double?>? elevationMeters = null,
        int take = 3)
    {
        if (graph.Nodes.Count == 0 || request.TargetDistanceMeters <= 0)
            return [];
        var origin = graph.Nearest(request.Latitude, request.Longitude, 1_500);
        if (origin < 0)
            return [];

        var pool = new Dictionary<string, LoopRoute>(StringComparer.Ordinal);
        foreach (var weight in Profiles(request))
        {
            var search = Search(graph, origin, weight, null, 1);
            foreach (var far in FarNodes(graph, origin, search.Length, request.TargetDistanceMeters))
            {
                var outbound = PathEdges(graph, origin, far, search);
                if (outbound.Count == 0)
                    continue;
                var used = outbound.Select(step => step.Edge).ToHashSet();
                var back = Search(graph, far, weight, used, 8);
                var inbound = PathEdges(graph, far, origin, back);
                if (inbound.Count == 0)
                    continue;
                Consider(graph, request, outbound.Concat(inbound).ToList(), pool);
            }

            AddTriangles(graph, request, origin, search, weight, pool);
        }

        var routes = pool.Values.Select(route => WithElevation(route, elevationMeters)).Select(route => LoopScorer.Score(route, request)).ToList();
        return Pick(routes, request, Math.Max(1, take));
    }

    public static string NameFor(IReadOnlyList<int> edgeIds, LoopGraph graph, LoopRouteType type)
    {
        double total = 0;
        var named = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in edgeIds)
        {
            if ((uint)id >= (uint)graph.Edges.Count)
                continue;
            var edge = graph.Edges[id];
            total += edge.LengthMeters;
            if (string.IsNullOrWhiteSpace(edge.Name))
                continue;
            named[edge.Name] = named.GetValueOrDefault(edge.Name) + edge.LengthMeters;
        }
        var ranked = named.OrderByDescending(pair => pair.Value).ToList();
        if (ranked.Count == 0 || total <= 0)
            return type == LoopRouteType.BikeLanes ? "Fort Collins bike-lane loop" : "Fort Collins trail loop";
        var first = ranked[0];
        if (ranked.Count == 1 || first.Value >= total * 0.45 || ranked[1].Value < total * 0.12)
            return first.Key.Trim() + " loop";
        return first.Key.Trim() + " and " + ranked[1].Key.Trim();
    }

    private static IEnumerable<Func<LoopEdge, double>> Profiles(LoopRequest request)
    {
        yield return edge => Weight(edge, request, corridorBias: false);
        if (request.Type == LoopRouteType.TrailFirst)
            yield return edge => Weight(edge, request, corridorBias: true);
    }

    public static double Weight(LoopEdge edge, LoopRequest request, bool corridorBias)
    {
        var factor = edge.Facility switch
        {
            LoopFacility.Trail when edge.Surface == LoopSurface.Paved => request.Type == LoopRouteType.TrailFirst ? 1.0 : 2.4,
            LoopFacility.Trail when edge.Surface == LoopSurface.Unknown => request.Type == LoopRouteType.TrailFirst ? 1.25 : 3.0,
            LoopFacility.Trail => request.Type == LoopRouteType.TrailFirst
                ? request.Climbing == ClimbPreference.Hilly ? 1.7 : 3.8
                : 5.0,
            LoopFacility.Lane => request.Type == LoopRouteType.BikeLanes ? 1.0 : 2.15,
            _ => 1.35
        };
        if (corridorBias && LoopGeometry.IsPriorityCorridor(edge.Name))
            factor *= 0.55;
        return Math.Max(edge.LengthMeters, 1) * factor;
    }

    private static void AddTriangles(
        LoopGraph graph,
        LoopRequest request,
        int origin,
        SearchResult search,
        Func<LoopEdge, double> weight,
        Dictionary<string, LoopRoute> pool)
    {
        var far = FarNodes(graph, origin, search.Length, request.TargetDistanceMeters).Take(8).ToList();
        var pairs = 0;
        for (var i = 0; i < far.Count && pairs < 8; i++)
        {
            for (var j = i + 1; j < far.Count && pairs < 8; j++)
            {
                var bearingA = Bearing(graph, origin, far[i]);
                var bearingB = Bearing(graph, origin, far[j]);
                var separation = Math.Abs(bearingA - bearingB);
                separation = Math.Min(separation, 360 - separation);
                if (separation < 50 || separation > 160)
                    continue;
                pairs++;
                var first = PathEdges(graph, origin, far[i], search);
                if (first.Count == 0)
                    continue;
                var used = first.Select(step => step.Edge).ToHashSet();
                var middleSearch = Search(graph, far[i], weight, used, 6);
                var middle = PathEdges(graph, far[i], far[j], middleSearch);
                if (middle.Count == 0)
                    continue;
                used.UnionWith(middle.Select(step => step.Edge));
                var lastSearch = Search(graph, far[j], weight, used, 6);
                var last = PathEdges(graph, far[j], origin, lastSearch);
                if (last.Count == 0)
                    continue;
                Consider(graph, request, first.Concat(middle).Concat(last).ToList(), pool);
            }
        }
    }

    private static void Consider(LoopGraph graph, LoopRequest request, List<PathStep> steps, Dictionary<string, LoopRoute> pool)
    {
        if (steps.Count == 0)
            return;
        var edgeIds = steps.Select(step => step.Edge).ToList();
        var distance = edgeIds.Sum(id => graph.Edges[id].LengthMeters);
        var strict = distance >= request.TargetDistanceMeters * 0.70 && distance <= request.TargetDistanceMeters * 1.28;
        var relaxed = distance >= request.TargetDistanceMeters * 0.50 && distance <= request.TargetDistanceMeters * 1.55;
        if (!relaxed)
            return;
        var shares = LoopScorer.Shares(edgeIds, graph);
        if (shares.Backtrack > 0.62)
            return;
        var signature = Signature(edgeIds);
        if (pool.TryGetValue(signature, out var existing) && (existing.DistanceMeters > 0 && strict == Band(existing, request)))
            return;
        if (pool.ContainsKey(signature) && !strict)
            return;

        var route = new LoopRoute
        {
            Name = NameFor(edgeIds, graph, request.Type),
            Points = GeometryOf(graph, steps),
            EdgeIds = edgeIds,
            DistanceMeters = distance,
            BikeLegalShare = shares.BikeLegal,
            TrailShare = shares.Trail,
            LaneShare = shares.Lane,
            BacktrackShare = shares.Backtrack,
            CorridorShare = shares.Corridor,
            ElevationKnown = false
        };
        if (!pool.ContainsKey(signature) || strict)
            pool[signature] = route;
    }

    private static bool Band(LoopRoute route, LoopRequest request) =>
        route.DistanceMeters >= request.TargetDistanceMeters * 0.70
        && route.DistanceMeters <= request.TargetDistanceMeters * 1.28;

    private static LoopRoute WithElevation(LoopRoute route, Func<double, double, double?>? elevationMeters)
    {
        if (elevationMeters is null)
            return route;
        var points = LoopGeometry.WithAltitude(route.Points, elevationMeters);
        var known = points.Any(point => point.HasAltitude);
        return route with
        {
            Points = points,
            ElevationKnown = known,
            ClimbingMeters = known ? LoopGeometry.ClimbingMeters(points) : 0
        };
    }

    private static List<LoopRoute> Pick(List<LoopRoute> routes, LoopRequest request, int take)
    {
        routes.Sort((left, right) => LoopScorer.SortBias(right, request).CompareTo(LoopScorer.SortBias(left, request)));
        var strict = routes.Where(route => Band(route, request)).ToList();
        var source = strict.Count > 0 ? strict : routes;
        var chosen = new List<LoopRoute>();
        foreach (var route in source)
        {
            if (chosen.Any(other => Similar(route, other)))
                continue;
            chosen.Add(route);
            if (chosen.Count == take)
                break;
        }
        if (chosen.Count < take)
        {
            foreach (var route in routes)
            {
                if (chosen.Contains(route) || chosen.Any(other => Similar(route, other)))
                    continue;
                chosen.Add(route);
                if (chosen.Count == take)
                    break;
            }
        }
        return chosen;
    }

    private static bool Similar(LoopRoute left, LoopRoute right)
    {
        var a = left.EdgeIds.ToHashSet();
        var b = right.EdgeIds.ToHashSet();
        if (a.Count == 0 || b.Count == 0)
            return false;
        var intersection = a.Intersect(b).Count();
        var union = a.Count + b.Count - intersection;
        return union > 0 && (double)intersection / union >= 0.72;
    }

    private static string Signature(IReadOnlyList<int> edgeIds)
    {
        var counts = new Dictionary<int, int>();
        foreach (var id in edgeIds)
            counts[id] = counts.GetValueOrDefault(id) + 1;
        return string.Join(',', counts.OrderBy(pair => pair.Key).Select(pair => pair.Key + "x" + pair.Value));
    }

    private static List<int> FarNodes(LoopGraph graph, int origin, double[] length, double target)
    {
        var ideal = target * 0.42;
        var buckets = new Dictionary<int, int>();
        for (var node = 0; node < length.Length; node++)
        {
            var distance = length[node];
            if (node == origin || distance < target * 0.28 || distance > target * 0.58 || double.IsInfinity(distance))
                continue;
            var bucket = (int)(Bearing(graph, origin, node) / 22.5);
            if (!buckets.TryGetValue(bucket, out var current) || Math.Abs(distance - ideal) < Math.Abs(length[current] - ideal))
                buckets[bucket] = node;
        }
        return buckets.Values
            .OrderBy(node => Math.Abs(length[node] - ideal))
            .Take(16)
            .ToList();
    }

    private static double Bearing(LoopGraph graph, int from, int to) =>
        GeoMath.InitialBearingDegrees(graph.Nodes[from].Latitude, graph.Nodes[from].Longitude, graph.Nodes[to].Latitude, graph.Nodes[to].Longitude);

    private static List<GeoCoord> GeometryOf(LoopGraph graph, IReadOnlyList<PathStep> steps)
    {
        var points = new List<GeoCoord>();
        foreach (var step in steps)
        {
            var geometry = graph.Edges[step.Edge].Geometry;
            if (step.AToB)
            {
                foreach (var point in geometry)
                    Append(points, point);
            }
            else
            {
                for (var i = geometry.Count - 1; i >= 0; i--)
                    Append(points, geometry[i]);
            }
        }
        return points;
    }

    private static void Append(List<GeoCoord> points, GeoCoord point)
    {
        if (points.Count > 0
            && GeoMath.DistanceMeters(points[^1].Latitude, points[^1].Longitude, point.Latitude, point.Longitude) < 1)
            return;
        points.Add(point);
    }

    private static List<PathStep> PathEdges(LoopGraph graph, int source, int target, SearchResult search)
    {
        if (source == target)
            return [];
        var reversed = new List<PathStep>();
        var node = target;
        var guard = 0;
        while (node != source && guard++ <= graph.Nodes.Count)
        {
            var edgeId = search.Via[node];
            if (edgeId < 0)
                return [];
            var edge = graph.Edges[edgeId];
            var aToB = search.AToB[node];
            reversed.Add(new PathStep(edgeId, aToB));
            node = aToB ? edge.A : edge.B;
        }
        if (node != source)
            return [];
        reversed.Reverse();
        return reversed;
    }

    private static SearchResult Search(LoopGraph graph, int source, Func<LoopEdge, double> weight, HashSet<int>? costly, double penalty)
    {
        var count = graph.Nodes.Count;
        var length = new double[count];
        var cost = new double[count];
        var via = new int[count];
        var aToB = new bool[count];
        Array.Fill(length, double.PositiveInfinity);
        Array.Fill(cost, double.PositiveInfinity);
        Array.Fill(via, -1);
        var queue = new PriorityQueue<int, double>();
        length[source] = 0;
        cost[source] = 0;
        queue.Enqueue(source, 0);
        while (queue.Count > 0)
        {
            queue.TryDequeue(out var node, out var nodeCost);
            if (nodeCost > cost[node] + 1e-6)
                continue;
            foreach (var edgeId in graph.From(node))
            {
                var edge = graph.Edges[edgeId];
                var next = edge.Other(node);
                var step = weight(edge);
                if (costly is not null && costly.Contains(edgeId))
                    step *= penalty;
                var nextCost = cost[node] + step;
                if (nextCost + 1e-6 >= cost[next])
                    continue;
                cost[next] = nextCost;
                length[next] = length[node] + edge.LengthMeters;
                via[next] = edgeId;
                aToB[next] = edge.A == node;
                queue.Enqueue(next, nextCost);
            }
        }
        return new SearchResult(length, via, aToB);
    }

    private readonly record struct PathStep(int Edge, bool AToB);

    private readonly record struct SearchResult(double[] Length, int[] Via, bool[] AToB);
}
