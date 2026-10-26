namespace GpsTrainer;

/// <summary>
/// Compares a live fix with a stored target-race track. A negative split means the rider
/// reached this point sooner than the target, which 1.44 spoke as ahead.
/// </summary>
public readonly record struct GhostSplit(TimeSpan Amount, bool Ahead);

/// <summary>When the sport's checkpoint prompt is allowed to speak.</summary>
public readonly record struct GhostSchedule(bool Enabled, bool ByDistance, TimeSpan Period, double DistanceKm)
{
    public bool Due(TimeSpan elapsed, double kilometers, TimeSpan lastElapsed, double lastKilometers)
    {
        if (!Enabled)
            return false;
        if (ByDistance)
            return DistanceKm > 0 && kilometers - lastKilometers >= DistanceKm;
        return Period > TimeSpan.Zero && elapsed - lastElapsed >= Period;
    }
}

public static class GhostRace
{
    /// <summary>1.44 gave up on a checkpoint more than about 150 m off the target track.</summary>
    public const double MatchMeters = 150;

    public static GhostSchedule Schedule(string? sport, Func<string, string?> read)
    {
        var name = string.IsNullOrWhiteSpace(sport) ? "Cycling" : sport.Trim();
        var mode = read(name + "CheckPointMode");
        if (string.Equals(mode, "Off", StringComparison.OrdinalIgnoreCase))
            return new GhostSchedule(false, false, TimeSpan.FromSeconds(30), 0);
        var byDistance = string.Equals(mode, "Dist", StringComparison.OrdinalIgnoreCase);
        var period = TimeSpan.FromSeconds(30);
        if (TimeSpan.TryParse(read(name + "CheckPointPeriod"), out var parsed) && parsed > TimeSpan.Zero)
            period = parsed;
        var distance = EditorText.ParseDistanceKm(read(name + "CheckPointDistance") ?? "") ?? EditorText.MilesToKilos(1);
        return new GhostSchedule(true, byDistance, period, distance);
    }

    public static GhostSplit? Compare(
        IReadOnlyList<TrackPoint> target,
        double latitude,
        double longitude,
        TimeSpan riderElapsed,
        TimeSpan goalTime,
        TimeSpan targetElapsed)
    {
        if (target.Count == 0 || !double.IsFinite(latitude) || !double.IsFinite(longitude))
            return null;

        var ordered = target.OrderBy(point => point.TimestampTicks).ToList();
        var origin = ordered[0].TimestampTicks;
        TrackPoint? chosen = null;
        var chosenMeters = double.MaxValue;
        var chosenSkew = long.MaxValue;
        foreach (var point in ordered)
        {
            var meters = GeoMath.DistanceMeters(latitude, longitude, point.Latitude, point.Longitude);
            if (meters > MatchMeters)
                continue;
            var skew = Math.Abs((Elapsed(point, origin) - riderElapsed).Ticks);
            var closer = chosen is null || meters + 1 < chosenMeters;
            var samePlace = chosen is not null && Math.Abs(meters - chosenMeters) <= 20;
            if (closer || (samePlace && skew < chosenSkew))
            {
                chosen = point;
                chosenMeters = meters;
                chosenSkew = skew;
            }
        }

        if (chosen is null)
            return null;

        var ghostTicks = Math.Max(0, chosen.TimestampTicks - origin);
        if (goalTime.Ticks > 0 && targetElapsed.Ticks > 0)
            ghostTicks = ghostTicks * goalTime.Ticks / targetElapsed.Ticks;
        var signed = riderElapsed - TimeSpan.FromTicks(ghostTicks);
        return signed < TimeSpan.Zero
            ? new GhostSplit(signed.Negate(), true)
            : new GhostSplit(signed, false);
    }

    public static IReadOnlyList<string> Phrases(GhostSplit split) =>
        ["CheckPoint", Format(split.Amount), split.Ahead ? "ahead" : "Behind"];

    public static string Format(TimeSpan amount)
    {
        var span = amount.Duration();
        if (span.TotalHours >= 1)
            return $"{(int)span.TotalHours} hours {span.Minutes} minutes";
        if (span.TotalMinutes >= 1)
            return $"{(int)span.TotalMinutes} minutes {span.Seconds} seconds";
        return $"{(int)span.TotalSeconds} seconds";
    }

    private static TimeSpan Elapsed(TrackPoint point, long origin) =>
        TimeSpan.FromTicks(Math.Max(0, point.TimestampTicks - origin));
}
