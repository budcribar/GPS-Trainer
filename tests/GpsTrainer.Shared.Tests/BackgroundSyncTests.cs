namespace GpsTrainer.Shared.Tests;

public class BackgroundSyncTests
{
    [Fact]
    public void A_finished_ride_with_a_signed_in_online_user_may_sync()
    {
        Assert.True(BackgroundSync.ShouldRun(recording: false, alreadyRunning: false, signedIn: true, online: true));
    }

    [Theory]
    [InlineData(true, false, true, true)]
    [InlineData(false, true, true, true)]
    [InlineData(false, false, false, true)]
    [InlineData(false, false, true, false)]
    public void Sync_stays_quiet_while_recording_busy_signed_out_or_offline(bool recording, bool alreadyRunning, bool signedIn, bool online)
    {
        Assert.False(BackgroundSync.ShouldRun(recording, alreadyRunning, signedIn, online));
    }

    [Fact]
    public void An_inactive_or_expired_license_does_not_allow_sync()
    {
        var now = new DateTime(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc);
        Assert.True(BackgroundSync.LicenseAllows(true, now, now));
        Assert.False(BackgroundSync.LicenseAllows(false, now.AddDays(1), now));
        Assert.False(BackgroundSync.LicenseAllows(true, now.AddMinutes(-1), now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_login_is_signed_out(string? login)
    {
        Assert.False(BackgroundSync.IsSignedIn(login));
    }

    [Fact]
    public void Finish_saves_the_track_before_it_queues_synchronize()
    {
        var screens = File.ReadAllText(RepoFile("src/GpsTrainer.App/Pages/Screens.cs"));
        var finish = screens.IndexOf("Ui.Command(\"Finish\"", StringComparison.Ordinal);
        var save = screens.IndexOf("await SaveTrace(run);", finish, StringComparison.Ordinal);
        var sync = screens.IndexOf("SynchronizeAfterFinishAsync", save, StringComparison.Ordinal);
        Assert.True(finish >= 0 && save > finish && sync > save);
    }

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(relative);
    }
}
