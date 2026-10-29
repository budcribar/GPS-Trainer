namespace GpsTrainer.App;

/// <summary>Starts the existing background sync when the app is open.</summary>
public sealed class ForegroundSync
{
    private readonly SyncService _sync;
    private readonly AppSession _session;
    private readonly Func<bool> _recording;
    private IDispatcherTimer? _timer;
    private bool _attached;
    private bool _inForeground;
    private DateTime _lastAttemptUtc = DateTime.MinValue;

    public ForegroundSync(SyncService sync, AppSession session, Func<bool> recording)
    {
        _sync = sync;
        _session = session;
        _recording = recording;
    }

    public void Attach(Window window)
    {
        if (_attached)
            return;
        _attached = true;
        window.Created += (_, _) => Enter(SyncMoment.Opened);
        window.Resumed += (_, _) => Enter(SyncMoment.Foreground);
        window.Backgrounding += (_, _) => Leave();
        Enter(SyncMoment.Opened);
    }

    /// <summary>After sign-in the session exists, so the open sync can run.</summary>
    public void Nudge() => Kick(SyncMoment.Opened);

    private void Enter(SyncMoment moment)
    {
        _inForeground = true;
        EnsureTimer();
        Kick(moment);
    }

    private void Leave()
    {
        _inForeground = false;
        _timer?.Stop();
    }

    private void EnsureTimer()
    {
        if (_timer is null)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null)
                return;
            _timer = dispatcher.CreateTimer();
            _timer.Interval = SyncSchedule.Interval;
            _timer.IsRepeating = true;
            _timer.Tick += (_, _) => Kick(SyncMoment.Timer);
        }
        if (!_timer.IsRunning)
            _timer.Start();
    }

    private void Kick(SyncMoment moment)
    {
        if (!SyncSchedule.Due(
                moment,
                _inForeground,
                _recording(),
                _sync.IsBusy,
                BackgroundSync.IsSignedIn(_session.User?.Login),
                Online(),
                DateTime.UtcNow - _lastAttemptUtc))
            return;
        _lastAttemptUtc = DateTime.UtcNow;
        _ = Task.Run(async () =>
        {
            try
            {
                await _sync.SynchronizeAfterFinishAsync(_recording);
            }
            catch (Exception)
            {
            }
        });
    }

    private static bool Online()
    {
        try
        {
            return Connectivity.NetworkAccess == NetworkAccess.Internet;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
