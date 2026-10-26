namespace GpsTrainer;

/// <summary>Ordinal values match <c>WaypointType</c> and are stored in the Waypoint table.</summary>
public enum WaypointType
{
    Arrive = 0,
    BearLeft = 1,
    BearRight = 2,
    TurnAround = 3,
    TurnLeft = 4,
    TurnRight = 5,
    Reserved1 = 6,
    Reserved2 = 7,
    Reserved3 = 8,
    Reserved4 = 9,
    Reserved5 = 10,
    Start = 11,
    Finish = 12,
    StartInterval = 13,
    FinishInterval = 14,
    Continue = 15,
    Summit = 16,
    FeedZone = 17,
    Announcement = 18
}

/// <summary>Ordinal values are <c>SyncAction.ActionCode</c>.</summary>
public enum ActionType
{
    DeleteCourse = 0,
    DeleteRace = 1,
    DeleteDownloadCourse = 2,
    DeleteDownloadRace = 3,
    ModifyCourse = 4,
    ModifyRace = 5,
    InsertCourse = 6,
    InsertRace = 7,
    InsertDownloadCourse = 8,
    InsertDownloadRace = 9
}

/// <summary>
/// Original spelling <c>SynhronizingComplete</c> is preserved as <see cref="SynhronizingComplete"/>.
/// </summary>
public enum RunStateKind
{
    WaitingForGps,
    WaitingForData,
    WaitingForStart,
    Racing,
    Learning,
    Finished,
    Aborted,
    Synchronizing,
    SynhronizingComplete
}

public enum AudioCue
{
    Beep,
    StartBeep,
    Start54321,
    Silence
}
