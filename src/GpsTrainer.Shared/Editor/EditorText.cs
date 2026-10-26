using System.Globalization;
using System.Text.RegularExpressions;

namespace GpsTrainer;

/// <summary>
/// Ports of the CourseEditor helpers in <c>String.ts</c>, <c>Number.ts</c>, <c>Common.ts</c>, and <c>TimeSpan.ts</c>.
/// </summary>
public static class EditorText
{
    public static bool IsEmpty(string? value) => string.IsNullOrWhiteSpace(value);

    public static bool IsNumeric(string value)
    {
        if (value.Length == 0)
            return false;
        const string valid = "0123456789.";
        foreach (var ch in value)
        {
            if (valid.IndexOf(ch) < 0)
                return false;
        }
        return true;
    }

    public static bool EndsWithSuffix(string value, string suffix)
    {
        if (suffix.Length == 0)
            return false;
        return value.IndexOf(suffix, Math.Max(0, value.Length - suffix.Length), StringComparison.Ordinal) >= 0
            && value.EndsWith(suffix, StringComparison.Ordinal);
    }

    public static string StripHtml(string value) => Regex.Replace(value, "<(?:.|\n)*?>", "");

    public static string Format(string template, params object?[] args)
    {
        var formatted = template;
        for (var i = 0; i < args.Length; i++)
            formatted = Regex.Replace(formatted, "\\{" + i + "\\}", args[i]?.ToString() ?? "", RegexOptions.IgnoreCase);
        return formatted;
    }

    public static double? ParseDistanceKm(string text)
    {
        var value = text.Trim();
        if (EndsWithSuffix(value, "km"))
            return double.Parse(value.Replace("km", "", StringComparison.Ordinal).Trim(), CultureInfo.InvariantCulture);
        if (EndsWithSuffix(value, "mi"))
            return MilesToKilos(double.Parse(value.Replace("mi", "", StringComparison.Ordinal).Trim(), CultureInfo.InvariantCulture));
        if (EndsWithSuffix(value, "ft"))
            return FeetToKilos(double.Parse(value.Replace("ft", "", StringComparison.Ordinal).Trim(), CultureInfo.InvariantCulture));
        if (EndsWithSuffix(value, "m"))
            return MetersToKilos(double.Parse(value.Replace("m", "", StringComparison.Ordinal).Trim(), CultureInfo.InvariantCulture));
        return null;
    }

    public static double MetersToKilos(double meters) => meters / 1000.0;
    public static double FeetToKilos(double feet) => feet / 3280.8399;
    public static double MilesToKilos(double miles) => miles / 0.621371192;
    public static double KilosToMiles(double kilos) => kilos * 0.621371192;

    public static string Pad(int value, int size)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);
        while (text.Length < size)
            text = "0" + text;
        return text;
    }

    public static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
    public static double ToDegrees(double radians) => radians * 180.0 / Math.PI;
}

/// <summary><c>TimeSpan.ts</c>. Ticks are 100 ns. Hours above 24 stay in the hour field.</summary>
public readonly struct EditorTimeSpan
{
    public const long TicksPerMillisecond = 10_000;
    public const long TicksPerSecond = TicksPerMillisecond * 1000;

    public long Ticks { get; }

    public EditorTimeSpan(long ticks) => Ticks = ticks;

    public double TotalHours() => Ticks / (60.0 * 60.0 * TicksPerSecond);

    public static EditorTimeSpan FromHours(double hours) => new((long)(60 * 60 * TicksPerSecond * hours));

    public static EditorTimeSpan Parse(string text)
    {
        var parts = text.Split(':');
        if (parts.Length == 3)
            return new((long.Parse(parts[0], CultureInfo.InvariantCulture) * 3600
                + long.Parse(parts[1], CultureInfo.InvariantCulture) * 60
                + long.Parse(parts[2], CultureInfo.InvariantCulture)) * TicksPerSecond);
        if (parts.Length == 2)
            return new((long.Parse(parts[0], CultureInfo.InvariantCulture) * 60
                + long.Parse(parts[1], CultureInfo.InvariantCulture)) * TicksPerSecond);
        return new(0);
    }

    public override string ToString()
    {
        var hours = (long)Math.Floor(TotalHours());
        var remainder = TimeSpan.FromTicks(Ticks - hours * 3600 * TicksPerSecond);
        return EditorText.Pad((int)hours, 2) + ":" + remainder.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }
}
