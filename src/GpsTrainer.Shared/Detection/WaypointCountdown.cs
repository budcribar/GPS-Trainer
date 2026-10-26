namespace GpsTrainer;

/// <summary>
/// Reconstruction of <c>WaypointDetector.CountdownThread.run()</c>, based on
/// the original 1.44 WaypointDetector countdown thread.
/// The jadx Java method was replaced with <c>throw new UnsupportedOperationException</c>.
/// </summary>
/// <remarks>
/// Timings taken from the detector field initializers (smali <c>.line 15-17</c>):
/// countdown 3 s, polling window 300 s, polling interval 100 ms.
/// Each loop iteration, while not <see cref="Speed.AtRest"/>, subtracts 100 ms from the ETA,
/// then sleeps 100 ms. While ETA is at or under the countdown, the countdown drops by 1 s
/// and <c>WaypointApproachedEvent</c> fires (the original passed a null argument).
/// The loop exits when ETA is at or under zero, or when <c>triggered</c> is set.
/// Shutdown calls resync, sets detour distance, and fires arrival unless already triggered.
/// The original locked on the thread instance, not the detector, so ETA updates from
/// <c>Approach</c> were not guarded by the same lock.
/// </remarks>
public static class WaypointCountdown
{
    public static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan PollingWindow = TimeSpan.FromSeconds(300);
    public static readonly TimeSpan InitialCountDown = TimeSpan.FromSeconds(3);
    public const int ThreadPriority = 10;
    public const string ThreadName = "WaypointDetector Countdown Thread";

    public static bool TryPoll(WaypointCountdownState state)
    {
        if (!state.SpeedIsAtRestSingleton)
            state.TimeToArrival -= PollingInterval;

        if (state.TimeToArrival <= TimeSpan.Zero || state.Triggered)
            return false;

        if (state.TimeToArrival <= state.CountDown)
        {
            state.CountDown -= TimeSpan.FromSeconds(1);
            state.ApproachedCount++;
        }

        return true;
    }

    public static void Shutdown(WaypointCountdownState state)
    {
        state.ResyncedElapsedDistance = true;
        var cur = Distance.FromKilometers(state.CurToTargetKilometers);
        var prev = Distance.FromKilometers(state.PrevToTargetKilometers);
        state.Detour = cur.ToKilometers().Value < prev.ToKilometers().Value
            ? Distance.Empty.Subtract(cur)
            : cur;
        if (!state.Triggered)
            state.ArrivalFired = true;
    }

    /// <summary>
    /// Runs the loop the way the thread did: poll, then wait one interval, until stop.
    /// <paramref name="delay"/> replaces <c>Thread.sleep</c> so tests can skip the wait.
    /// </summary>
    public static void Run(WaypointCountdownState state, Action<TimeSpan>? delay = null, int maxSteps = 100_000)
    {
        var steps = 0;
        while (steps++ < maxSteps && TryPoll(state))
            delay?.Invoke(PollingInterval);
        Shutdown(state);
    }
}

public sealed class WaypointCountdownState
{
    public TimeSpan TimeToArrival { get; set; } = TimeSpan.MaxValue;
    public TimeSpan CountDown { get; set; } = WaypointCountdown.InitialCountDown;
    public bool Triggered { get; set; }

    /// <summary>
    /// True only when <c>currentSpeed</c> is the <see cref="Speed.AtRest"/> singleton.
    /// A different <c>Speed</c> instance with value 0 still ticks, matching <c>Double</c> reference equality.
    /// </summary>
    public bool SpeedIsAtRestSingleton { get; set; }

    public double CurToTargetKilometers { get; set; }
    public double PrevToTargetKilometers { get; set; }
    public int ApproachedCount { get; set; }
    public bool ArrivalFired { get; set; }
    public bool ResyncedElapsedDistance { get; set; }
    public Distance? Detour { get; set; }
}
