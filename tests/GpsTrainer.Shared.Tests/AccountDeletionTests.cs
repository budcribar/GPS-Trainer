using GpsTrainer;

namespace GpsTrainer.Shared.Tests;

public class AccountDeletionTests
{
    [Fact]
    public void Blank_confirmation_does_not_call_the_server()
    {
        Assert.False(AccountDeletion.ShouldCallServer(null));
        Assert.False(AccountDeletion.ShouldCallServer(""));
        Assert.False(AccountDeletion.ShouldCallServer("   "));
        Assert.True(AccountDeletion.ShouldCallServer("DELETE"));
        Assert.True(AccountDeletion.ShouldCallServer("secret"));
    }

    [Theory]
    [InlineData(204, AccountDeletion.Result.Deleted, true)]
    [InlineData(400, AccountDeletion.Result.Rejected, false)]
    [InlineData(401, AccountDeletion.Result.Unauthorized, false)]
    [InlineData(403, AccountDeletion.Result.Unauthorized, false)]
    [InlineData(404, AccountDeletion.Result.Failed, false)]
    [InlineData(500, AccountDeletion.Result.Failed, false)]
    public void Only_a_successful_delete_clears_the_phone(int status, AccountDeletion.Result result, bool clears)
    {
        Assert.Equal(result, AccountDeletion.Interpret(status));
        Assert.Equal(clears, AccountDeletion.ClearsLocalData(result));
    }

    [Fact]
    public void Failure_messages_say_the_account_was_not_deleted()
    {
        Assert.Equal("Type your password or DELETE.", AccountDeletion.Message(AccountDeletion.Result.Rejected));
        Assert.Equal("The account was not deleted. Sign in again and try again.", AccountDeletion.Message(AccountDeletion.Result.Unauthorized));
        Assert.Equal("The account was not deleted. Check the connection and try again.", AccountDeletion.Message(AccountDeletion.Result.Failed));
        Assert.Contains("deleted", AccountDeletion.Message(AccountDeletion.Result.Deleted), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sign_out_drops_credentials_and_the_saved_api_address()
    {
        var keys = AccountDeletion.PreferenceKeys();
        Assert.Contains("SignIn", keys);
        Assert.Contains("Password", keys);
        Assert.Contains("ApiBaseUrl", keys);
        Assert.Contains("BackgroundLocationChoice", keys);
        Assert.Contains("CyclingUnits", keys);
        Assert.Contains("Mute", keys);
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Clearing_cached_data_removes_the_account_and_keeps_the_sport_list()
    {
        var path = Path.Combine(Path.GetTempPath(), "gpstrainer-delete-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using var db = new LocalDatabase(path);
            db.EnsureCreated();
            db.ReplaceActivityCodes([new ActivityCode { ActKey = 1, ActValue = "Cycling" }]);
            var course = new Course
            {
                Name = "Home loop",
                Login = "bud",
                ActivityCode = 1,
                Timestamp = new DateTime(2013, 10, 13, 0, 0, 0, DateTimeKind.Utc)
            };
            db.UpsertCourse(course);
            db.InsertWaypoint(new Waypoint
            {
                CourseId = course.CourseId,
                Latitude = 40.01,
                Longitude = -105.27,
                WaypointType = WaypointType.Start,
                TimestampTicks = 1
            });
            var race = new Race
            {
                CourseId = course.CourseId,
                Login = "bud",
                StartTime = DateTime.UnixEpoch,
                FinishTime = DateTime.UnixEpoch,
                Timestamp = DateTime.UnixEpoch
            };
            db.UpsertRace(race);
            db.InsertPosition(new TrackPoint { RaceId = race.RaceId, Latitude = 40.01, Longitude = -105.27, TimestampTicks = 2 });
            db.RecordAction(ActionType.InsertCourse, course.CourseId);
            db.UpsertUser(new UserAccount { Login = "bud", Password = "secret", ExpirationDate = DateTime.UnixEpoch });

            Assert.Contains("Users", AccountDeletion.CachedTables);
            Assert.Contains("Course", AccountDeletion.CachedTables);
            Assert.Contains("Waypoint", AccountDeletion.CachedTables);
            Assert.Contains("Race", AccountDeletion.CachedTables);
            Assert.Contains("Position", AccountDeletion.CachedTables);
            Assert.DoesNotContain("ActivityCodes", AccountDeletion.CachedTables);

            db.ClearCachedData();

            Assert.Null(db.FindUser("bud"));
            Assert.Empty(db.ListCourses());
            Assert.Empty(db.ListWaypoints(course.CourseId));
            Assert.Empty(db.ListRaces());
            Assert.Empty(db.ListPositions(race.RaceId));
            Assert.Empty(db.ListActions());
            Assert.Equal("Cycling", Assert.Single(db.ListActivityCodes()).ActValue);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
