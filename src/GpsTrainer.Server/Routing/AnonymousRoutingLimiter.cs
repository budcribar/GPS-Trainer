using System.Net;

namespace GpsTrainer.Server.Routing;

/// <summary>
/// Caps anonymous directions and elevation calls per client address.
/// Signed-in calls are not counted. The OpenRouteService key stays on the server.
/// </summary>
public sealed class AnonymousRoutingLimiter
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Queue<long>> _hits = new();

    public AnonymousRoutingLimiter(IConfiguration config)
    {
        PermitLimit = Math.Clamp(config.GetValue("Routing:AnonymousPermitLimit", 30), 1, 500);
        var seconds = Math.Clamp(config.GetValue("Routing:AnonymousWindowSeconds", 60), 1, 3600);
        Window = TimeSpan.FromSeconds(seconds);
    }

    public int PermitLimit { get; }
    public TimeSpan Window { get; }

    public bool TryAcquire(string clientKey, DateTime? utcNow = null)
    {
        var now = utcNow ?? DateTime.UtcNow;
        var cutoff = now.Subtract(Window).Ticks;
        var stamp = now.Ticks;
        lock (_gate)
        {
            if (!_hits.TryGetValue(clientKey, out var queue))
            {
                queue = new Queue<long>();
                _hits[clientKey] = queue;
            }
            while (queue.Count > 0 && queue.Peek() <= cutoff)
                queue.Dequeue();
            if (queue.Count >= PermitLimit)
                return false;
            queue.Enqueue(stamp);
            if (_hits.Count > 2048)
                DropStale(cutoff);
            return true;
        }
    }

    public static string ClientAddress(HttpContext http)
    {
        var forwarded = http.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var first = forwarded.Split(',')[0].Trim();
            if (TryNormalize(first, out var address))
                return address;
        }
        var remote = http.Connection.RemoteIpAddress;
        return remote is null ? "unknown" : remote.ToString();
    }

    private void DropStale(long cutoff)
    {
        var stale = new List<string>();
        foreach (var pair in _hits)
        {
            var newest = 0L;
            foreach (var tick in pair.Value)
            {
                if (tick > newest)
                    newest = tick;
            }
            if (pair.Value.Count == 0 || newest <= cutoff)
                stale.Add(pair.Key);
        }
        foreach (var key in stale)
            _hits.Remove(key);
    }

    private static bool TryNormalize(string text, out string address)
    {
        address = "";
        if (text.Length is 0 or > 64)
            return false;
        var bare = text;
        if (bare.StartsWith('[') && bare.Contains(']'))
            bare = bare[1..bare.IndexOf(']')];
        if (!IPAddress.TryParse(bare, out var parsed))
            return false;
        address = parsed.ToString();
        return true;
    }
}
