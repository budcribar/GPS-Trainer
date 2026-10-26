using Android.App;
using Android.Bluetooth;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using Application = Android.App.Application;

namespace GpsTrainer.App;

public sealed partial class PackageAudioPlayer
{
    private static partial Task PlayFileAsync(string path, bool loop)
    {
        var player = new Android.Media.MediaPlayer();
        player.SetDataSource(path);
        player.Looping = loop;
        player.Prepare();
        player.Start();
        return Task.CompletedTask;
    }
}

public sealed class AndroidBluetooth : IBluetoothDevices
{
    public bool IsAudioDevicePaired() => Names().Count > 0;

    public IReadOnlyList<string> PairedAudioDeviceNames() => Names();

    private static List<string> Names()
    {
        var names = new List<string>();
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

        var notification = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("gpsTrainer")
            .SetContentText("Tracking location")
            .SetSmallIcon(global::Android.Resource.Drawable.IcMenuMyLocation)
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
