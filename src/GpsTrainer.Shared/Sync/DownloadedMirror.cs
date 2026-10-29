namespace GpsTrainer;

/// <summary>
/// Local courses and races the server no longer lists. A pending upload keeps
/// its row. Nothing is removed while a ride is recording.
/// </summary>
public static class DownloadedMirror
{
    public static HashSet<Guid> PendingCourseIds(IEnumerable<SyncActionRecord> actions)
    {
        var ids = new HashSet<Guid>();
        foreach (var action in actions)
        {
            if (action.Action is ActionType.InsertCourse or ActionType.ModifyCourse
                or ActionType.DeleteCourse or ActionType.DeleteDownloadCourse
                or ActionType.InsertDownloadCourse)
                ids.Add(action.Id);
        }
        return ids;
    }

    public static HashSet<Guid> ProtectedCourseIds(
        IEnumerable<SyncActionRecord> actions,
        IEnumerable<(Guid RaceId, Guid CourseId)> races)
    {
        var ids = PendingCourseIds(actions);
        var pendingRaces = PendingRaceIds(actions);
        foreach (var (raceId, courseId) in races)
        {
            if (pendingRaces.Contains(raceId))
                ids.Add(courseId);
        }
        return ids;
    }

    public static HashSet<Guid> PendingRaceIds(IEnumerable<SyncActionRecord> actions)
    {
        var ids = new HashSet<Guid>();
        foreach (var action in actions)
        {
            if (action.Action is ActionType.InsertRace or ActionType.ModifyRace
                or ActionType.DeleteRace or ActionType.DeleteDownloadRace
                or ActionType.InsertDownloadRace)
                ids.Add(action.Id);
        }
        return ids;
    }

    public static List<Guid> IdsToRemove(
        IEnumerable<Guid> localIds,
        IEnumerable<Guid> serverIds,
        IEnumerable<Guid> pendingIds,
        bool recording)
    {
        if (recording)
            return [];
        var server = serverIds.ToHashSet();
        var pending = pendingIds.ToHashSet();
        var drop = new List<Guid>();
        foreach (var id in localIds)
        {
            if (server.Contains(id) || pending.Contains(id) || drop.Contains(id))
                continue;
            drop.Add(id);
        }
        return drop;
    }
}
