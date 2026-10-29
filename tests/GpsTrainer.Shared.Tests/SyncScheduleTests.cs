namespace GpsTrainer.Shared.Tests;

public class SyncScheduleTests
{
    [Theory]
    [InlineData(SyncMoment.Opened)]
    [InlineData(SyncMoment.Foreground)]
    public void Opening_or_returning_syncs_when_the_app_is_in_front(SyncMoment moment)
    {
        Assert.True(SyncSchedule.Due(moment, inForeground: true, recording: false, alreadyRunning: false, signedIn: true, online: true, sinceLastAttempt: TimeSpan.Zero));
    }

    [Fact]
    public void The_timer_waits_five_minutes_and_only_runs_in_front()
    {
        var almost = SyncSchedule.Interval - TimeSpan.FromSeconds(1);
        Assert.False(SyncSchedule.Due(SyncMoment.Timer, true, false, false, true, true, almost));
        Assert.True(SyncSchedule.Due(SyncMoment.Timer, true, false, false, true, true, SyncSchedule.Interval));
        Assert.False(SyncSchedule.Due(SyncMoment.Timer, inForeground: false, false, false, true, true, SyncSchedule.Interval));
        Assert.False(SyncSchedule.Due(SyncMoment.Opened, inForeground: false, false, false, true, true, SyncSchedule.Interval));
    }

    [Theory]
    [InlineData(true, false, true, true)]
    [InlineData(false, true, true, true)]
    [InlineData(false, false, false, true)]
    [InlineData(false, false, true, false)]
    public void A_ride_an_overlap_or_a_quiet_skip_blocks_every_moment(bool recording, bool alreadyRunning, bool signedIn, bool online)
    {
        foreach (var moment in new[] { SyncMoment.Opened, SyncMoment.Foreground, SyncMoment.Timer })
        {
            Assert.False(SyncSchedule.Due(moment, true, recording, alreadyRunning, signedIn, online, SyncSchedule.Interval));
        }
    }
}
