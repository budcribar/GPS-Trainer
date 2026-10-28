namespace GpsTrainer.Shared.Tests;

public class LearnedCourseTests
{
    [Fact]
    public void Finish_stores_the_ridden_track_and_queues_it_for_sync()
    {
        using var db = Open();
        var course = new Course { Name = "t2", Login = "bud", ElapsedDistanceKm = 0 };
        db.UpsertCourse(course);
        db.RecordAction(ActionType.InsertCourse, course.CourseId);
        var middle = GeoMath.Translate(40.0, -105.0, 0, 0.15);
        db.InsertWaypoint(new Waypoint
        {
            CourseId = course.CourseId,
            Latitude = middle.Latitude,
            Longitude = middle.Longitude,
            WaypointType = WaypointType.TurnLeft,
            TextDescription = "left",
            TimestampTicks = 15
        });

        var committed = LearnedCourse.Commit(db, course, Track(), latitude: null, longitude: null);

        Assert.True(committed);
        Assert.True(course.ElapsedDistanceKm > 0.2, "distance was " + course.ElapsedDistanceKm);
        var stored = db.ListWaypoints(course.CourseId);
        Assert.Equal(3, stored.Count);
        Assert.Equal(WaypointType.Start, stored[0].WaypointType);
        Assert.Equal(0, stored[0].ElapsedDistanceKm);
        Assert.Equal(WaypointType.TurnLeft, stored[1].WaypointType);
        Assert.Equal("left", stored[1].TextDescription);
        Assert.Equal(WaypointType.Finish, stored[^1].WaypointType);
        Assert.Null(CourseRules.Validate(stored));
        var saved = Assert.Single(db.ListCourses("bud"));
        Assert.Equal(course.ElapsedDistanceKm, saved.ElapsedDistanceKm);
        var actions = db.ListActions();
        Assert.Equal(ActionType.InsertCourse, actions[0].Action);
        Assert.Equal(ActionType.ModifyCourse, actions[1].Action);
        Assert.Equal(course.CourseId, actions[1].Id);
    }

    [Fact]
    public void A_course_that_already_has_a_route_is_left_alone()
    {
        using var db = Open();
        var course = new Course { Name = "Creek Loop", Login = "bud", ElapsedDistanceKm = 1.2 };
        db.UpsertCourse(course);
        db.ReplaceWaypoints(course.CourseId,
        [
            new Waypoint { CourseId = course.CourseId, WaypointType = WaypointType.Start, Latitude = 40, Longitude = -105, TimestampTicks = 1 },
            new Waypoint { CourseId = course.CourseId, WaypointType = WaypointType.Finish, Latitude = 40.01, Longitude = -105, ElapsedDistanceKm = 1.2, TimestampTicks = 2 }
        ]);

        var committed = LearnedCourse.Commit(db, course, Track(), latitude: null, longitude: null);

        Assert.False(committed);
        Assert.Equal(1.2, course.ElapsedDistanceKm);
        Assert.Equal(2, db.ListWaypoints(course.CourseId).Count);
        Assert.Empty(db.ListActions());
    }

    [Fact]
    public void An_empty_track_does_not_invent_a_course()
    {
        using var db = Open();
        var course = new Course { Name = "t2", Login = "bud" };
        db.UpsertCourse(course);

        Assert.False(LearnedCourse.Commit(db, course, [], latitude: null, longitude: null));
        Assert.Empty(db.ListWaypoints(course.CourseId));
        Assert.Empty(db.ListActions());
    }

    [Fact]
    public void The_recorder_keeps_a_moving_ride_that_finish_can_store()
    {
        var recorder = new RideRecorder();
        var when = new DateTime(2026, 10, 28, 15, 0, 0, DateTimeKind.Utc);
        var here = (Latitude: 40.0, Longitude: -105.0);
        Assert.True(recorder.Record(here.Latitude, here.Longitude, when));
        for (var i = 1; i <= 4; i++)
        {
            here = GeoMath.Translate(here.Latitude, here.Longitude, 90, 0.05);
            Assert.True(recorder.Record(here.Latitude, here.Longitude, when.AddSeconds(i)));
        }

        using var db = Open();
        var course = new Course { Name = "t2", Login = "bud" };
        db.UpsertCourse(course);
        Assert.True(LearnedCourse.Commit(db, course, recorder.Points, latitude: null, longitude: null));
        var stored = db.ListWaypoints(course.CourseId);
        Assert.Equal(5, stored.Count);
        Assert.Equal(WaypointType.Start, stored[0].WaypointType);
        Assert.Equal(WaypointType.Finish, stored[^1].WaypointType);
        Assert.True(Assert.Single(db.ListCourses()).ElapsedDistanceKm > 0.15);
    }

    private static List<TrackPoint> Track()
    {
        var start = (Latitude: 40.0, Longitude: -105.0);
        var middle = GeoMath.Translate(start.Latitude, start.Longitude, 0, 0.15);
        var end = GeoMath.Translate(middle.Latitude, middle.Longitude, 90, 0.15);
        return
        [
            new TrackPoint { Latitude = start.Latitude, Longitude = start.Longitude, TimestampTicks = 10 },
            new TrackPoint { Latitude = middle.Latitude, Longitude = middle.Longitude, TimestampTicks = 20 },
            new TrackPoint { Latitude = end.Latitude, Longitude = end.Longitude, TimestampTicks = 30 }
        ];
    }

    private static LocalDatabase Open()
    {
        var db = new LocalDatabase(Path.Combine(Path.GetTempPath(), "gpstrainer-learn-" + Guid.NewGuid().ToString("N") + ".db"));
        db.EnsureCreated();
        return db;
    }
}
