using GpsTrainer.Server.Data;
using GpsTrainer.Server.Security;
using Microsoft.EntityFrameworkCore;

namespace GpsTrainer.Server.Tests.Legacy;

public class DataLayerPortTests
{
    [Fact]
    public async Task TestGetActivityCodes()
    {
        using var db = new ThrowawayDb();
        await db.SeedActivityCodesAsync();
        var codes = await db.Db.ActivityCodes.OrderBy(c => c.ActKey).ToListAsync();
        Assert.Equal("Other", codes.Single(c => c.ActKey == 0).ActValue);
        Assert.Equal(9, codes.Count);
        Assert.Equal("Running", codes.Single(c => c.ActKey == 1).ActValue);
        Assert.Equal("Trail Running", codes.Single(c => c.ActKey == 2).ActValue);
        Assert.Equal("Cycling", codes.Single(c => c.ActKey == 3).ActValue);
        Assert.Equal("Mountain Biking", codes.Single(c => c.ActKey == 4).ActValue);
        Assert.Equal("XCountry Skate", codes.Single(c => c.ActKey == 5).ActValue);
        Assert.Equal("Race Walking", codes.Single(c => c.ActKey == 6).ActValue);
        Assert.Equal("Rowing", codes.Single(c => c.ActKey == 7).ActValue);
        Assert.Equal("XCountry Classic", codes.Single(c => c.ActKey == 8).ActValue);
    }

    [Fact]
    public async Task TestRacingCategories()
    {
        using var db = new ThrowawayDb();
        Assert.Empty(await db.Db.RacingCategories.ToListAsync());
        for (var i = 0; i < 12; i++)
        {
            db.Db.RacingCategories.Add(new RacingCategoryRow { CatKey = i, CatValue = "M" + i, Sex = "M" });
            db.Db.RacingCategories.Add(new RacingCategoryRow { CatKey = 100 + i, CatValue = "F" + i, Sex = "F" });
        }
        await db.Db.SaveChangesAsync();
        var cats = await db.Db.RacingCategories.ToListAsync();
        Assert.Equal(24, cats.Count);
        Assert.Equal(cats.Count(c => c.Sex == "M"), cats.Count(c => c.Sex == "F"));
    }

    [Fact]
    public void TestDatabaseIsClean()
    {
        using var db = new ThrowawayDb();
        Assert.Equal(0, db.UntrimmedLoginCount());
        Assert.Equal(0, db.ShortCourseCount());
        Assert.Equal(0, db.ShortRaceCount());
        Assert.Equal(0, db.OrphanWaypointCount());
        Assert.Equal(0, db.IllegalWaypointTicks());

        db.Db.Courses.Add(new CourseRow { CourseId = Guid.NewGuid(), Name = "", Login = "bud", Timestamp = DateTime.UtcNow, WaypointTimestamp = DateTime.UtcNow, ElapsedDistanceKm = 0.01 });
        db.Db.Waypoints.Add(new WaypointRow { CourseId = Guid.NewGuid(), TimestampTicks = 1, Latitude = 1, Longitude = 1 });
        db.Db.SaveChanges();
        Assert.Equal(1, db.ShortCourseCount());
        Assert.Equal(1, db.OrphanWaypointCount());
        Assert.Equal(1, db.DeleteUnnamedCourses());
        Assert.Equal(1, db.DeleteOrphanWaypoints());
        Assert.Equal(0, db.ShortCourseCount());
        Assert.Equal(0, db.OrphanWaypointCount());
    }

    [Fact]
    public async Task TestUserStatus_and_insert_round_trip()
    {
        using var db = new ThrowawayDb();
        Assert.Null(await db.Db.Users.FindAsync("nobody"));
        var before = DateTime.UtcNow;
        await db.AddUserAsync("test1");
        var user = await db.Db.Users.FindAsync("test1");
        Assert.NotNull(user);
        Assert.True(user!.IsActive);
        Assert.True(user.ExpirationDate > before);
        var stamp = user.UserTimestamp;
        user.City = "Boulder";
        user.UserTimestamp = DateTime.UtcNow.AddSeconds(1);
        await db.Db.SaveChangesAsync();
        var again = await db.Db.Users.FindAsync("test1");
        Assert.True(again!.UserTimestamp > stamp);
        Assert.True(again.UserTimestamp < stamp.AddMinutes(5));
        Assert.True(PasswordHasher.Verify("gps", again.PasswordHash));
        Assert.DoesNotContain("gps", again.PasswordHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestAllUsers_and_email_opt_out()
    {
        using var db = new ThrowawayDb();
        await db.AddUserAsync("a");
        await db.AddUserAsync("b");
        var users = await db.Db.Users.ToListAsync();
        Assert.True(users.Count > 1);
        Assert.Equal(users.Count, users.Count(u => u.WantsEmails));
        var row = await db.Db.Users.FindAsync("a");
        Assert.Equal(0, TurnOff(""));
        row!.WantsEmails = false;
        await db.Db.SaveChangesAsync();
        Assert.False((await db.Db.Users.FindAsync("a"))!.WantsEmails);
        Assert.Equal(1, users.Count(u => u.WantsEmails) - 0);
    }

    [Fact]
    public async Task TestSetC2DMRegistrationId_and_delete_user()
    {
        using var db = new ThrowawayDb();
        Assert.Equal(0, await SetPush(db, null, null));
        await db.AddUserAsync("test1");
        var id = "push-token-not-a-secret";
        Assert.Equal(1, await SetPush(db, "test1", id));
        Assert.Equal(id, (await db.Db.Users.FindAsync("test1"))!.PushToken);
        Assert.Equal(1, await SetPush(db, "test1", null));
        Assert.Null((await db.Db.Users.FindAsync("test1"))!.PushToken);
        db.Db.Users.Remove((await db.Db.Users.FindAsync("test1"))!);
        await db.Db.SaveChangesAsync();
        Assert.Null(await db.Db.Users.FindAsync("test1"));
    }

    [Fact]
    public async Task TestInitialUserLocation()
    {
        using var db = new ThrowawayDb();
        var missing = await db.Db.Users.FindAsync("missing");
        var lat = missing?.Latitude ?? ThrowawayDb.ColoradoLatitude;
        var lon = missing?.Longitude ?? ThrowawayDb.ColoradoLongitude;
        Assert.Equal(ThrowawayDb.ColoradoLatitude, lat);
        Assert.Equal(ThrowawayDb.ColoradoLongitude, lon);
        var user = await db.AddUserAsync("test1");
        user.Latitude = 40.01;
        user.Longitude = -105.27;
        await db.Db.SaveChangesAsync();
        var loaded = await db.Db.Users.FindAsync("test1");
        Assert.Equal(40.01, loaded!.Latitude);
        Assert.Equal(-105.27, loaded.Longitude);
    }

    [Fact]
    public void TestInsertCourse_and_download_grant()
    {
        using var db = new ThrowawayDb();
        var course = new CourseRow { CourseId = Guid.NewGuid(), Name = "name", Login = "", Timestamp = DateTime.UtcNow, WaypointTimestamp = DateTime.UtcNow };
        Assert.Equal(0, db.InsertCourse(course));
        course.Login = "test1";
        Assert.Equal(1, db.InsertCourse(course));
        Assert.Equal(0, db.InsertCourse(course));
        var loaded = db.Db.Courses.Find(course.CourseId);
        Assert.Equal(course.WaypointTimestamp, loaded!.WaypointTimestamp);
        Assert.False(db.DeleteCourse(course.CourseId, null));
        Assert.False(db.DeleteCourse(Guid.Empty, "test1"));
        Assert.False(db.DeleteCourse(course.CourseId, ""));
        Assert.Empty(db.Db.CourseDownloads.Where(d => d.Login == "test1"));
        db.Db.CourseDownloads.Add(new CourseDownload { Login = "test1", CourseId = course.CourseId });
        db.Db.SaveChanges();
        Assert.Single(db.Db.CourseDownloads.Where(d => d.Login == "test1"));
        Assert.True(db.DeleteCourse(course.CourseId, "test1"));
        Assert.Null(db.Db.Courses.Find(course.CourseId));
    }

    [Fact]
    public void TestWaypoints_validate_crash_and_willow()
    {
        using var db = new ThrowawayDb();
        var courseId = Guid.NewGuid();
        db.InsertCourse(new CourseRow { CourseId = courseId, Name = "w", Login = "test1", Timestamp = DateTime.UtcNow, WaypointTimestamp = DateTime.UtcNow });
        var bad = new Waypoint { TimestampTicks = -1, ElapsedDistanceKm = 1, Latitude = 40, Longitude = -105 };
        Assert.Equal(0, db.InsertWaypoint(courseId, bad));
        var pin = new Waypoint { TimestampTicks = 1, ElapsedDistanceKm = 0, Latitude = 40, Longitude = -105, WaypointType = WaypointType.Start, TextDescription = "Describe" };
        Assert.Equal(1, db.InsertWaypoint(courseId, pin));
        var row = db.Db.Waypoints.Single();
        row.TextDescription = "New Description";
        db.Db.SaveChanges();
        Assert.Equal("New Description", db.Db.Waypoints.Single().TextDescription);

    }

    [Fact]
    public async Task TestRaceCount_fastest_and_positions()
    {
        using var db = new ThrowawayDb();
        await db.AddUserAsync("test1");
        var courseId = Guid.NewGuid();
        var slow = new RaceRow { RaceId = Guid.NewGuid(), CourseId = courseId, Login = "test1", Publish = false, StartTime = DateTime.UtcNow, FinishTime = DateTime.UtcNow.AddMinutes(20), Timestamp = DateTime.UtcNow };
        var fast = new RaceRow { RaceId = Guid.NewGuid(), CourseId = courseId, Login = "test1", Publish = false, StartTime = slow.StartTime, FinishTime = slow.StartTime.AddMinutes(10), Timestamp = DateTime.UtcNow };
        Assert.Equal(0, db.RaceCount(courseId, null));
        Assert.Equal(0, db.InsertRace(new RaceRow { RaceId = Guid.NewGuid(), CourseId = courseId, Login = "" }));
        Assert.Equal(1, db.InsertRace(fast));
        Assert.Equal(0, db.InsertRace(fast));
        Assert.Equal(1, db.InsertRace(slow));
        Assert.Equal(2, db.RaceCount(courseId, "test1"));
        Assert.Equal(0, db.RaceCount(courseId, "xx"));
        Assert.Equal(fast.RaceId, db.FastestRace(courseId, "test1"));
        Assert.Equal(Guid.Empty, db.FastestRace(Guid.NewGuid(), "test1"));
        Assert.Equal(Guid.Empty, db.FastestRace(Guid.NewGuid(), null));
        Assert.Empty(db.Db.Positions.Where(p => p.RaceId == Guid.Empty));
        var missingRace = Guid.NewGuid();
        Assert.Equal(0, db.Db.Positions.Count(p => p.RaceId == missingRace));
        db.Db.Positions.Add(new PositionRow { RaceId = fast.RaceId, TimestampTicks = 1, Latitude = 40, Longitude = -105 });
        db.Db.Positions.Add(new PositionRow { RaceId = fast.RaceId, TimestampTicks = 2, Latitude = 40.01, Longitude = -105 });
        db.Db.SaveChanges();
        Assert.Equal(2, db.Db.Positions.Count(p => p.RaceId == fast.RaceId));
        Assert.True(db.RaceDistanceKm(fast.RaceId) > 0.5);
        Assert.Empty(db.Db.RaceDownloads.Where(d => d.Login == "test1"));
        db.Db.RaceDownloads.Add(new RaceDownload { Login = "test1", RaceId = fast.RaceId });
        db.Db.SaveChanges();
        Assert.Single(db.Db.RaceDownloads.Where(d => d.Login == "test1"));
    }

    [Fact]
    public void TestIntervals_persist_on_the_throwaway_database()
    {
        using var db = new ThrowawayDb();
        var courseId = Guid.NewGuid();
        db.InsertCourse(new CourseRow { CourseId = courseId, Name = "intervals", Login = "test1", Timestamp = DateTime.UtcNow, WaypointTimestamp = DateTime.UtcNow });
        var line = new List<Waypoint>();
        for (var i = 0; i < 4; i++)
        {
            var point = GeoMath.Translate(40, -105, 0, i);
            line.Add(new Waypoint
            {
                CourseId = courseId,
                TimestampTicks = i + 1,
                Latitude = point.Latitude,
                Longitude = point.Longitude,
                ElapsedDistanceKm = i,
                WaypointType = i == 0 ? WaypointType.Start : i == 3 ? WaypointType.Finish : WaypointType.Continue
            });
            db.InsertWaypoint(courseId, line[i]);
        }
        var placed = IntervalCourse.Place(line, courseId, "Climb", 1, 2);
        var interval = placed.Interval;
        db.Db.CourseIntervals.Add(new CourseIntervalRow
        {
            IntervalId = interval.IntervalId,
            CourseId = courseId,
            Ordinal = interval.Ordinal,
            Name = interval.Name,
            StartElapsedKm = interval.StartElapsedKm,
            FinishElapsedKm = interval.FinishElapsedKm,
            StartTimestampTicks = interval.StartTimestampTicks,
            FinishTimestampTicks = interval.FinishTimestampTicks,
            StartLatitude = interval.StartLatitude,
            StartLongitude = interval.StartLongitude,
            FinishLatitude = interval.FinishLatitude,
            FinishLongitude = interval.FinishLongitude
        });
        db.Db.SaveChanges();
        var loaded = db.Db.CourseIntervals.Single();
        Assert.Equal("Climb", loaded.Name);
        Assert.Equal(1, loaded.StartElapsedKm, 3);
        Assert.Equal(2, loaded.FinishElapsedKm, 3);
        Assert.Equal(courseId, loaded.CourseId);
    }

    [Fact]
    public void TestMaxLengths()
    {
        using var db = new ThrowawayDb();
        var model = db.Db.Model;
        Assert.Equal(ThrowawayDb.MaxCommentLength, model.FindEntityType(typeof(CourseRow))!.FindProperty(nameof(CourseRow.Comments))!.GetMaxLength());
        Assert.Equal(ThrowawayDb.MaxCommentLength, model.FindEntityType(typeof(RaceRow))!.FindProperty(nameof(RaceRow.Comments))!.GetMaxLength());
    }

    [Fact]
    public void TestWaypointDistances_on_a_tight_synthetic_course()
    {
        var pins = new List<Waypoint>();
        for (var i = 0; i < 5; i++)
            pins.Add(new Waypoint { TimestampTicks = i, Latitude = 40 + i * 0.0001, Longitude = -105, ElapsedDistanceKm = i * 0.01 });
        for (var i = 1; i < pins.Count; i++)
        {
            var meters = GeoMath.DistanceMeters(pins[i - 1].Latitude, pins[i - 1].Longitude, pins[i].Latitude, pins[i].Longitude);
            Assert.True(meters < 40);
        }
    }

    private static int TurnOff(string? login) => string.IsNullOrEmpty(login) ? 0 : 1;

    private static async Task<int> SetPush(ThrowawayDb db, string? login, string? token)
    {
        if (string.IsNullOrEmpty(login))
            return 0;
        var user = await db.Db.Users.FindAsync(login);
        if (user is null)
            return 0;
        user.PushToken = token;
        await db.Db.SaveChangesAsync();
        return 1;
    }
}
