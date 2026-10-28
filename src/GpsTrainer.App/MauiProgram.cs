using Microsoft.Extensions.Logging;

namespace GpsTrainer.App;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		builder.Services.AddSingleton<AppSession>();
		builder.Services.AddSingleton<TrainerRepository>();
		builder.Services.AddSingleton<TrainerApi>();
		builder.Services.AddSingleton<SyncService>();
		builder.Services.AddSingleton<DatabaseMaintenance>();
		builder.Services.AddSingleton<RunSession>();
		builder.Services.AddSingleton<IAudioCuePlayer, PackageAudioPlayer>();
		builder.Services.AddSingleton<SpeechFeedback>();
		builder.Services.AddSingleton<IPushNotifications, PlaceholderPush>();
		builder.Services.AddSingleton<TrackingHost>();
#if ANDROID
		builder.Services.AddSingleton<IBluetoothDevices, AndroidBluetooth>();
		builder.Services.AddSingleton<IGpsInfoFeed, AndroidGpsInfoFeed>();
#elif IOS
		builder.Services.AddSingleton<IBluetoothDevices, IosBluetooth>();
		builder.Services.AddSingleton<IGpsInfoFeed, IosGpsInfoFeed>();
#else
		builder.Services.AddSingleton<IBluetoothDevices, UnavailableBluetooth>();
#endif

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
