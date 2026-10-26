using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using GpsTrainer;

namespace GpsTrainer.Server.Loops;

public sealed class LoopSnapCache
{
    private readonly ConcurrentDictionary<string, GeoCoord[]> _routes = new(StringComparer.Ordinal);

    public string KeyFor(IReadOnlyList<GeoCoord> via)
    {
        var builder = new StringBuilder(via.Count * 24);
        foreach (var point in via)
        {
            builder.Append(point.Latitude.ToString("0.0000", CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(point.Longitude.ToString("0.0000", CultureInfo.InvariantCulture));
            builder.Append(';');
        }
        return builder.ToString();
    }

    public bool TryGet(string key, out GeoCoord[] route) => _routes.TryGetValue(key, out route!);

    public void Store(string key, IReadOnlyList<GeoCoord> route) => _routes[key] = route.ToArray();
}
