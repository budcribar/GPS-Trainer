using System.Globalization;

namespace GpsTrainer;

public enum GpsInfoBlocker
{
    None,
    PermissionDenied,
    LocationOff
}

/// <summary>
/// One location sample for the GPS Info page. Speed and altitude are meters.
/// Satellite counts are zero when the provider does not report them.
/// </summary>
public sealed record GpsInfoSample(
    double Latitude,
    double Longitude,
    double? AltitudeMeters,
    double? SpeedMetersPerSecond,
    double? AccuracyMeters,
    int SatellitesInView,
    int SatellitesUsed,
    bool Approximate,
    bool Simulated);

public sealed record GpsInfoRow(string Label, string Value);

public sealed record GpsInfoView(string Message, bool ShowSettings, IReadOnlyList<GpsInfoRow> Rows);

/// <summary>
/// Words and rows for the GPS Info page. The phone fills in the sample; tests
/// check this text without a device.
/// </summary>
public static class GpsInfoText
{
    public const string PermissionDenied =
        "Location permission is denied. Allow location so this page can show your position.";

    public const string LocationOff =
        "Location is turned off. Turn location on to see your position.";

    public const string OpenSettings = "Open settings";

    public const string Waiting = "Waiting For Location Data";

    public static GpsInfoView Present(GpsInfoBlocker blocker, GpsInfoSample? sample, bool metric)
    {
        if (blocker == GpsInfoBlocker.PermissionDenied)
            return new GpsInfoView(PermissionDenied, true, []);
        if (blocker == GpsInfoBlocker.LocationOff)
            return new GpsInfoView(LocationOff, true, []);
        if (sample is null)
            return new GpsInfoView(Waiting, false, []);

        var rows = new List<GpsInfoRow>
        {
            new("Latitude", sample.Latitude.ToString("0.00000", CultureInfo.InvariantCulture)),
            new("Longitude", sample.Longitude.ToString("0.00000", CultureInfo.InvariantCulture)),
            new("Altitude", FormatAltitude(sample.AltitudeMeters, metric)),
            new("Speed", FormatSpeed(sample.SpeedMetersPerSecond, metric)),
            new("Accuracy", FormatAccuracy(sample.AccuracyMeters)),
            new("Satellites", FormatSatellites(sample)),
            new("Status", Status(sample))
        };
        var message = sample.Simulated
            ? "Simulated position."
            : sample.Approximate
                ? "Approximate location. Precise location is off."
                : "";
        return new GpsInfoView(message, sample.Approximate, rows);
    }

    private static string FormatAltitude(double? meters, bool metric)
    {
        if (meters is not double value || !double.IsFinite(value))
            return "—";
        return metric
            ? value.ToString("0", CultureInfo.InvariantCulture) + " m"
            : (value * 3.2808399).ToString("0", CultureInfo.InvariantCulture) + " ft";
    }

    private static string FormatSpeed(double? metersPerSecond, bool metric)
    {
        if (metersPerSecond is not double value || !double.IsFinite(value) || value < 0)
            return "—";
        var speed = Speed.FromMetersPerSecond(value);
        return metric
            ? speed.ToKilometersPerHour().Value.ToString("0.0", CultureInfo.InvariantCulture) + " kph"
            : speed.ToStatuteMilesPerHour().Value.ToString("0.0", CultureInfo.InvariantCulture) + " mph";
    }

    private static string FormatAccuracy(double? meters)
    {
        if (meters is not double value || !double.IsFinite(value) || value < 0)
            return "—";
        return "±" + value.ToString("0", CultureInfo.InvariantCulture) + " m";
    }

    private static string FormatSatellites(GpsInfoSample sample)
    {
        if (sample.SatellitesInView <= 0 && sample.SatellitesUsed <= 0)
            return "—";
        return sample.SatellitesUsed + " used, " + sample.SatellitesInView + " in view";
    }

    private static string Status(GpsInfoSample sample)
    {
        if (sample.Simulated)
            return "Simulated fix";
        if (sample.Approximate)
            return "Approximate";
        return "Fix";
    }
}
