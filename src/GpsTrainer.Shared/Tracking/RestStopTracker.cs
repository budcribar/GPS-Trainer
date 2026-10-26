namespace GpsTrainer;

public readonly record struct RestStop(DateTime Start, DateTime Stop)
{
    public TimeSpan Duration => Stop - Start;
}

/// <summary>
/// Port of the rest-stop gate in <c>GpsDispatcher</c>. When <see cref="IgnoreRestStops"/> is set
/// and the run is racing or learning, samples at <see cref="Speed.AtRest"/> are not emitted and
/// their duration is accumulated so later timestamps can be shifted back by <see cref="TotalRest"/>.
/// Samples with accuracy at or above <see cref="PoorAccuracyMeters"/> are dropped the same way
/// the dispatcher ignored them.
/// </summary>
public sealed class RestStopTracker
{
    public const double PoorAccuracyMeters = 100;

    public bool IgnoreRestStops { get; set; } = true;
    public bool IsResting { get; private set; }
    public TimeSpan TotalRest { get; private set; }
    public IReadOnlyList<RestStop> Stops => _stops;

    private readonly List<RestStop> _stops = [];
    private DateTime _restStarted;

    public bool Accept(bool racingOrLearning, bool atRest, double? accuracyMeters, DateTime timestamp, out DateTime effectiveTimestamp)
    {
        effectiveTimestamp = timestamp - TotalRest;
        if (accuracyMeters is >= PoorAccuracyMeters)
            return false;

        if (!(racingOrLearning && IgnoreRestStops))
        {
            IsResting = false;
            return true;
        }

        if (atRest)
        {
            if (!IsResting)
            {
                IsResting = true;
                _restStarted = timestamp;
            }
            return false;
        }

        if (IsResting)
        {
            var stop = new RestStop(_restStarted, timestamp);
            _stops.Add(stop);
            TotalRest += stop.Duration;
            IsResting = false;
            effectiveTimestamp = timestamp - TotalRest;
        }

        return true;
    }
}
