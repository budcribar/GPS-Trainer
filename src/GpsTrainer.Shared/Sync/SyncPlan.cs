namespace GpsTrainer;

/// <summary>
/// SOAP calls <c>Synchronize.executionActions</c> makes for each <see cref="ActionType"/>.
/// Codes 8 and 9 fall out of the switch and only delete the local action row.
/// </summary>
public static class SyncPlan
{
    public static IReadOnlyList<string> CallsFor(ActionType action) => action switch
    {
        ActionType.DeleteCourse => ["DeleteCourse"],
        ActionType.DeleteRace => ["DeleteRace"],
        ActionType.DeleteDownloadCourse => ["InhibitCourseDownload"],
        ActionType.DeleteDownloadRace => ["InhibitRaceDownload"],
        ActionType.ModifyCourse => ["ClientUpdateCourse"],
        ActionType.ModifyRace => ["UpdateRace"],
        ActionType.InsertCourse => ["InsertCourse", "EnableCourseDownload", "InsertWaypoints"],
        ActionType.InsertRace => ["InsertRace", "EnableRaceDownload", "InsertPositions"],
        ActionType.InsertDownloadCourse => [],
        ActionType.InsertDownloadRace => [],
        _ => []
    };

    public static bool RequiresActiveUser(ActionType _) => true;
}
