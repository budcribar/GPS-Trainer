using GpsTrainer.Server.Api;
using GpsTrainer.Server.Data;
using GpsTrainer.Server.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace GpsTrainer.Server.Tests.Legacy;

public sealed class ThrowawayDb : IDisposable
{
    public const double ColoradoLatitude = 39.0646;
    public const double ColoradoLongitude = -105.3272;
    public const int MaxCommentLength = 256;

    private readonly string _path;
    public GpsTrainerDb Db { get; }

    public ThrowawayDb()
    {
        _path = Path.Combine(Path.GetTempPath(), "gpstrainer-legacy-" + Guid.NewGuid().ToString("N") + ".db");
        var options = new DbContextOptionsBuilder<GpsTrainerDb>().UseSqlite("Data Source=" + _path + ";Pooling=False").Options;
        Db = new GpsTrainerDb(options);
        Db.Database.EnsureCreated();
    }

    public async Task SeedActivityCodesAsync()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GpsTrainer:SeedInferredActivityCodes"] = "true"
        }).Build();
        await GpsEndpoints.SeedAsync(Db, config);
    }

    public async Task<UserRow> AddUserAsync(string login, string password = "gps", bool wantsEmails = true)
    {
        var row = new UserRow
        {
            Login = login,
            PasswordHash = PasswordHasher.Hash(password),
            FirstName = "First",
            LastName = "Last",
            Country = "US",
            City = "Fort Collins",
            State = "CO",
            Email = login + "@example.com",
            IsMale = true,
            WantsEmails = wantsEmails,
            IsActive = true,
            ExpirationDate = DateTime.UtcNow.AddYears(1),
            UserTimestamp = DateTime.UtcNow
        };
        Db.Users.Add(row);
        await Db.SaveChangesAsync();
        return row;
    }

    public int InsertCourse(CourseRow course)
    {
        if (string.IsNullOrWhiteSpace(course.Login))
            return 0;
        if (Db.Courses.Any(c => c.CourseId == course.CourseId))
            return 0;
        if (course.Timestamp == default)
            course.Timestamp = DateTime.UtcNow;
        if (course.WaypointTimestamp == default)
            course.WaypointTimestamp = course.Timestamp;
        Db.Courses.Add(course);
        Db.SaveChanges();
        return 1;
    }

    public bool DeleteCourse(Guid courseId, string? login)
    {
        if (courseId == Guid.Empty || string.IsNullOrEmpty(login))
            return false;
        var course = Db.Courses.Find(courseId);
        if (course is null || course.Login != login)
            return false;
        Db.Waypoints.RemoveRange(Db.Waypoints.Where(w => w.CourseId == courseId));
        Db.CourseDownloads.RemoveRange(Db.CourseDownloads.Where(d => d.CourseId == courseId));
        var races = Db.Races.Where(r => r.CourseId == courseId).ToList();
        foreach (var race in races)
        {
            Db.Positions.RemoveRange(Db.Positions.Where(p => p.RaceId == race.RaceId));
            Db.RaceDownloads.RemoveRange(Db.RaceDownloads.Where(d => d.RaceId == race.RaceId));
        }
        Db.Races.RemoveRange(races);
        Db.Courses.Remove(course);
        Db.SaveChanges();
        return true;
    }

    public int InsertWaypoint(Guid courseId, Waypoint pin)
    {
        if (pin.ElapsedDistanceKm < 0 || pin.TimestampTicks < 0)
            return 0;
        if (Db.Waypoints.Find(courseId, pin.TimestampTicks) is not null)
            return 0;
        Db.Waypoints.Add(new WaypointRow
        {
            CourseId = courseId,
            TimestampTicks = pin.TimestampTicks,
            Latitude = pin.Latitude,
            Longitude = pin.Longitude,
            Heading = pin.HeadingDegrees,
            WaypointType = (int)pin.WaypointType,
            AltitudeKm = pin.AltitudeKm,
            ElapsedDistanceKm = pin.ElapsedDistanceKm,
            TextDescription = pin.TextDescription
        });
        Db.SaveChanges();
        return 1;
    }

    public int InsertRace(RaceRow race)
    {
        if (string.IsNullOrWhiteSpace(race.Login) || !Db.Users.Any(u => u.Login == race.Login))
            return 0;
        if (Db.Races.Any(r => r.RaceId == race.RaceId))
            return 0;
        if (race.RaceId == Guid.Empty)
            race.RaceId = Guid.NewGuid();
        if (race.Timestamp == default)
            race.Timestamp = DateTime.UtcNow;
        Db.Races.Add(race);
        Db.SaveChanges();
        return 1;
    }

    public int RaceCount(Guid courseId, string? login)
    {
        if (string.IsNullOrEmpty(login))
            return 0;
        return Db.Races.Count(r => r.CourseId == courseId && (r.Login == login || r.Publish));
    }

    public Guid FastestRace(Guid courseId, string? login)
    {
        if (string.IsNullOrEmpty(login))
            return Guid.Empty;
        var race = Db.Races.Where(r => r.CourseId == courseId && (r.Login == login || r.Publish))
            .AsEnumerable()
            .OrderBy(r => r.FinishTime - r.StartTime)
            .FirstOrDefault();
        return race?.RaceId ?? Guid.Empty;
    }

    public int OrphanWaypointCount() =>
        Db.Waypoints.Count(w => !Db.Courses.Any(c => c.CourseId == w.CourseId));

    public int ShortCourseCount() => Db.Courses.Count(c => c.ElapsedDistanceKm < 0.05);

    public int ShortRaceCount() => Db.Races.Count(r => r.ElapsedDistanceKm < 0.05);

    public int IllegalWaypointTicks()
    {
        var limit = TimeSpan.FromDays(1).Ticks;
        return Db.Waypoints.Count(w => w.TimestampTicks > limit);
    }

    public int UntrimmedLoginCount() =>
        Db.Courses.Count(c => c.Login.EndsWith(' '))
        + Db.Races.Count(r => r.Login.EndsWith(' '))
        + Db.Users.Count(u => u.Login.EndsWith(' '));

    public int DeleteUnnamedCourses()
    {
        var rows = Db.Courses.Where(c => c.Name == "").ToList();
        Db.Courses.RemoveRange(rows);
        Db.SaveChanges();
        return rows.Count;
    }

    public int DeleteOrphanWaypoints()
    {
        var rows = Db.Waypoints.Where(w => !Db.Courses.Any(c => c.CourseId == w.CourseId)).ToList();
        Db.Waypoints.RemoveRange(rows);
        Db.SaveChanges();
        return rows.Count;
    }

    public double RaceDistanceKm(Guid raceId)
    {
        var points = Db.Positions.Where(p => p.RaceId == raceId).OrderBy(p => p.TimestampTicks).ToList();
        var total = 0.0;
        for (var i = 1; i < points.Count; i++)
            total += GeoMath.DistanceMeters(points[i - 1].Latitude, points[i - 1].Longitude, points[i].Latitude, points[i].Longitude);
        return total / 1000.0;
    }

    public void Dispose()
    {
        Db.Dispose();
        if (File.Exists(_path))
            File.Delete(_path);
    }
}
