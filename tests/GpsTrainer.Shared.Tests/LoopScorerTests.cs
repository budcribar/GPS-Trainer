namespace GpsTrainer.Shared.Tests;

public class LoopScorerTests
{
    [Fact]
    public void Distance_score_is_one_on_the_target_and_zero_at_a_quarter_off()
    {
        Assert.Equal(1, LoopScorer.DistanceScore(10_000, 10_000));
        Assert.Equal(0, LoopScorer.DistanceScore(12_500, 10_000));
        Assert.True(LoopScorer.DistanceScore(11_000, 10_000) > LoopScorer.DistanceScore(12_000, 10_000));
    }

    [Fact]
    public void Flat_climbing_preference_ranks_a_flatter_route_higher()
    {
        var flat = Score(climbingMeters: 20, preference: ClimbPreference.Flat);
        var steep = Score(climbingMeters: 280, preference: ClimbPreference.Flat);

        Assert.True(flat.Score > steep.Score);
        Assert.True(flat.Breakdown.Climbing > steep.Breakdown.Climbing);
    }

    [Fact]
    public void Hilly_climbing_preference_ranks_a_steeper_route_higher()
    {
        var flat = Score(climbingMeters: 20, preference: ClimbPreference.Hilly);
        var steep = Score(climbingMeters: 280, preference: ClimbPreference.Hilly);

        Assert.True(steep.Breakdown.Climbing > flat.Breakdown.Climbing);
    }

    [Fact]
    public void Backtracking_and_off_network_distance_lower_the_score()
    {
        var clean = Score(backtrack: 0, legal: 1);
        var repeated = Score(backtrack: 0.5, legal: 1);
        var offNetwork = Score(backtrack: 0, legal: 0.4);

        Assert.True(clean.Score > repeated.Score);
        Assert.True(clean.Score > offNetwork.Score);
        Assert.True(clean.Breakdown.Backtracking > repeated.Breakdown.Backtracking);
        Assert.True(clean.Breakdown.BikeLegal > offNetwork.Breakdown.BikeLegal);
    }

    [Fact]
    public void Unknown_elevation_uses_a_neutral_climbing_score()
    {
        var route = Score(climbingMeters: 0, preference: ClimbPreference.Flat, elevationKnown: false);
        Assert.Equal(0.55, route.Breakdown.Climbing);
    }

    [Fact]
    public void Near_ties_prefer_the_requested_facility()
    {
        var trail = Route(trail: 0.9, lane: 0.1, corridor: 0.8);
        var lane = Route(trail: 0.1, lane: 0.9, corridor: 0);
        var requestTrail = new LoopRequest(0, 0, 10_000, ClimbPreference.Flat, LoopRouteType.TrailFirst);
        var requestLane = new LoopRequest(0, 0, 10_000, ClimbPreference.Flat, LoopRouteType.BikeLanes);
        var scoredTrail = LoopScorer.Score(trail, requestTrail);
        var scoredLane = LoopScorer.Score(lane, requestLane);

        Assert.Equal(scoredTrail.Score, LoopScorer.Score(lane, requestTrail).Score, 5);
        Assert.True(LoopScorer.SortBias(scoredTrail, requestTrail) > LoopScorer.SortBias(LoopScorer.Score(lane, requestTrail), requestTrail));
        Assert.True(LoopScorer.SortBias(scoredLane, requestLane) > LoopScorer.SortBias(LoopScorer.Score(trail, requestLane), requestLane));
    }

    private static LoopRoute Score(
        double climbingMeters = 40,
        ClimbPreference preference = ClimbPreference.Moderate,
        double backtrack = 0,
        double legal = 1,
        bool elevationKnown = true) =>
        LoopScorer.Score(Route(climbingMeters, backtrack, legal, elevationKnown), new LoopRequest(0, 0, 10_000, preference, LoopRouteType.TrailFirst));

    private static LoopRoute Route(
        double climbingMeters = 40,
        double backtrack = 0,
        double legal = 1,
        bool elevationKnown = true,
        double trail = 1,
        double lane = 0,
        double corridor = 0) =>
        new()
        {
            Name = "Fixture",
            Points = [GeoCoord.At(40, -105), GeoCoord.At(40.01, -105)],
            EdgeIds = [0],
            DistanceMeters = 10_000,
            ClimbingMeters = climbingMeters,
            BikeLegalShare = legal,
            TrailShare = trail,
            LaneShare = lane,
            BacktrackShare = backtrack,
            CorridorShare = corridor,
            ElevationKnown = elevationKnown
        };
}
