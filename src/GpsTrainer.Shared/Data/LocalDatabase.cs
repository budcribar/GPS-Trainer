using Microsoft.Data.Sqlite;

namespace GpsTrainer;

/// <summary>Local gpsTrainer.db used by the app. Column names match the Android adapter.</summary>
public sealed class LocalDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly object _gate = new();

    public string FilePath { get; }

    public LocalDatabase(string path)
    {
        FilePath = path;
        var builder = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false };
        _connection = new SqliteConnection(builder.ConnectionString);
        _connection.Open();
    }

    public LocalDatabase(SqliteConnection openConnection)
    {
        _connection = openConnection;
        FilePath = openConnection.DataSource ?? "";
        if (_connection.State != System.Data.ConnectionState.Open)
            _connection.Open();
    }

    /// <summary>
    /// SQLite VACUUM on its own connection. busy_timeout makes it wait until
    /// other connections finish their writes.
    /// </summary>
    public static void Vacuum(string path, TimeSpan waitForWriters)
    {
        var wait = waitForWriters < TimeSpan.Zero ? TimeSpan.Zero : waitForWriters;
        var milliseconds = (int)Math.Min(int.MaxValue, wait.TotalMilliseconds);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ConnectionString);
        connection.Open();
        using var timeout = connection.CreateCommand();
        timeout.CommandText = "PRAGMA busy_timeout = " + milliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        timeout.ExecuteNonQuery();
        using var vacuum = connection.CreateCommand();
        vacuum.CommandTimeout = (int)Math.Min(int.MaxValue, Math.Ceiling(wait.TotalSeconds) + 15);
        vacuum.CommandText = "VACUUM";
        vacuum.ExecuteNonQuery();
    }

    public void EnsureCreated()
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            foreach (var sql in SchemaSql.All)
            {
                // SchemaSql keeps the recovered CREATE text. Repeat launches must not fail
                // with "table ActivityCodes already exists".
                cmd.CommandText = IdempotentCreate(sql);
                cmd.ExecuteNonQuery();
            }
            cmd.CommandText = "INSERT INTO Synchronization(SystemTimestamp, UserTimestamp) SELECT $s, $u WHERE NOT EXISTS (SELECT 1 FROM Synchronization)";
            cmd.Parameters.AddWithValue("$s", SoapDateTime.ToSoapString(DateTime.UnixEpoch));
            cmd.Parameters.AddWithValue("$u", SoapDateTime.ToSoapString(DateTime.UnixEpoch));
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<string> TableNames()
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
            using var reader = cmd.ExecuteReader();
            var names = new List<string>();
            while (reader.Read())
                names.Add(reader.GetString(0));
            return names;
        }
    }

    public void UpsertUser(UserAccount user)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Users(Login,Password,FirstName,LastName,Street,City,State,Country,email,PhoneNumber,Sex,BirthDate,RacingCategory,Longitude,Latitude,IsActive,WantsEmails,ExpirationDate)
                VALUES($login,$password,$first,$last,$street,$city,$state,$country,$email,$phone,$sex,$birth,$cat,$lon,$lat,$active,$emails,$exp)
                ON CONFLICT(Login) DO UPDATE SET
                  Password=$password, FirstName=$first, LastName=$last, Street=$street, City=$city, State=$state, Country=$country,
                  email=$email, PhoneNumber=$phone, Sex=$sex, BirthDate=$birth, RacingCategory=$cat, Longitude=$lon, Latitude=$lat,
                  IsActive=$active, WantsEmails=$emails, ExpirationDate=$exp
                """;
            BindUser(cmd, user);
            cmd.ExecuteNonQuery();
        }
    }

    public UserAccount? FindUser(string login)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT Login,Password,FirstName,LastName,Street,City,State,Country,email,PhoneNumber,Sex,BirthDate,RacingCategory,Longitude,Latitude,IsActive,WantsEmails,ExpirationDate FROM Users WHERE Login = $login";
            cmd.Parameters.AddWithValue("$login", login);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return null;
            return new UserAccount
            {
                Login = reader.GetString(0),
                Password = reader.GetString(1),
                FirstName = reader.GetString(2),
                LastName = reader.GetString(3),
                Street = reader.GetString(4),
                City = reader.GetString(5),
                State = reader.GetString(6),
                Country = reader.GetString(7),
                Email = reader.GetString(8),
                PhoneNumber = reader.GetString(9),
                IsMale = reader.GetString(10) == "M",
                BirthDate = SoapDateTime.Parse(reader.GetString(11)),
                RacingCategoryKey = reader.GetInt32(12),
                Longitude = reader.IsDBNull(13) ? null : reader.GetDouble(13),
                Latitude = reader.IsDBNull(14) ? null : reader.GetDouble(14),
                IsActive = reader.GetInt32(15) != 0,
                WantsEmails = !reader.IsDBNull(16) && reader.GetInt32(16) != 0,
                ExpirationDate = SoapDateTime.Parse(reader.GetString(17))
            };
        }
    }

    public bool SignIn(string login, string password)
    {
        var user = FindUser(login);
        return user is not null && user.Password == password;
    }

    public void UpsertCourse(Course course)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Course(CourseId,Name,ElapsedDistanceKM,Comments,AltitudeGainKM,ActivityCode,Publish,Login,Laps,AltitudeLossKM,WaypointsVerified,Timestamp,WaypointTimestamp)
                VALUES($id,$name,$dist,$comments,$gain,$activity,$publish,$login,$laps,$loss,$verified,$ts,$wts)
                ON CONFLICT(CourseId) DO UPDATE SET
                  Name=$name, ElapsedDistanceKM=$dist, Comments=$comments, AltitudeGainKM=$gain, ActivityCode=$activity,
                  Publish=$publish, Login=$login, Laps=$laps, AltitudeLossKM=$loss, WaypointsVerified=$verified,
                  Timestamp=$ts, WaypointTimestamp=$wts
                """;
            cmd.Parameters.AddWithValue("$id", course.CourseId.ToString());
            cmd.Parameters.AddWithValue("$name", course.Name);
            cmd.Parameters.AddWithValue("$dist", course.ElapsedDistanceKm);
            cmd.Parameters.AddWithValue("$comments", (object?)course.Comments ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$gain", course.AltitudeGainKm);
            cmd.Parameters.AddWithValue("$activity", course.ActivityCode);
            cmd.Parameters.AddWithValue("$publish", course.Publish ? 1 : 0);
            cmd.Parameters.AddWithValue("$login", course.Login);
            cmd.Parameters.AddWithValue("$laps", course.Laps);
            cmd.Parameters.AddWithValue("$loss", course.AltitudeLossKm);
            cmd.Parameters.AddWithValue("$verified", course.WaypointsVerified ? 1 : 0);
            cmd.Parameters.AddWithValue("$ts", SoapDateTime.ToSoapString(course.Timestamp));
            cmd.Parameters.AddWithValue("$wts", SoapDateTime.ToSoapString(course.WaypointTimestamp));
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<Course> ListCourses(string? login = null)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = login is null
                ? "SELECT CourseId,Name,ElapsedDistanceKM,Comments,AltitudeGainKM,ActivityCode,Publish,Login,Laps,AltitudeLossKM,WaypointsVerified,Timestamp,WaypointTimestamp FROM Course"
                : "SELECT CourseId,Name,ElapsedDistanceKM,Comments,AltitudeGainKM,ActivityCode,Publish,Login,Laps,AltitudeLossKM,WaypointsVerified,Timestamp,WaypointTimestamp FROM Course WHERE Login = $login";
            if (login is not null)
                cmd.Parameters.AddWithValue("$login", login);
            return ReadCourses(cmd);
        }
    }

    public void DeleteCourse(Guid courseId)
    {
        lock (_gate)
        {
            Execute("DELETE FROM Waypoint WHERE CourseId = $id", ("$id", courseId.ToString()));
            Execute("DELETE FROM Course WHERE CourseId = $id", ("$id", courseId.ToString()));
        }
    }

    public void ReplaceWaypoints(Guid courseId, IEnumerable<Waypoint> waypoints)
    {
        lock (_gate)
        {
            using var tx = _connection.BeginTransaction();
            Execute("DELETE FROM Waypoint WHERE CourseId = $id", ("$id", courseId.ToString()));
            foreach (var wp in waypoints)
            {
                wp.CourseId = courseId;
                InsertWaypoint(wp);
            }
            tx.Commit();
        }
    }

    public void InsertWaypoint(Waypoint wp)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Waypoint(Longitude,Latitude,Heading,CourseId,WaypointType,AltitudeKM,TimestampTicks,ElapsedDistanceKM,TextDescription)
                VALUES($lon,$lat,$heading,$course,$type,$alt,$ticks,$dist,$text)
                """;
            cmd.Parameters.AddWithValue("$lon", wp.Longitude);
            cmd.Parameters.AddWithValue("$lat", wp.Latitude);
            cmd.Parameters.AddWithValue("$heading", wp.HeadingDegrees);
            cmd.Parameters.AddWithValue("$course", wp.CourseId.ToString());
            cmd.Parameters.AddWithValue("$type", (int)wp.WaypointType);
            cmd.Parameters.AddWithValue("$alt", wp.AltitudeKm);
            cmd.Parameters.AddWithValue("$ticks", wp.TimestampTicks);
            cmd.Parameters.AddWithValue("$dist", wp.ElapsedDistanceKm);
            cmd.Parameters.AddWithValue("$text", (object?)wp.TextDescription ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<Waypoint> ListWaypoints(Guid courseId)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT Longitude,Latitude,Heading,CourseId,WaypointType,AltitudeKM,TimestampTicks,ElapsedDistanceKM,TextDescription FROM Waypoint WHERE CourseId = $id ORDER BY TimestampTicks";
            cmd.Parameters.AddWithValue("$id", courseId.ToString());
            using var reader = cmd.ExecuteReader();
            var list = new List<Waypoint>();
            while (reader.Read())
            {
                list.Add(new Waypoint
                {
                    Longitude = reader.GetDouble(0),
                    Latitude = reader.GetDouble(1),
                    HeadingDegrees = reader.GetDouble(2),
                    CourseId = Guid.Parse(reader.GetString(3)),
                    WaypointType = (WaypointType)reader.GetInt32(4),
                    AltitudeKm = reader.GetDouble(5),
                    TimestampTicks = reader.GetInt64(6),
                    ElapsedDistanceKm = reader.GetDouble(7),
                    TextDescription = reader.IsDBNull(8) ? null : reader.GetString(8)
                });
            }
            return list;
        }
    }

    public void UpsertRace(Race race)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Race(ElapsedTimeTicks,CourseId,RaceId,Comments,GoalTimeTicks,StartTime,FinishTime,StartPositionTicks,ElapsedDistanceKM,AltitudeGainKM,TargetRaceId,Publish,Login,AltitudeLossKM,Timestamp)
                VALUES($elapsed,$course,$id,$comments,$goal,$start,$finish,$startTicks,$dist,$gain,$target,$publish,$login,$loss,$ts)
                ON CONFLICT(RaceId) DO UPDATE SET
                  ElapsedTimeTicks=$elapsed, CourseId=$course, Comments=$comments, GoalTimeTicks=$goal, StartTime=$start, FinishTime=$finish,
                  StartPositionTicks=$startTicks, ElapsedDistanceKM=$dist, AltitudeGainKM=$gain, TargetRaceId=$target, Publish=$publish,
                  Login=$login, AltitudeLossKM=$loss, Timestamp=$ts
                """;
            cmd.Parameters.AddWithValue("$elapsed", race.ElapsedTimeTicks);
            cmd.Parameters.AddWithValue("$course", race.CourseId.ToString());
            cmd.Parameters.AddWithValue("$id", race.RaceId.ToString());
            cmd.Parameters.AddWithValue("$comments", (object?)race.Comments ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$goal", race.GoalTimeTicks);
            cmd.Parameters.AddWithValue("$start", SoapDateTime.ToSoapString(race.StartTime));
            cmd.Parameters.AddWithValue("$finish", SoapDateTime.ToSoapString(race.FinishTime));
            cmd.Parameters.AddWithValue("$startTicks", race.StartPositionTicks);
            cmd.Parameters.AddWithValue("$dist", race.ElapsedDistanceKm);
            cmd.Parameters.AddWithValue("$gain", race.AltitudeGainKm);
            cmd.Parameters.AddWithValue("$target", race.TargetRaceId.ToString());
            cmd.Parameters.AddWithValue("$publish", race.Publish ? 1 : 0);
            cmd.Parameters.AddWithValue("$login", race.Login);
            cmd.Parameters.AddWithValue("$loss", race.AltitudeLossKm);
            cmd.Parameters.AddWithValue("$ts", SoapDateTime.ToSoapString(race.Timestamp));
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<Race> ListRaces(Guid? courseId = null)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = courseId is null
                ? "SELECT RaceId,CourseId,Comments,GoalTimeTicks,ElapsedTimeTicks,StartTime,FinishTime,StartPositionTicks,ElapsedDistanceKM,AltitudeGainKM,AltitudeLossKM,TargetRaceId,Publish,Login,Timestamp FROM Race"
                : "SELECT RaceId,CourseId,Comments,GoalTimeTicks,ElapsedTimeTicks,StartTime,FinishTime,StartPositionTicks,ElapsedDistanceKM,AltitudeGainKM,AltitudeLossKM,TargetRaceId,Publish,Login,Timestamp FROM Race WHERE CourseId = $id";
            if (courseId is not null)
                cmd.Parameters.AddWithValue("$id", courseId.Value.ToString());
            using var reader = cmd.ExecuteReader();
            var list = new List<Race>();
            while (reader.Read())
            {
                list.Add(new Race
                {
                    RaceId = Guid.Parse(reader.GetString(0)),
                    CourseId = Guid.Parse(reader.GetString(1)),
                    Comments = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    GoalTimeTicks = reader.GetInt64(3),
                    ElapsedTimeTicks = reader.GetInt64(4),
                    StartTime = SoapDateTime.Parse(reader.GetString(5)),
                    FinishTime = SoapDateTime.Parse(reader.GetString(6)),
                    StartPositionTicks = reader.GetInt64(7),
                    ElapsedDistanceKm = reader.GetDouble(8),
                    AltitudeGainKm = reader.GetDouble(9),
                    AltitudeLossKm = reader.GetDouble(10),
                    TargetRaceId = Guid.Parse(reader.GetString(11)),
                    Publish = reader.GetInt32(12) != 0,
                    Login = reader.GetString(13),
                    Timestamp = SoapDateTime.Parse(reader.GetString(14))
                });
            }
            return list;
        }
    }

    public void DeleteRace(Guid raceId)
    {
        lock (_gate)
        {
            Execute("DELETE FROM Position WHERE RaceID = $id", ("$id", raceId.ToString()));
            Execute("DELETE FROM Race WHERE RaceId = $id", ("$id", raceId.ToString()));
        }
    }

    public void InsertPosition(TrackPoint point)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO Position(Latitude,Longitude,TimestampTicks,RaceID) VALUES($lat,$lon,$ticks,$race)";
            cmd.Parameters.AddWithValue("$lat", point.Latitude);
            cmd.Parameters.AddWithValue("$lon", point.Longitude);
            cmd.Parameters.AddWithValue("$ticks", point.TimestampTicks);
            cmd.Parameters.AddWithValue("$race", point.RaceId.ToString());
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// The finished ride is the whole ghost. Earlier points on this race are removed
    /// so a second finish does not leave the previous track mixed in.
    /// </summary>
    public void ReplacePositions(Guid raceId, IReadOnlyList<TrackPoint> points)
    {
        lock (_gate)
        {
            using var tx = _connection.BeginTransaction();
            using (var delete = _connection.CreateCommand())
            {
                delete.Transaction = tx;
                delete.CommandText = "DELETE FROM Position WHERE RaceID = $id";
                delete.Parameters.AddWithValue("$id", raceId.ToString());
                delete.ExecuteNonQuery();
            }
            foreach (var point in points)
            {
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO Position(Latitude,Longitude,TimestampTicks,RaceID) VALUES($lat,$lon,$ticks,$race)";
                cmd.Parameters.AddWithValue("$lat", point.Latitude);
                cmd.Parameters.AddWithValue("$lon", point.Longitude);
                cmd.Parameters.AddWithValue("$ticks", point.TimestampTicks);
                cmd.Parameters.AddWithValue("$race", raceId.ToString());
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    public IReadOnlyList<TrackPoint> ListPositions(Guid raceId)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT Latitude,Longitude,TimestampTicks,RaceID FROM Position WHERE RaceID = $id ORDER BY TimestampTicks";
            cmd.Parameters.AddWithValue("$id", raceId.ToString());
            using var reader = cmd.ExecuteReader();
            var list = new List<TrackPoint>();
            while (reader.Read())
            {
                list.Add(new TrackPoint
                {
                    Latitude = reader.GetDouble(0),
                    Longitude = reader.GetDouble(1),
                    TimestampTicks = reader.GetInt64(2),
                    RaceId = Guid.Parse(reader.GetString(3))
                });
            }
            return list;
        }
    }

    public void RecordAction(ActionType action, Guid id)
    {
        Execute("INSERT INTO SyncAction(ActionCode, Id) VALUES($code, $id)", ("$code", (int)action), ("$id", id.ToString()));
    }

    public IReadOnlyList<SyncActionRecord> ListActions()
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT rowid, ActionCode, Id FROM SyncAction ORDER BY rowid";
            using var reader = cmd.ExecuteReader();
            var list = new List<SyncActionRecord>();
            while (reader.Read())
            {
                list.Add(new SyncActionRecord
                {
                    RowId = reader.GetInt64(0),
                    Action = (ActionType)reader.GetInt32(1),
                    Id = Guid.Parse(reader.GetString(2))
                });
            }
            return list;
        }
    }

    public void DeleteAction(long rowId) => Execute("DELETE FROM SyncAction WHERE rowid = $id", ("$id", rowId));

    /// <summary>
    /// Drops the on-device copy of the account: courses, waypoints, rides, location trails, and the saved login.
    /// Sport names and racing categories stay so a later sign-in still has the lookup lists.
    /// </summary>
    public void ClearCachedData()
    {
        lock (_gate)
        {
            foreach (var table in AccountDeletion.CachedTables)
            {
                if (Array.IndexOf(SchemaSql.TableNames, table) < 0)
                    throw new InvalidOperationException("Unknown cached table " + table);
                Execute("DELETE FROM " + table);
            }
        }
    }

    public void SetUserTimestamp(DateTime timestamp)
    {
        Execute("UPDATE Synchronization SET UserTimestamp = $u", ("$u", SoapDateTime.ToSoapString(timestamp)));
    }

    public DateTime GetUserTimestamp()
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT UserTimestamp FROM Synchronization LIMIT 1";
            var value = cmd.ExecuteScalar() as string;
            return value is null ? DateTime.UnixEpoch : SoapDateTime.Parse(value);
        }
    }

    public void ReplaceActivityCodes(IEnumerable<ActivityCode> codes)
    {
        lock (_gate)
        {
            Execute("DELETE FROM ActivityCodes");
            foreach (var code in codes)
                Execute("INSERT INTO ActivityCodes(ActKey, ActValue) VALUES($k, $v)", ("$k", code.ActKey), ("$v", code.ActValue));
        }
    }

    public IReadOnlyList<ActivityCode> ListActivityCodes()
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT ActKey, ActValue FROM ActivityCodes ORDER BY ActKey";
            using var reader = cmd.ExecuteReader();
            var list = new List<ActivityCode>();
            while (reader.Read())
                list.Add(new ActivityCode { ActKey = reader.GetInt32(0), ActValue = reader.GetString(1) });
            return list;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _connection.Dispose();
        }
    }

    private static void BindUser(SqliteCommand cmd, UserAccount user)
    {
        cmd.Parameters.AddWithValue("$login", user.Login);
        cmd.Parameters.AddWithValue("$password", user.Password);
        cmd.Parameters.AddWithValue("$first", user.FirstName);
        cmd.Parameters.AddWithValue("$last", user.LastName);
        cmd.Parameters.AddWithValue("$street", user.Street);
        cmd.Parameters.AddWithValue("$city", user.City);
        cmd.Parameters.AddWithValue("$state", user.State);
        cmd.Parameters.AddWithValue("$country", user.Country);
        cmd.Parameters.AddWithValue("$email", user.Email);
        cmd.Parameters.AddWithValue("$phone", user.PhoneNumber);
        cmd.Parameters.AddWithValue("$sex", user.Gender);
        cmd.Parameters.AddWithValue("$birth", SoapDateTime.ToSoapString(user.BirthDate));
        cmd.Parameters.AddWithValue("$cat", user.RacingCategoryKey);
        cmd.Parameters.AddWithValue("$lon", (object?)user.Longitude ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$lat", (object?)user.Latitude ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$active", user.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("$emails", user.WantsEmails ? 1 : 0);
        cmd.Parameters.AddWithValue("$exp", SoapDateTime.ToSoapString(user.ExpirationDate));
    }

    private List<Course> ReadCourses(SqliteCommand cmd)
    {
        using var reader = cmd.ExecuteReader();
        var list = new List<Course>();
        while (reader.Read())
        {
            list.Add(new Course
            {
                CourseId = Guid.Parse(reader.GetString(0)),
                Name = reader.GetString(1),
                ElapsedDistanceKm = reader.GetDouble(2),
                Comments = reader.IsDBNull(3) ? "" : reader.GetString(3),
                AltitudeGainKm = reader.GetDouble(4),
                ActivityCode = reader.GetInt32(5),
                Publish = reader.GetInt32(6) != 0,
                Login = reader.GetString(7),
                Laps = reader.GetInt32(8),
                AltitudeLossKm = reader.GetDouble(9),
                WaypointsVerified = reader.GetInt32(10) != 0,
                Timestamp = SoapDateTime.Parse(reader.GetString(11)),
                WaypointTimestamp = SoapDateTime.Parse(reader.GetString(12))
            });
        }
        return list;
    }

    internal static string IdempotentCreate(string sql)
    {
        const string create = "CREATE TABLE";
        if (sql.StartsWith(create, StringComparison.OrdinalIgnoreCase)
            && !sql.Contains("IF NOT EXISTS", StringComparison.OrdinalIgnoreCase))
            return "CREATE TABLE IF NOT EXISTS" + sql[create.Length..];
        return sql;
    }

    private void Execute(string sql, params (string Name, object Value)[] parameters)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (name, value) in parameters)
                cmd.Parameters.AddWithValue(name, value);
            cmd.ExecuteNonQuery();
        }
    }
}
