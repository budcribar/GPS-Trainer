using System.Globalization;

namespace GpsTrainer;

public sealed record ProfileMark(double X, double Y, string Color, string Label);

/// <summary>
/// Elevation profile in the shape of the 2013 chart: distance on X, altitude on Y,
/// climb/descent from consecutive samples, and dots for the profile marker types.
/// Unknown altitude (-10 km) is left out so a missing sample is not a 10 km drop.
/// </summary>
public sealed class ProfilePlot
{
    public bool HasElevation { get; init; }
    public string Title { get; init; } = "";
    public string DistanceAxis { get; init; } = "";
    public string AltitudeAxis { get; init; } = "";
    public string ClimbDescent { get; init; } = "";
    public string Line { get; init; } = "";
    public string MinDistance { get; init; } = "";
    public string MaxDistance { get; init; } = "";
    public string MinAltitude { get; init; } = "";
    public string MaxAltitude { get; init; } = "";
    public IReadOnlyList<ProfileMark> Marks { get; init; } = [];
    public double GainKm { get; init; }
    public double LossKm { get; init; }
    public double MinDistanceKm { get; init; }
    public double MaxDistanceKm { get; init; }
    public IReadOnlyList<ProfileSample> Samples { get; init; } = [];
}

public readonly record struct ProfileSample(double DistanceKm, double AltitudeKm);

public static class ElevationProfile
{
    public const double HysteresisKm = 3.048 / 1000.0;
    private const double FeetPerKilometer = 3280.8399;

    public static (double GainKm, double LossKm) GainLoss(IReadOnlyList<Waypoint> waypoints)
    {
        var gain = 0.0;
        var loss = 0.0;
        double? previous = null;
        foreach (var point in waypoints)
        {
            if (!Known(point.AltitudeKm))
                continue;
            if (previous is null)
            {
                previous = point.AltitudeKm;
                continue;
            }
            var delta = point.AltitudeKm - previous.Value;
            if (Math.Abs(delta) <= HysteresisKm)
                continue;
            if (delta > 0)
                gain += delta;
            else
                loss -= delta;
            previous = point.AltitudeKm;
        }
        return (gain, loss);
    }

    public static string ClimbDescent(double gainKm, double lossKm, bool metric)
    {
        if (metric)
            return Math.Round(gainKm * 1000).ToString(CultureInfo.InvariantCulture)
                + "/" + Math.Round(lossKm * 1000).ToString(CultureInfo.InvariantCulture) + " m";
        return Math.Round(gainKm * FeetPerKilometer).ToString(CultureInfo.InvariantCulture)
            + "/" + Math.Round(lossKm * FeetPerKilometer).ToString(CultureInfo.InvariantCulture) + " ft";
    }

    public static ProfilePlot Plot(IReadOnlyList<Waypoint> waypoints, bool metric, string? courseName)
    {
        var samples = new List<Sample>();
        var distanceKm = 0.0;
        for (var i = 0; i < waypoints.Count; i++)
        {
            if (i > 0)
            {
                distanceKm += GeoMath.DistanceMeters(
                    waypoints[i - 1].Latitude, waypoints[i - 1].Longitude,
                    waypoints[i].Latitude, waypoints[i].Longitude) / 1000.0;
            }
            if (!Known(waypoints[i].AltitudeKm))
                continue;
            samples.Add(new Sample(distanceKm, waypoints[i].AltitudeKm, waypoints[i].WaypointType));
        }

        var (gain, loss) = GainLoss(waypoints);
        var name = string.IsNullOrWhiteSpace(courseName) ? "Course" : courseName.Trim();
        var plot = new ProfilePlot
        {
            HasElevation = samples.Count > 0,
            Title = name + " courtesy of www.gpsTrainer.com",
            DistanceAxis = metric ? "Distance(km)" : "Distance(mi)",
            AltitudeAxis = metric ? "Altitude(m)" : "Altitude(ft)",
            ClimbDescent = ClimbDescent(gain, loss, metric),
            GainKm = gain,
            LossKm = loss
        };
        if (samples.Count == 0)
            return plot;

        var minDistance = samples.Min(sample => sample.DistanceKm);
        var maxDistance = samples.Max(sample => sample.DistanceKm);
        var minAltitude = samples.Min(sample => DisplayAltitude(sample.AltitudeKm, metric));
        var maxAltitude = samples.Max(sample => DisplayAltitude(sample.AltitudeKm, metric));
        if (Math.Abs(maxAltitude - minAltitude) < 1)
        {
            minAltitude -= 1;
            maxAltitude += 1;
        }
        if (Math.Abs(maxDistance - minDistance) < 0.0001)
            maxDistance = minDistance + 0.01;

        const double left = 68;
        const double right = 742;
        const double top = 36;
        const double bottom = 196;
        var marks = new List<ProfileMark>();
        var line = new List<string>();
        foreach (var sample in samples)
        {
            var x = left + (DisplayDistance(sample.DistanceKm, metric) - DisplayDistance(minDistance, metric))
                / (DisplayDistance(maxDistance, metric) - DisplayDistance(minDistance, metric)) * (right - left);
            var altitude = DisplayAltitude(sample.AltitudeKm, metric);
            var y = bottom - (altitude - minAltitude) / (maxAltitude - minAltitude) * (bottom - top);
            line.Add(x.ToString("0.##", CultureInfo.InvariantCulture) + "," + y.ToString("0.##", CultureInfo.InvariantCulture));
            if (MarkColor(sample.Type) is string color)
            {
                marks.Add(new ProfileMark(Math.Round(x, 2), Math.Round(y, 2), color,
                    Label(sample.Type) + " " + Math.Round(altitude).ToString(CultureInfo.InvariantCulture) + (metric ? "m" : "ft")));
            }
        }
        if (line.Count == 1)
            line.Add(line[0]);

        return new ProfilePlot
        {
            HasElevation = true,
            Title = plot.Title,
            DistanceAxis = plot.DistanceAxis,
            AltitudeAxis = plot.AltitudeAxis,
            ClimbDescent = plot.ClimbDescent,
            GainKm = gain,
            LossKm = loss,
            Line = string.Join(" ", line),
            Marks = marks,
            MinDistance = DisplayDistance(minDistance, metric).ToString("0.00", CultureInfo.InvariantCulture),
            MaxDistance = DisplayDistance(maxDistance, metric).ToString("0.00", CultureInfo.InvariantCulture),
            MinDistanceKm = minDistance,
            MaxDistanceKm = maxDistance,
            MinAltitude = Math.Round(minAltitude).ToString(CultureInfo.InvariantCulture),
            MaxAltitude = Math.Round(maxAltitude).ToString(CultureInfo.InvariantCulture),
            Samples = samples.Select(sample => new ProfileSample(sample.DistanceKm, sample.AltitudeKm)).ToList()
        };
    }

    /// <summary>2013 chart tooltip: distance to two decimals, altitude rounded, units abbreviated.</summary>
    public static string Hover(ProfilePlot plot, double viewBoxX, bool metric)
    {
        if (!plot.HasElevation || plot.Samples.Count == 0)
            return "";
        var elapsed = SummitSearch.ProfileElapsedKm(viewBoxX, plot.MinDistanceKm, plot.MaxDistanceKm);
        var altitudeKm = AltitudeAt(plot.Samples, elapsed);
        if (altitudeKm is null)
            return "";
        var distance = metric ? elapsed : elapsed * 0.621371192;
        var altitude = metric ? altitudeKm.Value * 1000.0 : altitudeKm.Value * FeetPerKilometer;
        return distance.ToString("0.00", CultureInfo.InvariantCulture)
            + (metric ? "k," : "mi,")
            + Math.Round(altitude).ToString(CultureInfo.InvariantCulture)
            + (metric ? "m" : "ft");
    }

    private static double? AltitudeAt(IReadOnlyList<ProfileSample> samples, double elapsedKm)
    {
        if (samples.Count == 0 || !double.IsFinite(elapsedKm))
            return null;
        if (elapsedKm <= samples[0].DistanceKm)
            return samples[0].AltitudeKm;
        for (var i = 1; i < samples.Count; i++)
        {
            var next = samples[i];
            if (elapsedKm > next.DistanceKm)
                continue;
            var previous = samples[i - 1];
            var span = next.DistanceKm - previous.DistanceKm;
            if (span <= 1e-9)
                return next.AltitudeKm;
            var fraction = (elapsedKm - previous.DistanceKm) / span;
            return previous.AltitudeKm + (next.AltitudeKm - previous.AltitudeKm) * fraction;
        }
        return samples[^1].AltitudeKm;
    }

    private static bool Known(double altitudeKm) =>
        double.IsFinite(altitudeKm) && altitudeKm != Altitude.Unknown.Kilometers;

    private static double DisplayDistance(double kilometers, bool metric) =>
        metric ? kilometers : kilometers * 0.621371192;

    private static double DisplayAltitude(double kilometers, bool metric) =>
        metric ? kilometers * 1000.0 : kilometers * FeetPerKilometer;

    private static string? MarkColor(WaypointType type) => type switch
    {
        WaypointType.StartInterval => "#008000",
        WaypointType.FinishInterval => "#ff0000",
        WaypointType.Summit => "#000000",
        WaypointType.FeedZone => "#000000",
        WaypointType.Announcement => "#ffffff",
        WaypointType.TurnAround => "#0000ff",
        WaypointType.Finish => "#8b1e3f",
        _ => null
    };

    private static string Label(WaypointType type) => type switch
    {
        WaypointType.StartInterval => "Start Interval",
        WaypointType.FinishInterval => "Finish Interval",
        WaypointType.TurnAround => "Turn Around",
        WaypointType.FeedZone => "Feed Zone",
        _ => type.ToString()
    };

    private readonly record struct Sample(double DistanceKm, double AltitudeKm, WaypointType Type);
}
