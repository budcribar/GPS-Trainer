namespace GpsTrainer.Shared.Tests;

public class RaceScreenTests
{
    [Fact]
    public void The_race_screen_defaults_to_miles_and_mph()
    {
        var readout = RaceReadout.Create(TimeSpan.FromSeconds(65), 1.609344, 10, metric: false, showSpeed: true, checkpoint: null);

        Assert.Equal("00:01:05", readout.Time);
        Assert.Equal("sec", readout.TimeUnit);
        Assert.Equal("1.00", readout.DistanceValue);
        Assert.Equal("mi", readout.DistanceUnit);
        Assert.Equal("22.4", readout.Pace);
        Assert.Equal("mph", readout.PaceUnit);
        Assert.Equal("00:00:00", readout.Checkpoint);
        Assert.Equal("sec", readout.CheckpointUnit);
    }

    [Fact]
    public void Metric_uses_kilometers_and_kph()
    {
        var readout = RaceReadout.Create(TimeSpan.Zero, 2, 10, metric: true, showSpeed: true, checkpoint: null);

        Assert.Equal("2.00", readout.DistanceValue);
        Assert.Equal("km", readout.DistanceUnit);
        Assert.Equal("36.0", readout.Pace);
        Assert.Equal("kph", readout.PaceUnit);
    }

    [Fact]
    public void Pace_mode_uses_minutes_per_mile_until_metric_is_on()
    {
        var english = RaceReadout.Create(TimeSpan.Zero, 0, 4.4704, metric: false, showSpeed: false, checkpoint: null);
        var metric = RaceReadout.Create(TimeSpan.Zero, 0, 10, metric: true, showSpeed: false, checkpoint: null);

        Assert.Equal("mpm", english.PaceUnit);
        Assert.Equal("06:00", english.Pace);
        Assert.Equal("mpk", metric.PaceUnit);
        Assert.Equal("01:40", metric.Pace);
    }

    [Fact]
    public void A_checkpoint_shows_the_gap_as_ahead_or_behind()
    {
        var ahead = RaceReadout.Create(TimeSpan.FromMinutes(3), 1, 5, false, true, new GhostSplit(TimeSpan.FromSeconds(12), true));
        var behind = RaceReadout.Create(TimeSpan.FromMinutes(3), 1, 5, false, true, new GhostSplit(TimeSpan.FromSeconds(12), false));

        Assert.Equal("00:00:12", ahead.Checkpoint);
        Assert.Equal("ahead", ahead.CheckpointUnit);
        Assert.Equal("behind", behind.CheckpointUnit);
    }

    [Fact]
    public void Units_stay_english_unless_the_sport_preference_says_metric()
    {
        Assert.False(SportUnits.IsMetric("Cycling", _ => null));
        Assert.False(SportUnits.IsMetric("Cycling", key => key == "CyclingUnits" ? "English" : null));
        Assert.True(SportUnits.IsMetric("Running", key => key == "RunningUnits" ? "Metric" : null));
        Assert.True(SportUnits.ShowSpeed("Cycling", _ => null));
        Assert.False(SportUnits.ShowSpeed("Running", key => key == "RunningSpeed" ? "Pace" : null));
    }

    [Fact]
    public void A_finished_ride_keeps_the_moved_fixes_so_it_can_be_a_ghost()
    {
        var ride = new RideRecorder();
        var start = new DateTime(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc);
        Assert.True(ride.Record(40, -105, start));
        Assert.False(ride.Record(40, -105, start.AddSeconds(1)));
        var step = GeoMath.Translate(40, -105, 0, 0.001);
        Assert.False(ride.Record(step.Latitude, step.Longitude, start.AddSeconds(2)));
        var next = GeoMath.Translate(40, -105, 0, 0.05);
        Assert.True(ride.Record(next.Latitude, next.Longitude, start.AddSeconds(30)));
        Assert.Equal(2, ride.Points.Count);

        var split = GhostRace.Compare(
            ride.Points,
            next.Latitude,
            next.Longitude,
            TimeSpan.FromSeconds(20),
            TimeSpan.Zero,
            TimeSpan.Zero);

        Assert.NotNull(split);
        Assert.True(split!.Value.Ahead);
        Assert.Equal(TimeSpan.FromSeconds(10), split.Value.Amount);
    }

    [Fact]
    public void Replacing_a_race_track_keeps_only_the_finished_ride()
    {
        var path = Path.Combine(Path.GetTempPath(), "gpstrainer-track-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using var db = new LocalDatabase(path);
            db.EnsureCreated();
            var raceId = Guid.NewGuid();
            db.InsertPosition(new TrackPoint { RaceId = raceId, Latitude = 1, Longitude = 2, TimestampTicks = 10 });
            db.InsertPosition(new TrackPoint { RaceId = raceId, Latitude = 3, Longitude = 4, TimestampTicks = 20 });

            db.ReplacePositions(raceId,
            [
                new TrackPoint { Latitude = 40, Longitude = -105, TimestampTicks = 30 },
                new TrackPoint { Latitude = 40.01, Longitude = -105, TimestampTicks = 40 }
            ]);

            var stored = db.ListPositions(raceId);
            Assert.Equal(2, stored.Count);
            Assert.Equal(30, stored[0].TimestampTicks);
            Assert.Equal(40, stored[1].TimestampTicks);
            Assert.Equal(raceId, stored[0].RaceId);
            Assert.Equal(40, stored[0].Latitude);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
