namespace GpsTrainer;

public enum SpeedUnit
{
    Knots,
    StatuteMilesPerHour,
    KilometersPerHour,
    KilometersPerSecond,
    FeetPerSecond,
    MetersPerSecond
}

/// <summary>
/// Port of <c>Speed</c>. <c>Equals(Speed)</c> in the APK compared the boxed <c>Double</c>
/// references (<c>if-ne</c>), so only the <see cref="AtRest"/> singleton compared equal to itself
/// when the countdown thread decided whether to tick. Callers that need numeric equality
/// should use <see cref="NumericallyEqual"/>.
/// </summary>
public sealed class Speed
{
    public static readonly Speed Empty = new(0, SpeedUnit.MetersPerSecond);
    public static readonly Speed AtRest = new(0, SpeedUnit.MetersPerSecond);
    public static readonly Speed SpeedOfLight = new(2.99792458E8, SpeedUnit.MetersPerSecond);
    public static readonly Speed SpeedOfSoundAtSeaLevel = new(340.29, SpeedUnit.MetersPerSecond);
    public static readonly Speed Infinity = new(double.PositiveInfinity, SpeedUnit.MetersPerSecond);
    public static readonly Speed Invalid = new(double.NaN, SpeedUnit.KilometersPerSecond);

    public double Value { get; }
    public SpeedUnit Unit { get; }

    public Speed(double value, SpeedUnit unit)
    {
        Value = value;
        Unit = unit;
    }

    public static Speed FromMetersPerSecond(double value) => new(value, SpeedUnit.MetersPerSecond);
    public static Speed FromKilometersPerHour(double value) => new(value, SpeedUnit.KilometersPerHour);
    public static Speed FromStatuteMilesPerHour(double value) => new(value, SpeedUnit.StatuteMilesPerHour);

    public bool IsEmpty => Value == 0.0;
    public bool IsInfinity => double.IsInfinity(Value);

    public bool IsMetric => Unit is SpeedUnit.KilometersPerHour or SpeedUnit.KilometersPerSecond or SpeedUnit.MetersPerSecond;

    /// <summary>Faithful to the smali reference compare against <see cref="AtRest"/>.</summary>
    public bool IsAtRestSingleton => ReferenceEquals(this, AtRest);

    public double ToMetersPerSecond() => Unit switch
    {
        SpeedUnit.StatuteMilesPerHour => Value * 0.447d,
        SpeedUnit.KilometersPerHour => Value * 0.2777778d,
        SpeedUnit.KilometersPerSecond => Value * 1000.0d,
        SpeedUnit.FeetPerSecond => Value * 0.3048d,
        SpeedUnit.MetersPerSecond => Value,
        SpeedUnit.Knots => Value * 0.5144444d,
        _ => 0
    };

    public Speed ToKilometersPerHour() => Unit switch
    {
        SpeedUnit.StatuteMilesPerHour => new(Value * 1.609344d, SpeedUnit.KilometersPerHour),
        SpeedUnit.KilometersPerHour => this,
        SpeedUnit.FeetPerSecond => new(Value * 1.09728d, SpeedUnit.KilometersPerHour),
        SpeedUnit.MetersPerSecond => new(Value * 3.6d, SpeedUnit.KilometersPerHour),
        SpeedUnit.Knots => new(Value * 1.852d, SpeedUnit.KilometersPerHour),
        SpeedUnit.KilometersPerSecond => new(Value * 3600.0d, SpeedUnit.KilometersPerHour),
        _ => Empty
    };

    public Speed ToStatuteMilesPerHour() => Unit switch
    {
        SpeedUnit.StatuteMilesPerHour => this,
        SpeedUnit.KilometersPerHour => new(Value * 0.6213712d, SpeedUnit.StatuteMilesPerHour),
        SpeedUnit.KilometersPerSecond => new(Value * 2236.936d, SpeedUnit.StatuteMilesPerHour),
        SpeedUnit.FeetPerSecond => new(Value * 0.6818182d, SpeedUnit.StatuteMilesPerHour),
        SpeedUnit.MetersPerSecond => new(Value * 2.236936d, SpeedUnit.StatuteMilesPerHour),
        SpeedUnit.Knots => new(Value * 1.150779d, SpeedUnit.StatuteMilesPerHour),
        _ => Empty
    };

    public Speed ToKilometersPerSecond() => Unit switch
    {
        SpeedUnit.StatuteMilesPerHour => new(Value * 4.47E-4d, SpeedUnit.KilometersPerSecond),
        SpeedUnit.KilometersPerHour => new(Value * 2.777778E-4d, SpeedUnit.KilometersPerSecond),
        SpeedUnit.KilometersPerSecond => this,
        SpeedUnit.FeetPerSecond => new(Value * 3.048E-4d, SpeedUnit.KilometersPerSecond),
        SpeedUnit.MetersPerSecond => new(Value * 0.001d, SpeedUnit.KilometersPerSecond),
        SpeedUnit.Knots => new(Value * 5.144444E-4d, SpeedUnit.KilometersPerSecond),
        _ => Empty
    };

    public bool NumericallyEqual(Speed other) => ToMetersPerSecond() == other.ToMetersPerSecond();

    public override string ToString() => $"{Value.ToString(System.Globalization.CultureInfo.InvariantCulture)} {Unit}";
}
