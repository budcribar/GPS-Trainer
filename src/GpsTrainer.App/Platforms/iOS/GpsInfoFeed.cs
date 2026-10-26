using CoreLocation;
using Foundation;

namespace GpsTrainer.App;

/// <summary>iOS location for GPS Info. Satellite counts are not exposed by Core Location.</summary>
public sealed class IosGpsInfoFeed : IGpsInfoFeed
{
    private readonly IosGpsDelegate _delegate = new();
    private CLLocationManager? _manager;
    private GpsInfoBlocker _blocker;

    public event EventHandler<GpsInfoUpdate>? Updated;

    public IosGpsInfoFeed()
    {
        _delegate.Updated += (_, update) =>
        {
            _blocker = update.Blocker;
            Updated?.Invoke(this, update);
        };
    }

    public Task StartAsync()
    {
        Stop();
        if (Preferences.Get("Simulate", false))
        {
            _blocker = GpsInfoBlocker.None;
            Updated?.Invoke(this, new GpsInfoUpdate(GpsInfoBlocker.None, new GpsInfoSample(
                40.015, -105.27, 1650, 4.5, 5, 0, 0, Approximate: false, Simulated: true)));
            return Task.CompletedTask;
        }

        if (!CLLocationManager.LocationServicesEnabled)
        {
            _blocker = GpsInfoBlocker.LocationOff;
            Updated?.Invoke(this, new GpsInfoUpdate(GpsInfoBlocker.LocationOff, null));
            return Task.CompletedTask;
        }

        var manager = new CLLocationManager { Delegate = _delegate, DesiredAccuracy = CLLocation.AccuracyBest };
        _manager = manager;
        var status = manager.AuthorizationStatus;
        if (status is CLAuthorizationStatus.Denied or CLAuthorizationStatus.Restricted)
        {
            _blocker = GpsInfoBlocker.PermissionDenied;
            Updated?.Invoke(this, new GpsInfoUpdate(GpsInfoBlocker.PermissionDenied, null));
            return Task.CompletedTask;
        }

        manager.RequestWhenInUseAuthorization();
        manager.StartUpdatingLocation();
        Updated?.Invoke(this, new GpsInfoUpdate(GpsInfoBlocker.None, null));
        return Task.CompletedTask;
    }

    public void Stop()
    {
        _manager?.StopUpdatingLocation();
        _manager?.Dispose();
        _manager = null;
    }

    public void OpenSettings() => AppInfo.ShowSettingsUI();
}

sealed class IosGpsDelegate : CLLocationManagerDelegate
{
    public event EventHandler<GpsInfoUpdate>? Updated;

    public override void LocationsUpdated(CLLocationManager manager, CLLocation[] locations)
    {
        var loc = locations.LastOrDefault();
        if (loc is null)
            return;
        var approximate = loc.HorizontalAccuracy > 100;
        var sample = new GpsInfoSample(
            loc.Coordinate.Latitude,
            loc.Coordinate.Longitude,
            loc.VerticalAccuracy >= 0 ? loc.Altitude : null,
            loc.Speed >= 0 ? loc.Speed : null,
            loc.HorizontalAccuracy >= 0 ? loc.HorizontalAccuracy : null,
            0,
            0,
            approximate,
            Simulated: false);
        Updated?.Invoke(this, new GpsInfoUpdate(GpsInfoBlocker.None, sample));
    }

    public override void AuthorizationChanged(CLLocationManager manager, CLAuthorizationStatus status)
    {
        if (status is CLAuthorizationStatus.Denied or CLAuthorizationStatus.Restricted)
            Updated?.Invoke(this, new GpsInfoUpdate(GpsInfoBlocker.PermissionDenied, null));
    }
}
