namespace GpsTrainer.App;

/// <summary>
/// Live location for the GPS Info page. A ride does not have to be running.
/// Android reads the location manager; the page only renders <see cref="GpsInfoText"/>.
/// </summary>
public interface IGpsInfoFeed
{
    event EventHandler<GpsInfoUpdate>? Updated;

    Task StartAsync();

    void Stop();

    void OpenSettings();
}

public sealed record GpsInfoUpdate(GpsInfoBlocker Blocker, GpsInfoSample? Sample);
