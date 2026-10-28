namespace GpsTrainer.Shared.Tests;

public class RideControlsTests
{
    [Theory]
    [InlineData(RunStateKind.WaitingForGps)]
    [InlineData(RunStateKind.WaitingForData)]
    [InlineData(RunStateKind.WaitingForStart)]
    [InlineData(RunStateKind.Finished)]
    [InlineData(RunStateKind.Aborted)]
    [InlineData(RunStateKind.Synchronizing)]
    [InlineData(RunStateKind.SynhronizingComplete)]
    public void Start_is_shown_until_a_ride_is_recording(RunStateKind state)
    {
        Assert.True(RideControls.ShowStart(state));
        Assert.False(RideControls.ShowFinish(state));
    }

    [Theory]
    [InlineData(RunStateKind.Learning)]
    [InlineData(RunStateKind.Racing)]
    public void Finish_is_shown_only_while_recording(RunStateKind state)
    {
        Assert.False(RideControls.ShowStart(state));
        Assert.True(RideControls.ShowFinish(state));
    }
}
