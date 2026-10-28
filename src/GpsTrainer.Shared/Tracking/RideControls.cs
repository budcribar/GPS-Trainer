namespace GpsTrainer;

/// <summary>
/// Which ride-screen buttons belong on screen. Finish is only for an active
/// recording. Start is only while that recording has not begun.
/// </summary>
public static class RideControls
{
    public static bool ShowStart(RunStateKind state) => !IsRecording(state);

    public static bool ShowFinish(RunStateKind state) => IsRecording(state);

    public static bool IsRecording(RunStateKind state) =>
        state is RunStateKind.Learning or RunStateKind.Racing;
}
