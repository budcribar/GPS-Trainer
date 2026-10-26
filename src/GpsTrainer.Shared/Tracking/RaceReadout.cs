using System.Globalization;

namespace GpsTrainer;

/// <summary>
/// Large Time, Distance, Pace, and checkpoint figures from the 1.44 running screen.
/// English units (miles and mph) are the default. A sport whose units preference is
/// Metric uses kilometers and kph. A sport whose speed preference is Pace uses
/// minutes per mile or per kilometer.
/// </summary>
public readonly record struct RaceReadout(
    string Time,
    string TimeUnit,
    string DistanceValue,
    string DistanceUnit,
    string Pace,
    string PaceUnit,
    string Checkpoint,
    string CheckpointUnit)
{
    public string DistanceText => DistanceValue + " " + DistanceUnit;

    public string PaceText => Pace + " " + PaceUnit;

    public static RaceReadout Create(
        TimeSpan elapsed,
        double kilometers,
        double? speedMetersPerSecond,
        bool metric,
        bool showSpeed,
        GhostSplit? checkpoint)
    {
        var time = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
        var timeText = $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";

        var distance = metric
            ? Distance.FromKilometers(kilometers)
            : Distance.FromKilometers(kilometers).ToStatuteMiles();
        var distanceText = distance.Value.ToString("0.00", CultureInfo.InvariantCulture);
        var distanceUnit = metric ? "km" : "mi";

        var speed = speedMetersPerSecond is > 0
            ? Speed.FromMetersPerSecond(speedMetersPerSecond.Value)
            : Speed.AtRest;
        string paceText;
        string paceUnit;
        if (showSpeed)
        {
            var shown = metric ? speed.ToKilometersPerHour() : speed.ToStatuteMilesPerHour();
            paceText = shown.Value.ToString("0.0", CultureInfo.InvariantCulture);
            paceUnit = metric ? "kph" : "mph";
        }
        else
        {
            var pace = new Pace(metric ? speed.ToKilometersPerHour() : speed.ToStatuteMilesPerHour());
            paceText = pace.ToStringWithoutUnits();
            paceUnit = pace.UnitsLabel;
        }

        var checkpointText = "00:00:00";
        var checkpointUnit = "sec";
        if (checkpoint is GhostSplit split)
        {
            var amount = split.Amount < TimeSpan.Zero ? TimeSpan.Zero : split.Amount;
            checkpointText = $"{(int)amount.TotalHours:00}:{amount.Minutes:00}:{amount.Seconds:00}";
            checkpointUnit = split.Ahead ? "ahead" : "behind";
        }

        return new RaceReadout(timeText, "sec", distanceText, distanceUnit, paceText, paceUnit, checkpointText, checkpointUnit);
    }
}

public static class SportUnits
{
    public const string English = "English";
    public const string Metric = "Metric";

    public static string UnitsKey(string? sport) =>
        (string.IsNullOrWhiteSpace(sport) ? "Other" : sport.Trim()) + "Units";

    public static string SpeedKey(string? sport) =>
        (string.IsNullOrWhiteSpace(sport) ? "Cycling" : sport.Trim()) + "Speed";

    /// <summary>Missing or English stays on miles. Only the word Metric switches.</summary>
    public static bool IsMetric(string? sport, Func<string, string?> read) =>
        string.Equals(read(UnitsKey(sport))?.Trim(), Metric, StringComparison.OrdinalIgnoreCase);

    /// <summary>mph unless the sport's speed preference is Pace.</summary>
    public static bool ShowSpeed(string? sport, Func<string, string?> read) =>
        !string.Equals(read(SpeedKey(sport))?.Trim(), "Pace", StringComparison.OrdinalIgnoreCase);
}
