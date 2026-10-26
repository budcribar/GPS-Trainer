using GpsTrainer;

namespace GpsTrainer.Shared.Tests;

public class TurnDetectorTests
{
    [Fact]
    public void The_first_sample_only_starts_the_detector()
    {
        var detector = new TurnDetector();
        var first = detector.Update(Sample(0, heading: 0), Speed.FromMetersPerSecond(5));
        Assert.Null(first);
    }

    [Fact]
    public void A_right_turn_after_60_meters_inserts_a_turn_waypoint()
    {
        var detector = new TurnDetector();
        Assert.Null(detector.Update(Sample(0, 0), Speed.FromMetersPerSecond(5)));

        Waypoint? turn = null;
        for (var meters = 0; meters <= 80; meters += 10)
            turn ??= detector.Update(Sample(meters, heading: 0), Speed.FromMetersPerSecond(5));
        Assert.Null(turn);

        for (var meters = 90; meters <= 220 && turn is null; meters += 10)
            turn = detector.Update(Sample(meters, heading: 90), Speed.FromMetersPerSecond(5));

        Assert.NotNull(turn);
        Assert.Equal(WaypointType.TurnRight, turn.WaypointType);
        Assert.Equal(TurnDetector.TurnDetectorFlag, turn.TextDescription);
        Assert.InRange(turn.ElapsedDistanceKm * 1000, 20, 200);
    }

    [Fact]
    public void A_heading_reversal_inserts_a_turnaround()
    {
        var detector = new TurnDetector();
        detector.Update(Sample(0, 0), Speed.FromMetersPerSecond(5));
        Waypoint? turn = null;
        for (var meters = 0; meters <= 100; meters += 10)
            detector.Update(Sample(meters, heading: 0), Speed.FromMetersPerSecond(5));
        for (var meters = 110; meters <= 260 && turn is null; meters += 10)
            turn = detector.Update(Sample(meters, heading: 180), Speed.FromMetersPerSecond(5));

        Assert.NotNull(turn);
        Assert.Equal(WaypointType.TurnAround, turn.WaypointType);
        Assert.Null(turn.TextDescription);
    }

    [Fact]
    public void A_left_turn_is_turn_left()
    {
        var turn = Run(headingAfter: 270);
        Assert.NotNull(turn);
        Assert.Equal(WaypointType.TurnLeft, turn.WaypointType);
    }

    [Fact]
    public void Walking_slower_than_one_mile_per_hour_does_not_insert()
    {
        var detector = new TurnDetector();
        var slow = Speed.FromStatuteMilesPerHour(0.9);
        detector.Update(Sample(0, 0), slow);
        Waypoint? turn = null;
        for (var meters = 0; meters <= 200; meters += 10)
            turn ??= detector.Update(Sample(meters, meters < 100 ? 0 : 90), slow);
        Assert.Null(turn);
    }

    [Fact]
    public void A_wraparound_heading_of_twenty_degrees_is_not_a_turn()
    {
        Assert.Equal(20, HeadingTurn.Difference(10, 350), 0);
        Assert.Equal(WaypointType.TurnLeft, HeadingTurn.Direction(10, 350));
        var detector = new TurnDetector();
        detector.Update(Sample(0, 10), Speed.FromMetersPerSecond(5));
        Waypoint? turn = null;
        for (var meters = 0; meters <= 80; meters += 10)
            detector.Update(Sample(meters, heading: 10), Speed.FromMetersPerSecond(5));
        for (var meters = 90; meters <= 200 && turn is null; meters += 10)
            turn = detector.Update(Sample(meters, heading: 350), Speed.FromMetersPerSecond(5));
        Assert.Null(turn);
    }

    private static Waypoint? Run(double headingAfter)
    {
        var detector = new TurnDetector();
        detector.Update(Sample(0, 0), Speed.FromMetersPerSecond(5));
        for (var meters = 0; meters <= 80; meters += 10)
            detector.Update(Sample(meters, heading: 0), Speed.FromMetersPerSecond(5));
        Waypoint? turn = null;
        for (var meters = 90; meters <= 240 && turn is null; meters += 10)
            turn = detector.Update(Sample(meters, headingAfter), Speed.FromMetersPerSecond(5));
        return turn;
    }

    private static Waypoint Sample(double elapsedMeters, double heading) => new()
    {
        Latitude = 40 + elapsedMeters / 111_320d,
        Longitude = -105,
        HeadingDegrees = heading,
        ElapsedDistanceKm = elapsedMeters / 1000d,
        TimestampTicks = 10_000 + (long)elapsedMeters,
        AltitudeKm = 1.6,
        WaypointType = WaypointType.Continue
    };
}
