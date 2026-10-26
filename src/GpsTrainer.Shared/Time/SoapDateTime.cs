using System.Globalization;
using System.Text.RegularExpressions;

namespace GpsTrainer;

/// <summary>
/// The Android <c>DateTime</c> class stored Unix epoch milliseconds times 10,000
/// (<c>TimeSpan.TicksPerMillisecond</c>), not .NET <see cref="DateTime.Ticks"/> from year 1.
/// SOAP strings follow <c>yyyy-MM-dd'T'HH:mm:ss.fff±HH:mm</c> (Java's <c>Z</c> pattern wrote
/// <c>±HHmm</c> and the client inserted a colon before sending).
/// </summary>
public static partial class SoapDateTime
{
    public const long TicksPerMillisecond = 10_000;

    public static long ToUnixTicks(DateTime utc)
    {
        var ms = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        return ms * TicksPerMillisecond;
    }

    public static DateTime FromUnixTicks(long ticks)
    {
        var ms = ticks / TicksPerMillisecond;
        return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
    }

    public static string ToSoapString(DateTime value)
    {
        var kind = value.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : value.Kind;
        var dto = new DateTimeOffset(DateTime.SpecifyKind(value, kind));
        return dto.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
    }

    public static DateTime Parse(string? datestring)
    {
        if (string.IsNullOrWhiteSpace(datestring))
            return DateTime.UnixEpoch;

        var text = datestring.Trim();
        text = FractionPattern().Replace(text, match =>
        {
            var fraction = match.Groups[1].Value;
            if (fraction.Length > 3)
                fraction = fraction[..3];
            else if (fraction.Length == 1)
                fraction += "00";
            else if (fraction.Length == 2)
                fraction += "0";
            return "." + fraction;
        });

        if (OffsetWithoutColon().IsMatch(text))
            text = text.Insert(text.Length - 2, ":");

        if (!text.EndsWith('Z') && !text.Contains('+') && text.LastIndexOf('-') < 10)
            text += "Z";

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var dto))
            return dto.UtcDateTime;

        return DateTime.UnixEpoch;
    }

    [GeneratedRegex(@"\.(\d+)")]
    private static partial Regex FractionPattern();

    [GeneratedRegex(@"[+-]\d{4}$")]
    private static partial Regex OffsetWithoutColon();
}
