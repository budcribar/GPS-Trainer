namespace GpsTrainer;

/// <summary>
/// ETA rules from <c>WaypointDetector.Approach</c> (Java lines 166-178, smali <c>.line 233-253</c>).
/// </summary>
public static class WaypointApproach
{
    public static readonly TimeSpan CloseEta = TimeSpan.FromSeconds(5);
    public static readonly Distance PastWaypointMargin = Distance.FromMeters(500);

    public static double DistanceToTargetKilometers(double curToTargetKm, double elapsedToTargetKm) =>
        Math.Max(curToTargetKm, elapsedToTargetKm);

    public static TimeSpan UpdatedEta(double curToTargetKm, double elapsedToTargetKm, double speedKilometersPerSecond, TimeSpan timeLag)
    {
        if (speedKilometersPerSecond <= 0 || double.IsNaN(speedKilometersPerSecond) || double.IsInfinity(speedKilometersPerSecond))
            return TimeSpan.MaxValue;
        var dist = DistanceToTargetKilometers(curToTargetKm, elapsedToTargetKm);
        var seconds = dist / speedKilometersPerSecond;
        return TimeSpan.FromSeconds(Math.Max(seconds - timeLag.TotalSeconds, 0));
    }

    /// <summary>
    /// Refresh when the new ETA is sooner than the one being counted, or when it is still
    /// more than 5 seconds out. Inside the last 5 seconds a later ETA is ignored.
    /// </summary>
    public static bool ShouldRefreshEta(TimeSpan updatedEta, TimeSpan timeToArrival) =>
        updatedEta < timeToArrival || updatedEta > CloseEta;

    public static bool ShouldStartCountdown(TimeSpan updatedEta, bool countdownRunning) =>
        !countdownRunning && updatedEta <= WaypointCountdown.PollingWindow;

    /// <summary>Original <c>Approach</c> returns immediately when speed reference-equals <see cref="Speed.AtRest"/>.</summary>
    public static bool ShouldIgnoreBecauseAtRest(Speed speed) => ReferenceEquals(speed, Speed.AtRest);
}
