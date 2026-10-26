using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using GpsTrainer;

namespace GpsTrainer.Server.Routing;

/// <summary>
/// Remembers a successful elevation/line for this process. The same rounded
/// geometry is not sent to OpenRouteService again.
/// </summary>
public sealed class ElevationLineCache
{
    private readonly ConcurrentDictionary<string, double[]> _values = new(StringComparer.Ordinal);

    public string KeyFor(IReadOnlyList<ElevationSampleDto> points)
    {
        var builder = new StringBuilder(points.Count * 24);
        foreach (var point in points)
        {
            builder.Append(point.Longitude.ToString("0.00000", CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(point.Latitude.ToString("0.00000", CultureInfo.InvariantCulture));
            builder.Append(';');
        }
        return builder.ToString();
    }

    public bool TryGet(string key, out double[] altitudeKm) => _values.TryGetValue(key, out altitudeKm!);

    public void Store(string key, double[] altitudeKm) => _values[key] = altitudeKm;
}
