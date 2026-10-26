using GpsTrainer;

namespace GpsTrainer.Shared.Tests;

public class GhostRaceTests
{
    [Fact]
    public void A_rider_who_arrives_early_is_ahead()
    {
        var track = Line();
        var here = track[2];
        var split = GhostRace.Compare(track, here.Latitude, here.Longitude, TimeSpan.FromSeconds(15), TimeSpan.Zero, TimeSpan.Zero);

        Assert.NotNull(split);
        Assert.True(split!.Value.Ahead);
        Assert.Equal(TimeSpan.FromSeconds(5), split.Value.Amount);
        Assert.Equal(new[] { "CheckPoint", "5 seconds", "ahead" }, GhostRace.Phrases(split.Value));
    }

    [Fact]
    public void A_rider_who_arrives_late_is_behind()
    {
        var track = Line();
        var here = track[2];
        var split = GhostRace.Compare(track, here.Latitude, here.Longitude, TimeSpan.FromSeconds(40), TimeSpan.Zero, TimeSpan.Zero);

        Assert.NotNull(split);
        Assert.False(split!.Value.Ahead);
        Assert.Equal(TimeSpan.FromSeconds(20), split.Value.Amount);
        Assert.Equal("Behind", GhostRace.Phrases(split.Value)[2]);
    }

    [Fact]
    public void A_fix_far_from_the_target_track_is_not_a_checkpoint()
    {
        var track = Line();
        var off = GeoMath.Translate(track[2].Latitude, track[2].Longitude, 90, 0.25);
        var split = GhostRace.Compare(track, off.Latitude, off.Longitude, TimeSpan.FromSeconds(20), TimeSpan.Zero, TimeSpan.Zero);

        Assert.Null(split);
    }

    [Fact]
    public void A_loop_uses_the_pass_closest_to_the_rider_clock()
    {
        var start = Line()[0];
        var track = new[]
        {
            start,
            Point(start.Latitude, start.Longitude, TimeSpan.FromSeconds(100))
        };
        var split = GhostRace.Compare(track, start.Latitude, start.Longitude, TimeSpan.FromSeconds(90), TimeSpan.Zero, TimeSpan.Zero);

        Assert.NotNull(split);
        Assert.True(split!.Value.Ahead);
        Assert.Equal(TimeSpan.FromSeconds(10), split.Value.Amount);
    }

    [Fact]
    public void A_goal_time_scales_the_target_clock()
    {
        var track = Line();
        var here = track[2];
        var split = GhostRace.Compare(
            track,
            here.Latitude,
            here.Longitude,
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(60));

        Assert.NotNull(split);
        Assert.False(split!.Value.Ahead);
        Assert.Equal(TimeSpan.FromSeconds(5), split.Value.Amount);
    }

    [Fact]
    public void The_checkpoint_period_gates_speech_and_off_silences_it()
    {
        var time = GhostRace.Schedule("Running", key => key.EndsWith("Mode", StringComparison.Ordinal) ? "Time" : "00:00:10");
        Assert.False(time.Due(TimeSpan.FromSeconds(9), 0, TimeSpan.Zero, 0));
        Assert.True(time.Due(TimeSpan.FromSeconds(10), 0, TimeSpan.Zero, 0));

        var distance = GhostRace.Schedule("Cycling", key => key.EndsWith("Mode", StringComparison.Ordinal) ? "Dist" : "1.0 mi");
        Assert.False(distance.Due(TimeSpan.FromHours(1), 1.0, TimeSpan.Zero, 0));
        Assert.True(distance.Due(TimeSpan.Zero, EditorText.MilesToKilos(1), TimeSpan.Zero, 0));

        var off = GhostRace.Schedule("Cycling", _ => "Off");
        Assert.False(off.Enabled);
        Assert.False(off.Due(TimeSpan.FromHours(1), 10, TimeSpan.Zero, 0));
    }

    private static TrackPoint[] Line()
    {
        var points = new TrackPoint[5];
        var lat = 40.0;
        var lon = -105.0;
        for (var i = 0; i < points.Length; i++)
        {
            points[i] = Point(lat, lon, TimeSpan.FromSeconds(i * 10));
            (lat, lon) = GeoMath.Translate(lat, lon, 0, 0.1);
        }
        return points;
    }

    private static TrackPoint Point(double latitude, double longitude, TimeSpan elapsed) => new()
    {
        Latitude = latitude,
        Longitude = longitude,
        TimestampTicks = elapsed.Ticks
    };
}
