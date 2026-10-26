namespace GpsTrainer;

/// <summary>
/// Ranks a loop on distance match, climbing match, low backtracking, and the
/// share of the route that stays on bike-legal paths. Corridor and facility
/// preference only breaks a near tie so a Poudre or Spring Creek loop wins
/// when the four scores are otherwise the same.
/// </summary>
public static class LoopScorer
{
    public static LoopRoute Score(LoopRoute route, LoopRequest request)
    {
        var distance = DistanceScore(route.DistanceMeters, request.TargetDistanceMeters);
        var climbing = ClimbScore(route.ClimbingMeters, route.DistanceMeters, request.Climbing, route.ElevationKnown);
        var backtracking = 1 - Math.Clamp(route.BacktrackShare, 0, 1);
        var legal = Math.Clamp(route.BikeLegalShare, 0, 1);
        var total = 0.40 * distance + 0.25 * climbing + 0.20 * backtracking + 0.15 * legal;
        return route with
        {
            Score = total,
            Breakdown = new LoopScore(distance, climbing, backtracking, legal, total)
        };
    }

    public static double SortBias(LoopRoute route, LoopRequest request)
    {
        var preferred = request.Type == LoopRouteType.TrailFirst
            ? Math.Max(route.CorridorShare, route.TrailShare * 0.35)
            : route.LaneShare;
        return route.Score + 0.02 * Math.Clamp(preferred, 0, 1);
    }

    public static double DistanceScore(double actualMeters, double targetMeters)
    {
        if (targetMeters <= 0 || !double.IsFinite(actualMeters))
            return 0;
        var error = Math.Abs(actualMeters - targetMeters) / targetMeters;
        return Math.Clamp(1 - error / 0.25, 0, 1);
    }

    public static double PreferredGainPerKm(ClimbPreference preference) => preference switch
    {
        ClimbPreference.Flat => 4,
        ClimbPreference.Hilly => 28,
        _ => 12
    };

    public static double ClimbScore(double climbingMeters, double distanceMeters, ClimbPreference preference, bool elevationKnown)
    {
        if (!elevationKnown || distanceMeters < 1 || !double.IsFinite(climbingMeters))
            return 0.55;
        var rate = climbingMeters / (distanceMeters / 1000.0);
        var sigma = preference == ClimbPreference.Hilly ? 14 : 8;
        var z = (rate - PreferredGainPerKm(preference)) / sigma;
        return Math.Exp(-0.5 * z * z);
    }

    public static (double BikeLegal, double Trail, double Lane, double Corridor, double Backtrack) Shares(
        IReadOnlyList<int> edgeIds, LoopGraph graph)
    {
        if (edgeIds.Count == 0)
            return (0, 0, 0, 0, 1);
        var counts = new Dictionary<int, int>();
        double total = 0, trail = 0, lane = 0, corridor = 0, legal = 0;
        foreach (var id in edgeIds)
        {
            if ((uint)id >= (uint)graph.Edges.Count)
                continue;
            var edge = graph.Edges[id];
            total += edge.LengthMeters;
            counts[id] = counts.GetValueOrDefault(id) + 1;
            if (edge.Facility == LoopFacility.Trail)
                trail += edge.LengthMeters;
            else if (edge.Facility == LoopFacility.Lane)
                lane += edge.LengthMeters;
            if (edge.Facility != LoopFacility.Link)
                legal += edge.LengthMeters;
            else
                legal += edge.LengthMeters;
            if (LoopGeometry.IsPriorityCorridor(edge.Name))
                corridor += edge.LengthMeters;
        }
        if (total <= 0)
            return (0, 0, 0, 0, 1);
        double repeated = 0;
        foreach (var (id, count) in counts)
        {
            if (count > 1)
                repeated += graph.Edges[id].LengthMeters * (count - 1);
        }
        return (legal / total, trail / total, lane / total, corridor / total, repeated / total);
    }
}
