namespace GpsTrainer;

/// <summary>
/// Sport audio periods and waypoint every/for distances from <see cref="PreferenceCatalog"/>.
/// A blank stored value uses the catalog default. Checkpoint comparison stays with the ghost race.
/// </summary>
public static class AudioPrompts
{
    public sealed record Plan(string Kind, string Mode, double DistanceKm, TimeSpan Period);

    public sealed record Approach(string Mode, double EveryKm, double ForKm);

    public readonly record struct Clock(bool Started, TimeSpan Mark, double Kilometers);

    public readonly record struct Tick(bool Speak, Clock Clock);

    private static readonly string[] Kinds = ["CheckPoint", "AltGain", "Pace", "Time", "Distance"];

    private static readonly Dictionary<string, string> AudioDefaults =
        PreferenceCatalog.AudioPrompts().ToDictionary(item => item.Key, item => item.DefaultValue);

    private static readonly Dictionary<string, string> WaypointDefaults =
        PreferenceCatalog.WaypointPrompts().ToDictionary(item => item.Key, item => item.DefaultValue);

    public static IReadOnlyList<Plan> ForSport(string? sport, Func<string, string?> read)
    {
        var name = string.IsNullOrWhiteSpace(sport) ? "Cycling" : sport.Trim();
        var plans = new List<Plan>(Kinds.Length);
        foreach (var kind in Kinds)
        {
            var prefix = name + kind;
            var mode = Value(read, prefix + "Mode", AudioDefaults, "Off");
            var distance = ParseKm(Value(read, prefix + "Distance", AudioDefaults, "1.0 mi"));
            var period = ParsePeriod(Value(read, prefix + "Period", AudioDefaults, kind == "CheckPoint" ? "00:00:30" : "00:10:00"));
            plans.Add(new Plan(kind, mode, distance, period));
        }
        return plans;
    }

    public static Approach ForWaypoint(string typeKey, Func<string, string?> read)
    {
        var mode = Value(read, typeKey + "Mode", WaypointDefaults, "Off");
        var every = ParseKm(Value(read, typeKey + "Every", WaypointDefaults, "0"));
        var window = ParseKm(Value(read, typeKey + "For", WaypointDefaults, "0"));
        return new Approach(mode, every, window);
    }

    public static Tick Advance(Plan plan, TimeSpan elapsed, double kilometers, Clock clock)
    {
        if (plan.Mode.Equals("Time", StringComparison.OrdinalIgnoreCase))
            return AdvanceTime(plan.Period, elapsed, kilometers, clock);
        if (plan.Mode.Equals("Dist", StringComparison.OrdinalIgnoreCase))
            return AdvanceDistance(plan.DistanceKm, elapsed, kilometers, clock);
        return new Tick(false, clock);
    }

    /// <summary>
    /// Speak while the remaining distance is inside the For window, once per Every step.
    /// Finish is spoken even when its mode is Off. Arrival is the last 40 m.
    /// </summary>
    public static bool ApproachDue(Approach prompt, bool always, double remainingKm, double? lastRemainingKm)
    {
        var enabled = always || !prompt.Mode.Equals("Off", StringComparison.OrdinalIgnoreCase);
        if (!enabled || remainingKm < 0 || double.IsNaN(remainingKm))
            return false;
        const double arrivalKm = 0.04;
        if (remainingKm <= arrivalKm)
            return lastRemainingKm is null || lastRemainingKm > arrivalKm;
        if (prompt.EveryKm <= 0)
            return false;
        var window = prompt.ForKm + 0.001;
        if (remainingKm > window)
            return false;
        var bucket = Math.Floor(remainingKm / prompt.EveryKm);
        var lastBucket = lastRemainingKm is null || lastRemainingKm > window
            ? double.PositiveInfinity
            : Math.Floor(lastRemainingKm.Value / prompt.EveryKm);
        return bucket < lastBucket;
    }

    /// <summary>
    /// Distance-to-start speech. The first fix inside the window only arms the tracker.
    /// Inside 600 m the every-step is at most 100 m. Inside 50 m it stays quiet.
    /// </summary>
    public static bool StagingDue(Approach prompt, double distanceKm, double? lastDistanceKm, out double? spokenKm)
    {
        spokenKm = lastDistanceKm;
        if (prompt.Mode.Equals("Off", StringComparison.OrdinalIgnoreCase) || distanceKm <= 0.05 || double.IsNaN(distanceKm))
            return false;
        if (distanceKm > prompt.ForKm + 0.01)
            return false;
        var every = distanceKm <= 0.6 && prompt.EveryKm > 0.1 ? 0.1 : prompt.EveryKm;
        if (every <= 0)
            return false;
        if (lastDistanceKm is null)
        {
            spokenKm = distanceKm;
            return false;
        }
        if (Math.Abs(distanceKm - lastDistanceKm.Value) >= every - 1e-9)
        {
            spokenKm = distanceKm;
            return true;
        }
        return false;
    }

    public static string DistanceToStart(double? latitude, double? longitude, double startLatitude, double startLongitude)
    {
        if (latitude is not double lat || longitude is not double lon || !double.IsFinite(lat) || !double.IsFinite(lon))
            return "Waiting for GPS";
        var kilometers = GeoMath.DistanceMeters(lat, lon, startLatitude, startLongitude) / 1000.0;
        return Distance.FromKilometers(kilometers).Format(null);
    }

    private static Tick AdvanceTime(TimeSpan period, TimeSpan elapsed, double kilometers, Clock clock)
    {
        if (period <= TimeSpan.Zero)
            return new Tick(false, clock);
        if (!clock.Started)
            return new Tick(false, new Clock(true, elapsed, kilometers));
        var roundup = TimeSpan.FromMilliseconds(500);
        if (elapsed - clock.Mark + roundup < period)
            return new Tick(false, clock);
        var mark = clock.Mark;
        while (elapsed - mark + roundup >= period)
            mark += period;
        return new Tick(true, new Clock(true, mark, kilometers));
    }

    private static Tick AdvanceDistance(double intervalKm, TimeSpan elapsed, double kilometers, Clock clock)
    {
        if (intervalKm <= 0 || double.IsNaN(kilometers))
            return new Tick(false, clock);
        if (!clock.Started)
        {
            var floored = Math.Floor(kilometers / intervalKm) * intervalKm;
            return new Tick(false, new Clock(true, elapsed, floored));
        }
        if (kilometers - clock.Kilometers < intervalKm)
            return new Tick(false, clock);
        var steps = Math.Round(kilometers / intervalKm, MidpointRounding.AwayFromZero);
        return new Tick(true, new Clock(true, elapsed, intervalKm * steps));
    }

    private static string Value(Func<string, string?> read, string key, IReadOnlyDictionary<string, string> defaults, string fallback)
    {
        var stored = read(key);
        if (!string.IsNullOrWhiteSpace(stored))
            return stored.Trim();
        return defaults.TryGetValue(key, out var catalog) ? catalog : fallback;
    }

    private static double ParseKm(string text) => EditorText.ParseDistanceKm(text) ?? 0;

    private static TimeSpan ParsePeriod(string text) =>
        TimeSpan.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var period) ? period : TimeSpan.Zero;
}
