using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using GpsTrainer.Server.Data;
using GpsTrainer.Server.Security;
using Microsoft.EntityFrameworkCore;

namespace GpsTrainer.Server.Api;

public static partial class GpsEndpoints
{
    private static void MapWebsite(RouteGroupBuilder api)
    {
        api.MapGet("/site", (IConfiguration config) => Results.Ok(new SiteInfoDto
        {
            Domain = config["GpsTrainer:PublicHost"] ?? SiteIdentity.Domain,
            Url = config["GpsTrainer:PublicUrl"] ?? SiteIdentity.PublicUrl,
            GooglePlayUrl = StoreUrl(config["Stores:GooglePlayUrl"]),
            AppStoreUrl = StoreUrl(config["Stores:AppStoreUrl"])
        }));

        static string StoreUrl(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";
            var trimmed = value.Trim();
            return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                ? trimmed
                : "";
        }

        api.MapPost("/auth/password", async (PasswordChangeRequest body, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            if (!TryLogin(http, config, out var login))
                return Results.Unauthorized();
            var user = await db.Users.FindAsync(login);
            var current = body.CurrentPassword?.TrimEnd() ?? "";
            var next = body.NewPassword?.TrimEnd() ?? "";
            if (user is null || !PasswordHasher.Verify(current, user.PasswordHash))
                return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(next))
                return Results.BadRequest();
            user.PasswordHash = PasswordHasher.Hash(next);
            user.UserTimestamp = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapPost("/auth/forgot", async (ForgotPasswordRequest body, GpsTrainerDb db, IConfiguration config) =>
        {
            var response = new ForgotPasswordResponse();
            var user = await FindAccount(db, body);
            if (user is null)
                return Results.Ok(response);

            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            db.PasswordResets.Add(new PasswordResetRow
            {
                Id = Guid.NewGuid(),
                Login = user.Login,
                TokenHash = HashToken(token),
                ExpiresUtc = DateTime.UtcNow.AddHours(1)
            });
            await db.SaveChangesAsync();
            if (config.GetValue("GpsTrainer:ExposeResetTokens", false))
                response.ResetToken = token;
            return Results.Ok(response);
        });

        api.MapPost("/auth/reset", async (ResetPasswordRequest body, GpsTrainerDb db) =>
        {
            if (string.IsNullOrWhiteSpace(body.NewPassword) || string.IsNullOrWhiteSpace(body.Token))
                return Results.BadRequest();
            var hash = HashToken(body.Token);
            var row = await db.PasswordResets
                .Where(r => r.Login == body.Login && r.TokenHash == hash && r.ExpiresUtc > DateTime.UtcNow)
                .OrderByDescending(r => r.ExpiresUtc)
                .FirstOrDefaultAsync();
            if (row is null)
                return Results.Unauthorized();
            var user = await db.Users.FindAsync(body.Login);
            if (user is null)
                return Results.Unauthorized();
            user.PasswordHash = PasswordHasher.Hash(body.NewPassword.TrimEnd());
            db.PasswordResets.RemoveRange(db.PasswordResets.Where(r => r.Login == body.Login));
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapGet("/courses/{id:guid}/intervals", async (Guid id, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            var course = await db.Courses.FindAsync(id);
            if (course is not null && !CanReadCourse(http, config, course))
                return Results.NotFound();
            var stored = await db.CourseIntervals.Where(i => i.CourseId == id).OrderBy(i => i.Ordinal).ToListAsync();
            if (stored.Count > 0)
                return Results.Ok(stored.Select(row => ToDto(ToDefinition(row))));
            var waypoints = await LoadWaypoints(db, id);
            return Results.Ok(IntervalCourse.FromMarkers(id, waypoints).Select(ToDto));
        });

        api.MapGet("/courses/{id:guid}/interval-results", async (Guid id, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            var course = await db.Courses.FindAsync(id);
            if (course is not null && !CanReadCourse(http, config, course))
                return Results.NotFound();
            var waypoints = await LoadWaypoints(db, id);
            var stored = await db.CourseIntervals.Where(i => i.CourseId == id).OrderBy(i => i.Ordinal).ToListAsync();
            var pairs = stored.Count > 0
                ? stored.Select(row => (Start: IntervalCourse.Marker(ToDefinition(row), true), Finish: IntervalCourse.Marker(ToDefinition(row), false))).ToList()
                : CourseIntervals.Pair(waypoints).ToList();
            TryLogin(http, config, out var viewer);
            var races = await db.Races.Where(r => r.CourseId == id && (r.Publish || r.Login == viewer)).ToListAsync();
            var results = new List<IntervalResultDto>();
            foreach (var race in races.OrderBy(r => r.StartTime))
            {
                var positions = await db.Positions.Where(p => p.RaceId == race.RaceId).OrderBy(p => p.TimestampTicks).ToListAsync();
                var track = positions.Select(p => new TrackPoint
                {
                    RaceId = p.RaceId,
                    TimestampTicks = p.TimestampTicks,
                    Latitude = p.Latitude,
                    Longitude = p.Longitude
                }).ToList();
                var splits = new List<IntervalSplit>();
                foreach (var pair in pairs)
                {
                    var split = CourseIntervals.Measure(pair.Start, pair.Finish, track);
                    if (split is null)
                    {
                        splits.Clear();
                        break;
                    }
                    splits.Add(split);
                }
                if (splits.Count != pairs.Count || splits.Count == 0)
                    continue;
                var total = TimeSpan.Zero;
                foreach (var split in splits)
                    total += split.Time;
                results.Add(new IntervalResultDto
                {
                    RaceId = race.RaceId,
                    Login = race.Login,
                    Date = splits[0].StartTime,
                    Splits = splits.Select(s => s.TimeString).ToList(),
                    Total = IntervalClock.Format(total)
                });
            }
            return Results.Ok(results);
        });

        api.MapPost("/courses/{id:guid}/validate", async (Guid id, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            var course = await db.Courses.FindAsync(id);
            if (course is null)
                return Results.NotFound();
            if (!CanReadUser(http, config, course.Login))
                return Results.Unauthorized();
            var waypoints = await LoadWaypoints(db, id);
            var message = CourseRules.Validate(waypoints);
            course.WaypointsVerified = message is null;
            await db.SaveChangesAsync();
            return Results.Ok(new ValidationResultDto { Ok = message is null, Message = message ?? "" });
        });

        api.MapGet("/courses/{id:guid}/waypoints/essential", async (Guid id, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            var course = await db.Courses.FindAsync(id);
            if (course is not null && !CanReadCourse(http, config, course))
                return Results.NotFound();
            var waypoints = await LoadWaypoints(db, id);
            return Results.Ok(CourseRules.Essential(waypoints));
        });

        api.MapPost("/courses/{id:guid}/intervals", async (Guid id, PlaceIntervalRequest body, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            var course = await db.Courses.FindAsync(id);
            if (course is null)
                return Results.NotFound();
            if (!CanReadUser(http, config, course.Login))
                return Results.Unauthorized();
            var waypoints = await LoadWaypoints(db, id);
            (IReadOnlyList<Waypoint> merged, CourseIntervalDefinition interval) placed;
            try
            {
                placed = IntervalCourse.Place(waypoints, id, body.Name, body.StartElapsedKm, body.FinishElapsedKm);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }
            WriteWaypoints(db, id, placed.merged);
            var previous = (await db.CourseIntervals.Where(i => i.CourseId == id).ToListAsync()).Select(ToDefinition).ToList();
            var all = IntervalCourse.FromMarkers(id, placed.merged).ToList();
            KeepIntervalIds(previous, all);
            var created = all.Single(item => item.StartTimestampTicks == placed.interval.StartTimestampTicks);
            created.IntervalId = placed.interval.IntervalId;
            created.Name = body.Name ?? "";
            ReplaceIntervalRows(db, id, all);
            course.WaypointTimestamp = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(created));
        });

        api.MapPost("/courses/{id:guid}/intervals/{intervalId:guid}/move", async (Guid id, Guid intervalId, MoveIntervalDistanceRequest body, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            var course = await db.Courses.FindAsync(id);
            if (course is null)
                return Results.NotFound();
            if (!CanReadUser(http, config, course.Login))
                return Results.Unauthorized();
            var row = await db.CourseIntervals.FindAsync(intervalId);
            if (row is null || row.CourseId != id)
                return Results.NotFound();
            var waypoints = await LoadWaypoints(db, id);
            var definition = ToDefinition(row);
            IReadOnlyList<Waypoint> moved;
            try
            {
                moved = IntervalCourse.Move(waypoints, definition, body.StartElapsedKm, body.FinishElapsedKm);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }
            WriteWaypoints(db, id, moved);
            var previous = (await db.CourseIntervals.Where(i => i.CourseId == id).ToListAsync()).Select(ToDefinition).ToList();
            var all = IntervalCourse.FromMarkers(id, moved).ToList();
            KeepIntervalIds(previous, all);
            var updated = all.Single(item => item.StartTimestampTicks == definition.StartTimestampTicks);
            updated.IntervalId = definition.IntervalId;
            updated.Name = definition.Name;
            ReplaceIntervalRows(db, id, all);
            course.WaypointTimestamp = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(updated));
        });

        api.MapDelete("/courses/{id:guid}/intervals/{intervalId:guid}", async (Guid id, Guid intervalId, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            var course = await db.Courses.FindAsync(id);
            if (course is null)
                return Results.NotFound();
            if (!CanReadUser(http, config, course.Login))
                return Results.Unauthorized();
            var row = await db.CourseIntervals.FindAsync(intervalId);
            if (row is null || row.CourseId != id)
                return Results.NotFound();
            var waypoints = await LoadWaypoints(db, id);
            var kept = waypoints.Where(w => w.TimestampTicks != row.StartTimestampTicks && w.TimestampTicks != row.FinishTimestampTicks).ToList();
            WriteWaypoints(db, id, kept);
            var previous = (await db.CourseIntervals.Where(i => i.CourseId == id && i.IntervalId != intervalId).ToListAsync()).Select(ToDefinition).ToList();
            var rest = IntervalCourse.FromMarkers(id, kept).ToList();
            KeepIntervalIds(previous, rest);
            ReplaceIntervalRows(db, id, rest);
            course.WaypointTimestamp = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapGet("/courses/{id:guid}/export/gpx", async (Guid id, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            var course = await db.Courses.FindAsync(id);
            if (course is null)
                return Results.NotFound();
            if (!CanReadUser(http, config, course.Login))
                return Results.Unauthorized();
            var waypoints = await LoadWaypoints(db, id);
            var intervals = (await db.CourseIntervals.Where(i => i.CourseId == id).OrderBy(i => i.Ordinal).ToListAsync())
                .Select(ToDefinition).ToList();
            if (intervals.Count == 0)
                intervals = IntervalCourse.FromMarkers(id, waypoints).ToList();
            var xml = GpxCourse.Export(course.Name, waypoints, intervals, config["GpsTrainer:PublicUrl"]);
            return Results.Text(xml, "application/gpx+xml");
        });

        api.MapPost("/courses/import/gpx", async (HttpRequest request, HttpContext http, IConfiguration config) =>
        {
            if (!TryLogin(http, config, out var login))
                return Results.Unauthorized();
            var xml = await ReadGpxAsync(request.Body);
            if (xml is null)
                return Results.BadRequest("GPX file is too large.");
            var tolerance = GpxCourse.DefaultToleranceMeters;
            if (double.TryParse(request.Query["toleranceMeters"], NumberStyles.Float, CultureInfo.InvariantCulture, out var requested))
                tolerance = requested;
            GpxCourseDocument imported;
            try
            {
                imported = GpxCourse.Import(xml, new GpxImportOptions
                {
                    ToleranceMeters = tolerance,
                    FileName = request.Query["fileName"]
                });
            }
            catch (Exception ex) when (ex is InvalidOperationException or XmlException or FormatException)
            {
                return Results.BadRequest(ex.Message);
            }
            return Results.Ok(new GpxImportResult
            {
                PreservedGpsTrainerExtensions = imported.HasGpsTrainerExtensions,
                Waypoints = imported.Waypoints,
                Intervals = imported.Intervals,
                Course = new Course
                {
                    Name = string.IsNullOrWhiteSpace(imported.Name) ? "Imported course" : imported.Name,
                    Comments = imported.Provenance ?? "",
                    Publish = false,
                    Login = login,
                    ElapsedDistanceKm = imported.ElapsedDistanceKm,
                    AltitudeGainKm = imported.AltitudeGainKm,
                    AltitudeLossKm = imported.AltitudeLossKm
                }
            });
        });

        api.MapPost("/courses/{id:guid}/import/gpx", async (Guid id, HttpRequest request, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            var course = await db.Courses.FindAsync(id);
            if (course is null)
                return Results.NotFound();
            if (!CanReadUser(http, config, course.Login))
                return Results.Unauthorized();
            using var reader = new StreamReader(request.Body);
            var xml = await reader.ReadToEndAsync();
            if (xml.Length > GpxCourse.MaxCharacters)
                return Results.BadRequest("GPX file is too large.");
            GpxCourseDocument imported;
            try
            {
                imported = GpxCourse.Import(xml);
            }
            catch (Exception ex) when (ex is InvalidOperationException or XmlException)
            {
                return Results.BadRequest(ex.Message);
            }
            if (!imported.HasGpsTrainerExtensions)
                return Results.BadRequest("This GPX file has no gpsTrainer interval extensions.");
            foreach (var wp in imported.Waypoints)
                wp.CourseId = id;
            foreach (var interval in imported.Intervals)
            {
                interval.CourseId = id;
                if (interval.IntervalId == Guid.Empty)
                    interval.IntervalId = Guid.NewGuid();
            }
            WriteWaypoints(db, id, imported.Waypoints);
            ReplaceIntervalRows(db, id, imported.Intervals);
            if (!string.IsNullOrWhiteSpace(imported.Name))
                course.Name = imported.Name;
            course.WaypointTimestamp = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(imported.Intervals.Select(ToDto));
        });

        api.MapPost("/courses/{id:guid}/intervals/move", async (Guid id, MoveIntervalRequest body, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            var course = await db.Courses.FindAsync(id);
            if (course is null)
                return Results.NotFound();
            if (!CanReadUser(http, config, course.Login))
                return Results.Unauthorized();
            var start = await db.Waypoints.FindAsync(id, body.PreviousStartTicks);
            var finish = await db.Waypoints.FindAsync(id, body.PreviousFinishTicks);
            if (start is null || finish is null)
                return Results.NotFound();
            db.Waypoints.Remove(start);
            db.Waypoints.Remove(finish);
            db.Waypoints.Add(ToRow(id, body.NewStart, WaypointType.StartInterval));
            db.Waypoints.Add(ToRow(id, body.NewFinish, WaypointType.FinishInterval));
            course.WaypointTimestamp = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapGet("/courses/published", async (double? latitude, double? longitude, double? radiusKm, GpsTrainerDb db) =>
        {
            var courses = await db.Courses.Where(c => c.Publish && c.Approved).ToListAsync();
            if (latitude is null || longitude is null)
                return Results.Ok(courses);
            var radius = (radiusKm ?? 50) * 1000;
            var nearby = new List<CourseRow>();
            foreach (var course in courses)
            {
                var first = await db.Waypoints.Where(w => w.CourseId == course.CourseId).OrderBy(w => w.TimestampTicks).FirstOrDefaultAsync();
                if (first is null)
                    continue;
                if (GeoMath.DistanceMeters(latitude.Value, longitude.Value, first.Latitude, first.Longitude) <= radius)
                    nearby.Add(course);
            }
            return Results.Ok(nearby);
        });

        api.MapGet("/promotions", async (GpsTrainerDb db) =>
            Results.Ok(await db.RacePromotions.OrderBy(p => p.RaceDateStart).ToListAsync()));

        api.MapPost("/promotions", async (RacePromotionDto body, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            if (!CanReadUser(http, config, body.Login))
                return Results.Unauthorized();
            var course = await db.Courses.FindAsync(body.CourseId);
            if (course is null)
                return Results.NotFound();
            if (!IsAdmin(http, config) && !string.Equals(course.Login, body.Login, StringComparison.Ordinal))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (body.RacePromotionId == Guid.Empty)
                body.RacePromotionId = Guid.NewGuid();
            db.RacePromotions.Add(new RacePromotionRow
            {
                RacePromotionId = body.RacePromotionId,
                CourseId = body.CourseId,
                RaceDateStart = body.RaceDateStart,
                RaceDateFinish = body.RaceDateFinish,
                EntryFee = body.EntryFee,
                Login = body.Login
            });
            await db.SaveChangesAsync();
            return Results.Ok(body);
        });

        api.MapPost("/promotions/{id:guid}/register", async (Guid id, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            if (!TryLogin(http, config, out var login))
                return Results.Unauthorized();
            var promotion = await db.RacePromotions.FindAsync(id);
            if (promotion is null)
                return Results.NotFound();
            if (await db.RaceRegistrations.AnyAsync(r => r.RacePromotionId == id && r.Login == login))
                return Results.Ok(new { login, alreadyRegistered = true });
            db.RaceRegistrations.Add(new RaceRegistrationRow
            {
                RaceRegistrationId = Guid.NewGuid(),
                RacePromotionId = id,
                Login = login
            });
            await db.SaveChangesAsync();
            return Results.Ok(new { login, alreadyRegistered = false });
        });
    }

    private static async Task<UserRow?> FindAccount(GpsTrainerDb db, ForgotPasswordRequest body)
    {
        if (!string.IsNullOrWhiteSpace(body.Login))
            return await db.Users.FindAsync(body.Login.Trim());
        if (!string.IsNullOrWhiteSpace(body.Email))
            return await db.Users.FirstOrDefaultAsync(u => u.Email == body.Email.Trim());
        return null;
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static async Task<List<Waypoint>> LoadWaypoints(GpsTrainerDb db, Guid courseId)
    {
        var rows = await db.Waypoints.Where(w => w.CourseId == courseId).OrderBy(w => w.TimestampTicks).ToListAsync();
        return rows.Select(w => new Waypoint
        {
            CourseId = w.CourseId,
            TimestampTicks = w.TimestampTicks,
            Latitude = w.Latitude,
            Longitude = w.Longitude,
            HeadingDegrees = w.Heading,
            WaypointType = (WaypointType)w.WaypointType,
            AltitudeKm = w.AltitudeKm,
            ElapsedDistanceKm = w.ElapsedDistanceKm,
            TextDescription = w.TextDescription
        }).ToList();
    }

    private static void WriteWaypoints(GpsTrainerDb db, Guid courseId, IReadOnlyList<Waypoint> waypoints)
    {
        db.Waypoints.RemoveRange(db.Waypoints.Where(w => w.CourseId == courseId));
        foreach (var wp in waypoints)
        {
            db.Waypoints.Add(new WaypointRow
            {
                CourseId = courseId,
                TimestampTicks = wp.TimestampTicks,
                Latitude = wp.Latitude,
                Longitude = wp.Longitude,
                Heading = wp.HeadingDegrees,
                WaypointType = (int)wp.WaypointType,
                AltitudeKm = wp.AltitudeKm,
                ElapsedDistanceKm = wp.ElapsedDistanceKm,
                TextDescription = wp.TextDescription
            });
        }
    }

    private static void KeepIntervalIds(IReadOnlyList<CourseIntervalDefinition> previous, IEnumerable<CourseIntervalDefinition> current)
    {
        foreach (var item in current)
        {
            var match = previous.FirstOrDefault(existing => existing.StartTimestampTicks == item.StartTimestampTicks);
            if (match is not null)
                item.IntervalId = match.IntervalId;
        }
    }

    private static void ReplaceIntervalRows(GpsTrainerDb db, Guid courseId, IReadOnlyList<CourseIntervalDefinition> intervals)
    {
        var existing = db.CourseIntervals.Where(i => i.CourseId == courseId).ToList();
        foreach (var interval in intervals)
        {
            interval.CourseId = courseId;
            if (interval.IntervalId == Guid.Empty)
                interval.IntervalId = Guid.NewGuid();
        }
        foreach (var row in existing.Where(row => intervals.All(interval => interval.IntervalId != row.IntervalId)))
            db.CourseIntervals.Remove(row);
        foreach (var interval in intervals)
        {
            var row = existing.FirstOrDefault(item => item.IntervalId == interval.IntervalId);
            if (row is null)
            {
                db.CourseIntervals.Add(ToIntervalRow(interval));
                continue;
            }
            row.Ordinal = interval.Ordinal;
            row.Name = interval.Name;
            row.StartElapsedKm = interval.StartElapsedKm;
            row.FinishElapsedKm = interval.FinishElapsedKm;
            row.StartTimestampTicks = interval.StartTimestampTicks;
            row.FinishTimestampTicks = interval.FinishTimestampTicks;
            row.StartLatitude = interval.StartLatitude;
            row.StartLongitude = interval.StartLongitude;
            row.FinishLatitude = interval.FinishLatitude;
            row.FinishLongitude = interval.FinishLongitude;
            row.StartAltitudeKm = interval.StartAltitudeKm;
            row.FinishAltitudeKm = interval.FinishAltitudeKm;
            row.StartHeadingDegrees = interval.StartHeadingDegrees;
            row.FinishHeadingDegrees = interval.FinishHeadingDegrees;
        }
    }

    private static CourseIntervalDto ToDto(CourseIntervalDefinition interval) => new()
    {
        IntervalId = interval.IntervalId,
        Index = interval.Ordinal,
        Name = interval.Name,
        LengthKm = interval.LengthKm,
        StartElapsedKm = interval.StartElapsedKm,
        FinishElapsedKm = interval.FinishElapsedKm,
        StartTimestampTicks = interval.StartTimestampTicks,
        FinishTimestampTicks = interval.FinishTimestampTicks,
        StartLatitude = interval.StartLatitude,
        StartLongitude = interval.StartLongitude,
        FinishLatitude = interval.FinishLatitude,
        FinishLongitude = interval.FinishLongitude
    };

    private static CourseIntervalDefinition ToDefinition(CourseIntervalRow row) => new()
    {
        IntervalId = row.IntervalId,
        CourseId = row.CourseId,
        Ordinal = row.Ordinal,
        Name = row.Name,
        StartElapsedKm = row.StartElapsedKm,
        FinishElapsedKm = row.FinishElapsedKm,
        StartTimestampTicks = row.StartTimestampTicks,
        FinishTimestampTicks = row.FinishTimestampTicks,
        StartLatitude = row.StartLatitude,
        StartLongitude = row.StartLongitude,
        FinishLatitude = row.FinishLatitude,
        FinishLongitude = row.FinishLongitude,
        StartAltitudeKm = row.StartAltitudeKm,
        FinishAltitudeKm = row.FinishAltitudeKm,
        StartHeadingDegrees = row.StartHeadingDegrees,
        FinishHeadingDegrees = row.FinishHeadingDegrees
    };

    private static CourseIntervalRow ToIntervalRow(CourseIntervalDefinition interval) => new()
    {
        IntervalId = interval.IntervalId,
        CourseId = interval.CourseId,
        Ordinal = interval.Ordinal,
        Name = interval.Name,
        StartElapsedKm = interval.StartElapsedKm,
        FinishElapsedKm = interval.FinishElapsedKm,
        StartTimestampTicks = interval.StartTimestampTicks,
        FinishTimestampTicks = interval.FinishTimestampTicks,
        StartLatitude = interval.StartLatitude,
        StartLongitude = interval.StartLongitude,
        FinishLatitude = interval.FinishLatitude,
        FinishLongitude = interval.FinishLongitude,
        StartAltitudeKm = interval.StartAltitudeKm,
        FinishAltitudeKm = interval.FinishAltitudeKm,
        StartHeadingDegrees = interval.StartHeadingDegrees,
        FinishHeadingDegrees = interval.FinishHeadingDegrees
    };

    private static WaypointRow ToRow(Guid courseId, Waypoint wp, WaypointType type) => new()
    {
        CourseId = courseId,
        TimestampTicks = wp.TimestampTicks == 0 ? DateTime.UtcNow.Ticks : wp.TimestampTicks,
        Latitude = wp.Latitude,
        Longitude = wp.Longitude,
        Heading = wp.HeadingDegrees,
        WaypointType = (int)type,
        AltitudeKm = wp.AltitudeKm,
        ElapsedDistanceKm = wp.ElapsedDistanceKm,
        TextDescription = wp.TextDescription
    };

    private static async Task<string?> ReadGpxAsync(Stream body)
    {
        using var reader = new StreamReader(body);
        var buffer = new char[GpxCourse.MaxCharacters + 1];
        var read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
        if (read > GpxCourse.MaxCharacters)
            return null;
        return new string(buffer, 0, read);
    }
}
