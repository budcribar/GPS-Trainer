using System.Globalization;

namespace GpsTrainer;

/// <summary>
/// Port of <c>com.peakSwc.gpsTrainer.Distance</c>. Values stay in the unit they were
/// created with. Conversions go through meters using the original <c>ToMeters</c> factors
/// so a round trip is exact. The decompiled pairwise matrix used rounded reciprocals
/// (for example FeetPerMeter = 3.2808399).
/// </summary>
public sealed class Distance : IComparable<Distance>
{
    public const double MetersPerKilometer = 1000.0;
    public const double MetersPerStatuteMile = 1609.344;
    public const double MetersPerCentimeter = 0.01;
    public const double MetersPerFoot = 0.3048;
    public const double MetersPerInch = 0.0254;
    public const double MetersPerNauticalMile = 1852.0;

    public static readonly Distance EarthsEquatorialRadiusWgs1984 = new(6378137.0, DistanceUnit.Meters);
    public static readonly Distance EarthsPolarRadiusWgs1984 = new(6356752.314245, DistanceUnit.Meters);
    public static readonly Distance EarthsAverageRadius = new(6378100.0, DistanceUnit.Meters);
    public static readonly Distance Infinity = new(double.PositiveInfinity, DistanceUnit.Meters);
    public static readonly Distance Invalid = new(double.NaN, DistanceUnit.Kilometers);

    /// <summary>
    /// Original <c>Empty</c> is <c>new Distance(0, Meters).ToLocalUnitType()</c>.
    /// <c>ToLocalUnitType</c> always called <c>ToImperialUnitType</c>, and a zero value
    /// falls through miles → feet → inches → centimeters.
    /// </summary>
    public static readonly Distance Empty = new(0, DistanceUnit.Centimeters);

    public static readonly Distance SeaLevel = Empty;
    public static readonly Distance Maximum = new Distance(double.MaxValue, DistanceUnit.Kilometers).ToImperial();
    public static readonly Distance Minimum = new Distance(double.Epsilon, DistanceUnit.Kilometers).ToImperial();

    public double Value { get; }
    public DistanceUnit Unit { get; }

    public Distance(double value, DistanceUnit unit)
    {
        Value = value;
        Unit = unit;
    }

    public static Distance FromKilometers(double value) => new(value, DistanceUnit.Kilometers);
    public static Distance FromMeters(double value) => new(value, DistanceUnit.Meters);
    public static Distance FromStatuteMiles(double value) => new(value, DistanceUnit.StatuteMiles);
    public static Distance FromFeet(double value) => new(value, DistanceUnit.Feet);
    public static Distance FromNauticalMiles(double value) => new(value, DistanceUnit.NauticalMiles);
    public static Distance FromCentimeters(double value) => new(value, DistanceUnit.Centimeters);
    public static Distance FromInches(double value) => new(value, DistanceUnit.Inches);

    public bool IsInvalid => double.IsNaN(Value);
    public bool IsEmpty => Value == 0.0;
    public bool IsInfinity => double.IsInfinity(Value);

    public bool IsMetric => Unit is DistanceUnit.Centimeters or DistanceUnit.Meters or DistanceUnit.Kilometers;

    public double ToMeters() => Unit switch
    {
        DistanceUnit.Meters => Value,
        DistanceUnit.Kilometers => Value * MetersPerKilometer,
        DistanceUnit.StatuteMiles => Value * MetersPerStatuteMile,
        DistanceUnit.Centimeters => Value * MetersPerCentimeter,
        DistanceUnit.Feet => Value * MetersPerFoot,
        DistanceUnit.Inches => Value * MetersPerInch,
        DistanceUnit.NauticalMiles => Value * MetersPerNauticalMile,
        _ => 0
    };

    public Distance ToUnit(DistanceUnit unit)
    {
        if (unit == Unit)
            return this;
        if (IsInvalid)
            return new Distance(double.NaN, unit);
        var meters = ToMeters();
        var converted = unit switch
        {
            DistanceUnit.Meters => meters,
            DistanceUnit.Kilometers => meters / MetersPerKilometer,
            DistanceUnit.StatuteMiles => meters / MetersPerStatuteMile,
            DistanceUnit.Centimeters => meters / MetersPerCentimeter,
            DistanceUnit.Feet => meters / MetersPerFoot,
            DistanceUnit.Inches => meters / MetersPerInch,
            DistanceUnit.NauticalMiles => meters / MetersPerNauticalMile,
            _ => 0
        };
        return new Distance(converted, unit);
    }

    public Distance ToKilometers() => ToUnit(DistanceUnit.Kilometers);
    public Distance ToStatuteMiles() => ToUnit(DistanceUnit.StatuteMiles);
    public Distance ToFeet() => ToUnit(DistanceUnit.Feet);
    public Distance ToInches() => ToUnit(DistanceUnit.Inches);
    public Distance ToCentimeters() => ToUnit(DistanceUnit.Centimeters);
    public Distance ToNauticalMiles() => ToUnit(DistanceUnit.NauticalMiles);

    /// <summary>Original <c>ToImperialUnitType</c>: miles, then feet under 1, then inches under 1, else centimeters.</summary>
    public Distance ToImperial()
    {
        var temp = ToStatuteMiles();
        if (Math.Abs(temp.Value) < 1.0)
            temp = temp.ToFeet();
        if (Math.Abs(temp.Value) < 1.0)
            temp = temp.ToInches();
        if (Math.Abs(temp.Value) < 1.0)
            return temp.ToCentimeters();
        return temp;
    }

    public Distance ToMetric()
    {
        var temp = ToKilometers();
        if (Math.Abs(temp.Value) < 1.0)
            temp = ToUnit(DistanceUnit.Meters);
        if (Math.Abs(temp.Value) < 1.0)
            return temp.ToCentimeters();
        return temp;
    }

    public Distance Add(Distance other) => new(Value + other.ToUnit(Unit).Value, Unit);
    public Distance Subtract(Distance other) => new(Value - other.ToUnit(Unit).Value, Unit);
    public Distance Multiply(double factor) => new(Value * factor, Unit);

    public bool GreaterThan(Distance other) => ToMeters() > other.ToMeters();
    public bool GreaterThanOrEqual(Distance other) => ToMeters() >= other.ToMeters();

    /// <summary>Original <c>equals(Distance)</c> uses an absolute tolerance of 1e-4 in this instance's unit.</summary>
    public bool ApproximatelyEquals(Distance other) =>
        Math.Abs(Value - other.ToUnit(Unit).Value) < 1e-4;

    public int CompareTo(Distance? other)
    {
        if (other is null)
            return 1;
        return ToMeters().CompareTo(other.ToMeters());
    }

    public override string ToString() => Format(null);

    /// <summary>
    /// Clean rewrite of <c>Distance.toString(Context, String)</c>, which jadx left as goto-style
    /// code. See <see cref="FormatCore"/>.
    /// </summary>
    public string Format(string? format, string groupSeparator = ",", string decimalSeparator = ".") =>
        FormatCore(Value, Unit, format, groupSeparator, decimalSeparator);

    public static string FormatCore(double value, DistanceUnit unit, string? format, string groupSeparator = ",", string decimalSeparator = ".")
    {
        try
        {
            if (string.IsNullOrEmpty(format))
                format = "G";
            if (string.Equals(format, "G", StringComparison.OrdinalIgnoreCase))
                format = "#" + groupSeparator + "##0.00 uu";

            format = format.ToUpper(CultureInfo.CurrentCulture);
            format = format.Replace("V", "0", StringComparison.Ordinal);

            var formatted = ApplyNumericPattern(value, format, groupSeparator, decimalSeparator);
            return ApplyUnitTokens(formatted, value, unit);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Substitutes the first run of U/UU/UUU. Nautical miles are intentionally left
    /// unchanged: the original switch broke without a replacement.
    /// </summary>
    public static string ApplyUnitTokens(string format, double value, DistanceUnit unit)
    {
        var start = format.IndexOf('U');
        if (start < 0)
            return format;
        var end = format.LastIndexOf('U');
        var token = format.Substring(start, end - start + 1);
        // Only a contiguous U-run is a unit token. A later U elsewhere still uses lastIndex in the original.
        if (token.Any(c => c != 'U'))
            token = format.Substring(start, format.Skip(start).TakeWhile(c => c == 'U').Count());

        var replacement = token.Length switch
        {
            1 => unit switch
            {
                DistanceUnit.Centimeters => "cm",
                DistanceUnit.Feet => "'",
                DistanceUnit.Inches => "\"",
                DistanceUnit.Kilometers => "km",
                DistanceUnit.Meters => "m",
                DistanceUnit.StatuteMiles => "mi",
                _ => null
            },
            2 => unit switch
            {
                DistanceUnit.Centimeters => "cm",
                DistanceUnit.Feet => "ft",
                DistanceUnit.Inches => "in",
                DistanceUnit.Kilometers => "km",
                DistanceUnit.Meters => "m",
                DistanceUnit.StatuteMiles => "mi",
                _ => null
            },
            3 => value == 1.0
                ? unit switch
                {
                    DistanceUnit.Centimeters => "centimeter",
                    DistanceUnit.Feet => "foot",
                    DistanceUnit.Inches => "inch",
                    DistanceUnit.Kilometers => "kilometer",
                    DistanceUnit.Meters => "meter",
                    DistanceUnit.StatuteMiles => "mile",
                    _ => null
                }
                : unit switch
                {
                    DistanceUnit.Centimeters => "centimeters",
                    DistanceUnit.Feet => "feet",
                    DistanceUnit.Inches => "inches",
                    DistanceUnit.Kilometers => "kilometers",
                    DistanceUnit.Meters => "meters",
                    DistanceUnit.StatuteMiles => "miles",
                    _ => null
                },
            _ => null
        };

        if (replacement is null)
            return format;
        return format.Remove(start, token.Length).Insert(start, replacement);
    }

    private static string ApplyNumericPattern(double value, string pattern, string groupSeparator, string decimalSeparator)
    {
        var numericStart = -1;
        var numericEnd = -1;
        for (var i = 0; i < pattern.Length; i++)
        {
            if (IsNumericChar(pattern[i]))
            {
                if (numericStart < 0)
                    numericStart = i;
                numericEnd = i;
            }
            else if (numericStart >= 0 && pattern[i] != 'U')
            {
                // stop the numeric run at the first non-numeric, non-U. U is a suffix token.
                break;
            }
        }

        if (numericStart < 0)
            throw new FormatException("Invalid distance format");

        // Extend through a contiguous numeric pattern only.
        numericEnd = numericStart;
        while (numericEnd < pattern.Length && IsNumericChar(pattern[numericEnd]))
            numericEnd++;

        var numeric = pattern[numericStart..numericEnd];
        if (numeric.Count(c => c == '.') > 1)
            throw new FormatException("Invalid distance format");

        var dot = numeric.IndexOf('.');
        var decimals = dot < 0 ? 0 : numeric.Length - dot - 1;
        var grouping = numeric.Contains(',');

        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberGroupSeparator = groupSeparator;
        culture.NumberFormat.NumberDecimalSeparator = decimalSeparator;
        culture.NumberFormat.NumberDecimalDigits = decimals;

        var body = grouping
            ? value.ToString("N" + decimals.ToString(CultureInfo.InvariantCulture), culture)
            : value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), culture);

        return pattern[..numericStart] + body + pattern[numericEnd..];
    }

    private static bool IsNumericChar(char c) => c is '#' or '0' or '.' or ',';
}
