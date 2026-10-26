using System.Globalization;

namespace GpsTrainer;

public enum PaceUnit
{
    MinPerMile,
    MinPerKilo
}

/// <summary>Port of <c>Pace</c>. A zero speed speaks as 59:59, the original <c>MAX_PACE</c>.</summary>
public sealed class Pace
{
    private static readonly TimeSpan MaxPace = new(0, 59, 59);
    private readonly Speed _speed;

    public Pace(Speed speed)
    {
        _speed = speed.Unit is SpeedUnit.FeetPerSecond or SpeedUnit.StatuteMilesPerHour
            ? speed.ToStatuteMilesPerHour()
            : speed.ToKilometersPerHour();
    }

    public PaceUnit Unit => _speed.Unit == SpeedUnit.KilometersPerHour ? PaceUnit.MinPerKilo : PaceUnit.MinPerMile;

    public bool AtRest => _speed.IsEmpty;

    public TimeSpan ToTimeSpan()
    {
        if (_speed.Value == 0)
            return MaxPace;
        var minutes = Math.Min((int)MinutesPerUnit(), 59);
        var seconds = (int)Math.Round((MinutesPerUnit() - (int)MinutesPerUnit()) * 60.0, MidpointRounding.AwayFromZero);
        if (seconds == 60)
        {
            seconds = 0;
            minutes = Math.Min(minutes + 1, 59);
        }
        return new TimeSpan(0, minutes, seconds);
    }

    public string UnitsLabel => Unit == PaceUnit.MinPerKilo ? "mpk" : "mpm";

    public override string ToString() => ToStringWithoutUnits() + UnitsLabel;

    public string ToStringWithoutUnits()
    {
        var ts = ToTimeSpan();
        var text = ts.Minutes.ToString("00", CultureInfo.InvariantCulture)
            + ":"
            + ts.Seconds.ToString("00", CultureInfo.InvariantCulture);
        if (ts.Hours > 1)
            text = ts.Hours.ToString("00", CultureInfo.InvariantCulture) + ":" + text;
        return text;
    }

    private double MinutesPerUnit() => _speed.Value == 0 ? double.PositiveInfinity : 60.0 / _speed.Value;
}
