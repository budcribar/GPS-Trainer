namespace GpsTrainer.Shared.Tests;

public class DownloadedMirrorTests
{
    [Fact]
    public void A_course_the_server_dropped_is_removed_with_its_waypoints()
    {
        using var db = Open();
        var kept = Course(db, "kept");
        var gone = Course(db, "gone");
        db.InsertWaypoint(Pin(gone));

        var drop = DownloadedMirror.IdsToRemove(
            db.ListCourses().Select(course => course.CourseId),
            [kept],
            pendingIds: [],
            recording: false);

        Assert.Equal([gone], drop);
        db.DeleteCourse(gone);
        Assert.Empty(db.ListWaypoints(gone));
        Assert.Equal(kept, Assert.Single(db.ListCourses()).CourseId);
    }

    [Fact]
    public void A_pending_upload_and_a_ride_keep_the_local_course()
    {
        var server = new[] { Guid.NewGuid() };
        var pendingCourse = Guid.NewGuid();
        var plain = Guid.NewGuid();
        var actions = new[]
        {
            new SyncActionRecord { Action = ActionType.ModifyCourse, Id = pendingCourse },
            new SyncActionRecord { Action = ActionType.InsertRace, Id = Guid.NewGuid() }
        };
        var pending = DownloadedMirror.PendingCourseIds(actions);

        var drop = DownloadedMirror.IdsToRemove([server[0], pendingCourse, plain], server, pending, recording: false);
        Assert.Equal([plain], drop);
        Assert.Empty(DownloadedMirror.IdsToRemove([plain], server, pending, recording: true));
    }

    [Fact]
    public void A_pending_race_is_not_removed_and_protects_its_course_id_when_added()
    {
        var race = Guid.NewGuid();
        var course = Guid.NewGuid();
        var actions = new[] { new SyncActionRecord { Action = ActionType.InsertRace, Id = race } };
        var pendingRaces = DownloadedMirror.PendingRaceIds(actions);
        Assert.Empty(DownloadedMirror.IdsToRemove([race], serverIds: [], pendingRaces, recording: false));
        Assert.Empty(DownloadedMirror.IdsToRemove([race], serverIds: [], pendingRaces, recording: true));

        var pendingCourses = DownloadedMirror.ProtectedCourseIds(actions, [(race, course)]);
        Assert.Contains(course, pendingCourses);
        Assert.Empty(DownloadedMirror.IdsToRemove([course], serverIds: [], pendingCourses, recording: false));
    }

    private static Guid Course(LocalDatabase db, string name)
    {
        var course = new Course { Name = name, Login = "bud", Timestamp = DateTime.UnixEpoch };
        db.UpsertCourse(course);
        return course.CourseId;
    }

    private static Waypoint Pin(Guid courseId) => new()
    {
        CourseId = courseId,
        Latitude = 40,
        Longitude = -105,
        WaypointType = WaypointType.Start,
        TimestampTicks = 1
    };

    private static LocalDatabase Open()
    {
        var db = new LocalDatabase(Path.Combine(Path.GetTempPath(), "gpstrainer-mirror-" + Guid.NewGuid().ToString("N") + ".db"));
        db.EnsureCreated();
        return db;
    }
}
