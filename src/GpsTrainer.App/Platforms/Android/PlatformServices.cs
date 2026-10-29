using Android.App;
using Android.Bluetooth;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using Application = Android.App.Application;

namespace GpsTrainer.App;

public sealed partial class PackageAudioPlayer
{
    private static readonly object LoopGate = new();
    private static Android.Media.MediaPlayer? _loopPlayer;

    private static partial Task PlayFileAsync(string path, bool loop)
    {
        if (loop)
        {
            lock (LoopGate)
            {
                if (_loopPlayer is { IsPlaying: true })
                    return Task.CompletedTask;
                StopLoopCore();
                var loopPlayer = new Android.Media.MediaPlayer();
                loopPlayer.SetDataSource(path);
                loopPlayer.Looping = true;
                loopPlayer.Prepare();
                loopPlayer.Start();
                _loopPlayer = loopPlayer;
            }
            return Task.CompletedTask;
        }

        var player = new Android.Media.MediaPlayer();
        player.Completion += (_, _) =>
        {
            try
            {
                player.Release();
                player.Dispose();
            }
            catch (Exception)
            {
            }
        };
        player.Error += (_, _) =>
        {
            try
            {
                player.Release();
                player.Dispose();
            }
            catch (Exception)
            {
            }
        };
        player.SetDataSource(path);
        player.Looping = false;
        player.Prepare();
        player.Start();
        return Task.CompletedTask;
    }

    internal static void StopLoop()
    {
        lock (LoopGate)
        {
            StopLoopCore();
        }
    }

    private static void StopLoopCore()
    {
        if (_loopPlayer is null)
            return;
        try
        {
            if (_loopPlayer.IsPlaying)
                _loopPlayer.Stop();
        }
        catch (Exception)
        {
        }
        try
        {
            _loopPlayer.Release();
            _loopPlayer.Dispose();
        }
        catch (Exception)
        {
        }
        _loopPlayer = null;
    }
}

public sealed class AndroidBluetooth : IBluetoothDevices
{
    public bool IsAudioDevicePaired() => Names().Count > 0;

    public IReadOnlyList<string> PairedAudioDeviceNames() => Names();

    private static List<string> Names()
    {
        var names = new List<string>();
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(31)
                && AndroidX.Core.Content.ContextCompat.CheckSelfPermission(
                    Platform.AppContext,
                    global::Android.Manifest.Permission.BluetoothConnect) != global::Android.Content.PM.Permission.Granted)
            {
                return names;
            }
            var adapter = BluetoothAdapter.DefaultAdapter;
            if (adapter is null || !adapter.IsEnabled)
                return names;
            var devices = adapter.BondedDevices;
            if (devices is null)
                return names;
            foreach (var device in devices)
            {
                if (device.BluetoothClass?.MajorDeviceClass == MajorDeviceClass.AudioVideo)
                    names.Add(device.Name ?? device.Address ?? "audio");
            }
        }
        catch (Exception)
        {
        }
        return names;
    }
}

public partial class TrackingHost
{
    partial void StartPlatform()
    {
        var intent = new Intent(Platform.AppContext, typeof(TrackingForegroundService));
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            Platform.AppContext.StartForegroundService(intent);
        else
            Platform.AppContext.StartService(intent);
    }

    partial void StopPlatform()
    {
        PackageAudioPlayer.StopLoop();
        var intent = new Intent(Platform.AppContext, typeof(TrackingForegroundService));
        Platform.AppContext.StopService(intent);
    }
}

[Service(ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeLocation, Exported = false)]
public class TrackingForegroundService : Service
{
    public const int NotificationId = 123;
    private const string ChannelId = "gpstrainer.tracking";

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var channel = new NotificationChannel(ChannelId, "gpsTrainer", NotificationImportance.Low);
            var manager = (NotificationManager?)GetSystemService(NotificationService);
            manager?.CreateNotificationChannel(channel);
        }

        var launchIntent = new Intent(this, typeof(MainActivity));
        launchIntent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        var pendingFlags = OperatingSystem.IsAndroidVersionAtLeast(23)
            ? PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable
            : PendingIntentFlags.UpdateCurrent;
        var pendingIntent = PendingIntent.GetActivity(this, 0, launchIntent, pendingFlags);

        var notification = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("gpsTrainer")
            .SetContentText("Tracking location")
            .SetSmallIcon(global::Android.Resource.Drawable.IcMenuMyLocation)
            .SetContentIntent(pendingIntent)
            .SetOngoing(true)
            .Build()!;

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            StartForeground(NotificationId, notification, global::Android.Content.PM.ForegroundService.TypeLocation);
        else
            StartForeground(NotificationId, notification);

        return StartCommandResult.Sticky;
    }

    public override IBinder? OnBind(Intent? intent) => null;
}

static class AndroidApplicationMarker
{
    public static void Touch() => _ = Application.Context;
}
