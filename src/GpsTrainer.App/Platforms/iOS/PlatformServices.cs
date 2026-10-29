using AVFoundation;
using CoreLocation;
using Foundation;

namespace GpsTrainer.App;

public sealed partial class PackageAudioPlayer
{
    private static readonly object Gate = new();
    private static AVAudioPlayer? _activePlayer;
    private static AVAudioPlayer? _loopPlayer;

    private static partial Task PlayFileAsync(string path, bool loop)
    {
        lock (Gate)
        {
            if (loop && _loopPlayer is { Playing: true })
                return Task.CompletedTask;
            var url = NSUrl.FromFilename(path);
            var player = AVAudioPlayer.FromUrl(url, out _);
            if (player is null)
                return Task.CompletedTask;
            player.NumberOfLoops = loop ? -1 : 0;
            player.PrepareToPlay();
            player.Play();
            if (loop)
            {
                _loopPlayer?.Stop();
                _loopPlayer?.Dispose();
                _loopPlayer = player;
            }
            else
            {
                _activePlayer?.Dispose();
                _activePlayer = player;
            }
        }
        return Task.CompletedTask;
    }

    internal static void StopLoop()
    {
        lock (Gate)
        {
            _loopPlayer?.Stop();
            _loopPlayer?.Dispose();
            _loopPlayer = null;
        }
    }
}

/// <summary>
/// iOS does not expose the classic Bluetooth bonded-device list the Android app scanned.
/// This stays empty on purpose. Heart-rate sensors were not in version 1.44.
/// </summary>
public sealed class IosBluetooth : IBluetoothDevices
{
    public bool IsAudioDevicePaired() => false;
    public IReadOnlyList<string> PairedAudioDeviceNames() => [];
}

public partial class TrackingHost
{
    private static readonly BackgroundLocationSession Session = new();

    partial void StartPlatform() => Session.Start();
    partial void StopPlatform()
    {
        PackageAudioPlayer.StopLoop();
        Session.Stop();
    }
}

sealed class BackgroundLocationSession
{
    private CLLocationManager? _manager;

    public void Start()
    {
        _manager ??= new CLLocationManager();
        _manager.AllowsBackgroundLocationUpdates = true;
        _manager.PausesLocationUpdatesAutomatically = false;
        _manager.RequestAlwaysAuthorization();
        _manager.StartUpdatingLocation();
    }

    public void Stop() => _manager?.StopUpdatingLocation();
}
