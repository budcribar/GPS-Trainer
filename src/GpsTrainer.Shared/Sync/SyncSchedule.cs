namespace GpsTrainer;

/// <summary>When an open app may start the existing background sync.</summary>
public enum SyncMoment
{
    Opened,
    Foreground,
    Timer
}

public static class SyncSchedule
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    public static bool Due(
        SyncMoment moment,
        bool inForeground,
        bool recording,
        bool alreadyRunning,
        bool signedIn,
        bool online,
        TimeSpan sinceLastAttempt)
    {
        if (!inForeground)
            return false;
        if (!BackgroundSync.ShouldRun(recording, alreadyRunning, signedIn, online))
            return false;
        if (moment == SyncMoment.Timer)
            return sinceLastAttempt >= Interval;
        return true;
    }
}
