namespace GpsTrainer;

/// <summary>Port of <c>Altitude</c>. Unknown altitude is -10000 m, uploaded in place of the real value.</summary>
public sealed class Altitude
{
    public static readonly Altitude Unknown = new(-10.0);

    public double Kilometers { get; }

    public Altitude(double kilometers) => Kilometers = kilometers;

    public static Altitude FromKilometers(double km) => new(km);
    public static Altitude FromMeters(double meters) => new(meters / 1000.0);

    public double ToMeters() => Kilometers * 1000.0;

    public override string ToString() => Kilometers.ToString(System.Globalization.CultureInfo.InvariantCulture) + " km";
}
