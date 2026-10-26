using System.Globalization;

namespace GpsTrainer;

/// <summary>
/// The 2013 editor opened in English (miles and feet). Metric was the other choice.
/// The browser remembers the choice, and a signed-in login keeps its own value.
/// </summary>
public static class ElevationUnits
{
    public const bool MetricByDefault = false;
    public const string English = "english";
    public const string Metric = "metric";
    public const string GroupLabel = "Elevation units";
    public const string EnglishLabel = "English (mi/ft)";
    public const string MetricLabel = "Metric (km/m)";

    public static string StorageKey(string? login) =>
        string.IsNullOrWhiteSpace(login)
            ? "gpstrainer.elevationUnits"
            : "gpstrainer.elevationUnits." + login.Trim();

    public static bool IsMetric(string? stored) =>
        string.Equals(stored?.Trim(), Metric, StringComparison.OrdinalIgnoreCase);

    /// <summary>The 2013 button shows the unit you switch to: "mi" while metric, "km" while English.</summary>
    public static string SwitchLabel(bool metric) => metric ? "mi" : "km";

    public static string FormatDistance(double lengthKm, bool metric)
    {
        var value = metric ? lengthKm : EditorText.KilosToMiles(lengthKm);
        var unit = metric ? "km" : "mi";
        return value.ToString("0.00", CultureInfo.InvariantCulture) + " " + unit;
    }
}
