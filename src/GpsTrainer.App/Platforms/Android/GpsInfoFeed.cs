using Android.Content;
using Android.Content.PM;
using Android.Locations;
using Android.OS;
using Android.Provider;
using AndroidX.Core.Content;
using AndroidLocation = Android.Locations.Location;
using AUri = Android.Net.Uri;

namespace GpsTrainer.App;

/// <summary>
/// Reads the device location while GPS Info is open. This does not start the
/// ride foreground service: that service runs only while a ride is being tracked.
/// </summary>
public sealed class AndroidGpsInfoFeed : IGpsInfoFeed
{
    public event EventHandler<GpsInfoUpdate>? Updated;

    private LocationManager? _manager;
    private GpsInfoListener? _listener;
    private SatelliteCallback? _satellites;
    private Timer? _simulator;
    private AndroidLocation? _last;
    private int _generation;
    private GpsInfoBlocker _blocker;
    private int _inView;
    private int _used;
    private bool _approximate;
    private double _simLatitude = 40.015;

    public async Task StartAsync()
    {
        var generation = Interlocked.Increment(ref _generation);
        StopListeners();
        _last = null;
        _inView = 0;
        _used = 0;
        _approximate = false;

        if (Preferences.Get("Simulate", false))
        {
            if (generation != Volatile.Read(ref _generation))
                return;
            _blocker = GpsInfoBlocker.None;
            PublishSim();
            _simulator = new Timer(_ =>
            {
                if (generation != Volatile.Read(ref _generation))
                    return;
                _simLatitude += 0.00008;
                PublishSim();
            }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            return;
        }

        var context = Platform.AppContext;
        var fine = Has(context, Android.Manifest.Permission.AccessFineLocation);
        var coarse = Has(context, Android.Manifest.Permission.AccessCoarseLocation);
        if (!fine && !coarse)
        {
            await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            if (generation != Volatile.Read(ref _generation))
                return;
            fine = Has(context, Android.Manifest.Permission.AccessFineLocation);
            coarse = Has(context, Android.Manifest.Permission.AccessCoarseLocation);
        }

        if (!fine && !coarse)
        {
            Publish(GpsInfoBlocker.PermissionDenied, null);
            return;
        }

        // Android 12+ can grant approximate (coarse) without precise (fine).
        _approximate = !fine;
        var manager = context.GetSystemService(Context.LocationService) as LocationManager;
        if (manager is null || !LocationEnabled(manager, context))
        {
            Publish(GpsInfoBlocker.LocationOff, null);
            return;
        }

        _manager = manager;
        _listener = new GpsInfoListener(OnLocation);
        var listening = false;
        var denied = false;
        if (fine)
            listening |= Listen(manager, LocationManager.GpsProvider, ref denied);
        listening |= Listen(manager, LocationManager.NetworkProvider, ref denied);
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
            listening |= Listen(manager, LocationManager.FusedProvider, ref denied);
        listening |= Listen(manager, LocationManager.PassiveProvider, ref denied);
        if (!listening)
        {
            Publish(denied ? GpsInfoBlocker.PermissionDenied : GpsInfoBlocker.LocationOff, null);
            return;
        }

        if (fine && OperatingSystem.IsAndroidVersionAtLeast(30))
            WatchSatellites(manager, context);

        var last = BestLastKnown(manager, fine);
        if (generation != Volatile.Read(ref _generation))
            return;
        if (last is not null)
            OnLocation(last);
        else
            Publish(GpsInfoBlocker.None, null);
    }

    public void Stop()
    {
        Interlocked.Increment(ref _generation);
        StopListeners();
    }

    public void OpenSettings()
    {
        var context = Platform.CurrentActivity ?? Platform.AppContext;
        Intent intent = _blocker == GpsInfoBlocker.LocationOff
            ? new Intent(Settings.ActionLocationSourceSettings)
            : new Intent(Settings.ActionApplicationDetailsSettings).SetData(AUri.Parse("package:" + context.PackageName));
        intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
    }

    private void WatchSatellites(LocationManager manager, Context context)
    {
        _satellites = new SatelliteCallback((view, used) =>
        {
            _inView = view;
            _used = used;
            if (_last is not null)
                OnLocation(_last);
        });
        try
        {
            manager.RegisterGnssStatusCallback(context.MainExecutor!, _satellites);
        }
        catch (Java.Lang.Exception)
        {
            _satellites = null;
        }
    }

    private void OnLocation(AndroidLocation location)
    {
        _last = location;
        var used = _used;
        if (used == 0 && location.Extras?.ContainsKey("satellites") == true)
            used = location.Extras.GetInt("satellites");
        var sample = new GpsInfoSample(
            location.Latitude,
            location.Longitude,
            location.HasAltitude ? location.Altitude : null,
            location.HasSpeed && location.Speed >= 0 ? (double)location.Speed : null,
            location.HasAccuracy ? (double)location.Accuracy : null,
            _inView,
            used,
            _approximate,
            Simulated: false);
        Publish(GpsInfoBlocker.None, sample);
    }

    private void PublishSim()
    {
        Publish(GpsInfoBlocker.None, new GpsInfoSample(
            _simLatitude, -105.27, 1650, 4.5, 5, 12, 8, Approximate: false, Simulated: true));
    }

    private void Publish(GpsInfoBlocker blocker, GpsInfoSample? sample)
    {
        _blocker = blocker;
        var update = new GpsInfoUpdate(blocker, sample);
        MainThread.BeginInvokeOnMainThread(() => Updated?.Invoke(this, update));
    }

    private void StopListeners()
    {
        _simulator?.Dispose();
        _simulator = null;
        if (_manager is not null && _satellites is not null)
        {
            try
            {
                _manager.UnregisterGnssStatusCallback(_satellites);
            }
            catch (Java.Lang.Exception)
            {
                // The callback was already gone.
            }
        }
        if (_manager is not null && _listener is not null)
        {
            try
            {
                _manager.RemoveUpdates(_listener);
            }
            catch (Java.Lang.Exception)
            {
                // Updates were already removed.
            }
        }
        _satellites = null;
        _listener = null;
        _manager = null;
    }

    private static bool Has(Context context, string permission) =>
        ContextCompat.CheckSelfPermission(context, permission) == Permission.Granted;

    private static bool LocationEnabled(LocationManager manager, Context context)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(28))
            return manager.IsLocationEnabled;
        try
        {
            return Settings.Secure.GetInt(context.ContentResolver, "location_mode") != 0;
        }
        catch (Java.Lang.Exception)
        {
            return false;
        }
    }

    private static AndroidLocation? BestLastKnown(LocationManager manager, bool fine)
    {
        AndroidLocation? best = null;
        foreach (var provider in Candidates(fine))
        {
            if (string.IsNullOrEmpty(provider))
                continue;
            try
            {
                var last = manager.GetLastKnownLocation(provider);
                if (last is null)
                    continue;
                if (best is null || last.Time > best.Time)
                    best = last;
            }
            catch (Java.Lang.Exception)
            {
                // This provider is not readable. Another one may be.
            }
        }
        return best;
    }

    private static IEnumerable<string?> Candidates(bool fine)
    {
        if (fine)
            yield return LocationManager.GpsProvider;
        yield return LocationManager.NetworkProvider;
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
            yield return LocationManager.FusedProvider;
    }

    private bool Listen(LocationManager manager, string? provider, ref bool denied)
    {
        if (_listener is null || string.IsNullOrEmpty(provider))
            return false;
        try
        {
            if (!manager.IsProviderEnabled(provider))
                return false;
            manager.RequestLocationUpdates(provider, 1000L, 0f, _listener, Looper.MainLooper!);
            return true;
        }
        catch (Java.Lang.IllegalArgumentException)
        {
            return false;
        }
        catch (Java.Lang.SecurityException)
        {
            denied = true;
            return false;
        }
    }
}

sealed class GpsInfoListener : Java.Lang.Object, ILocationListener
{
    private readonly Action<AndroidLocation> _onLocation;

    public GpsInfoListener(Action<AndroidLocation> onLocation) => _onLocation = onLocation;

    public void OnLocationChanged(AndroidLocation location) => _onLocation(location);

    public void OnProviderDisabled(string provider)
    {
    }

    public void OnProviderEnabled(string provider)
    {
    }

    public void OnStatusChanged(string? provider, Availability status, Bundle? extras)
    {
    }

    public void OnFlushComplete(int requestCode)
    {
    }
}

sealed class SatelliteCallback : GnssStatus.Callback
{
    private readonly Action<int, int> _onStatus;

    public SatelliteCallback(Action<int, int> onStatus) => _onStatus = onStatus;

    public override void OnSatelliteStatusChanged(GnssStatus status)
    {
        var view = status.SatelliteCount;
        var used = 0;
        for (var i = 0; i < view; i++)
        {
            if (status.UsedInFix(i))
                used++;
        }
        _onStatus(view, used);
    }
}
