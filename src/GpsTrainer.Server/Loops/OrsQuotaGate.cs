namespace GpsTrainer.Server.Loops;

/// <summary>
/// Stops further OpenRouteService calls for this process after a quota or rate-limit response.
/// </summary>
public sealed class OrsQuotaGate
{
    private long _untilTicks;

    public bool IsBlocked => DateTime.UtcNow.Ticks < Interlocked.Read(ref _untilTicks);

    public void Block(TimeSpan? duration = null)
    {
        var until = DateTime.UtcNow.Add(duration ?? TimeSpan.FromHours(6)).Ticks;
        while (true)
        {
            var current = Interlocked.Read(ref _untilTicks);
            if (current >= until)
                return;
            if (Interlocked.CompareExchange(ref _untilTicks, until, current) == current)
                return;
        }
    }

    public static bool IsQuota(int statusCode, string? body)
    {
        if (statusCode == 429)
            return true;
        if (string.IsNullOrEmpty(body))
            return false;
        return body.Contains("quota", StringComparison.OrdinalIgnoreCase)
            || body.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
            || body.Contains("daily limit", StringComparison.OrdinalIgnoreCase);
    }
}
