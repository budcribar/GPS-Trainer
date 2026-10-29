using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using GpsTrainer.Server.Data;
using GpsTrainer.Server.Routing;
using GpsTrainer.Server.Security;
using Microsoft.EntityFrameworkCore;

namespace GpsTrainer.Server.Api;

public static partial class GpsEndpoints
{
    public static void MapGpsApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapPost("/auth/login", async (LoginRequest body, GpsTrainerDb db, IConfiguration config) =>
        {
            var login = body.Login.TrimEnd();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Login.ToLower() == login.ToLower());
            if (user is null || !PasswordHasher.Verify(body.Password.TrimEnd(), user.PasswordHash))
                return Results.Unauthorized();
            var token = AuthTokens.Issue(user.Login, SigningKey(config), DateTimeOffset.UtcNow.AddDays(14));
            return Results.Ok(new LoginResponse { Token = token, User = ToProfile(user) });
        });

        api.MapPost("/auth/logout", async (HttpContext http, IConfiguration config, GpsTrainerDb db, RevokedAuthTokens revoked) =>
        {
            if (!AuthTokens.TryRead(ReadBearer(http), SigningKey(config), out _, out var expires, out var tokenId))
                return Results.Unauthorized();
            revoked.Remember(tokenId, expires);
            var row = await db.RevokedAuthTokens.FindAsync(tokenId);
            if (row is null)
                db.RevokedAuthTokens.Add(new RevokedAuthTokenRow { TokenId = tokenId, ExpiresUnix = expires });
            else
                row.ExpiresUnix = expires;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapPost("/users", async (UserWriteDto body, GpsTrainerDb db) =>
        {
            if (string.IsNullOrWhiteSpace(body.Login) || string.IsNullOrWhiteSpace(body.Password))
                return Results.BadRequest(new InsertUserResponse { Result = "MissingLogin" });
            var login = body.Login.TrimEnd();
            if (login.Length > 32 || login.Contains('|'))
                return Results.BadRequest(new InsertUserResponse { Result = "MissingLogin" });
            if (await db.Users.AnyAsync(u => u.Login.ToLower() == login.ToLower()))
                return Results.Ok(new InsertUserResponse { Result = SoapContract.DuplicateUserName });
            var email = body.Email?.Trim() ?? "";
            if (email.Length > 0 && await db.Users.AnyAsync(u => u.Email != null && u.Email.ToLower() == email.ToLower()))
                return Results.Ok(new InsertUserResponse { Result = SoapContract.DuplicateEmail });

            db.Users.Add(new UserRow
            {
                Login = body.Login.TrimEnd(),
                PasswordHash = PasswordHasher.Hash(body.Password.TrimEnd()),
                FirstName = body.FirstName,
                LastName = body.LastName,
                Street = body.Street,
                City = body.City,
                State = body.State,
                Country = string.IsNullOrWhiteSpace(body.Country) ? "US" : body.Country,
                Email = email,
                PhoneNumber = body.PhoneNumber,
                IsMale = body.IsMale,
                BirthDate = body.BirthDate,
                RacingCategory = body.RacingCategoryKey,
                WantsEmails = body.WantsEmails,
                IsActive = true,
                ExpirationDate = body.ExpirationDate ?? DateTime.UtcNow.AddYears(10),
                UserTimestamp = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            return Results.Ok(new InsertUserResponse { Result = SoapContract.InsertUserOk });
        });

        api.MapGet("/users/{login}", async (string login, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            if (!CanReadUser(http, config, login))
                return Results.Unauthorized();
            var user = await db.Users.FindAsync(login);
            return user is null ? Results.NotFound() : Results.Ok(ToProfile(user));
        });

        api.MapGet("/users/{login}/status", async (string login, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            // The same response for a missing account and for someone else's account,
            // so this route cannot be used to discover which logins exist.
            if (!CanReadUser(http, config, login))
                return Results.Unauthorized();
            var user = await db.Users.FindAsync(login);
            if (user is null)
                return Results.NotFound();
            return Results.Ok(new UserStatus
            {
                IsActive = user.IsActive,
                ExpirationDate = user.ExpirationDate,
                SystemTimestamp = DateTime.UtcNow,
                UserTimestamp = user.UserTimestamp
            });
        });

        api.MapDelete("/users/{login}", async (string login, [Microsoft.AspNetCore.Mvc.FromBody] AccountDeletionRequest body, GpsTrainerDb db, HttpContext http, IConfiguration config, RevokedAuthTokens revoked) =>
        {
            if (!TryLogin(http, config, out var tokenLogin))
                return Results.Unauthorized();
            var user = await db.Users.FindAsync(login);
            if (user is null || !string.Equals(tokenLogin, user.Login, StringComparison.Ordinal))
                return user is null && string.Equals(tokenLogin, login, StringComparison.Ordinal)
                    ? Results.NotFound()
                    : Results.StatusCode(StatusCodes.Status403Forbidden);
            var confirmation = body.Confirmation?.Trim() ?? "";
            var passwordAccepted = confirmation.Length > 0 && PasswordHasher.Verify(confirmation, user.PasswordHash);
            if (!string.Equals(confirmation, "DELETE", StringComparison.Ordinal) && !passwordAccepted)
                return Results.BadRequest();
            await DeleteAccountAsync(db, user);
            if (AuthTokens.TryRead(ReadBearer(http), SigningKey(config), out _, out var expires, out var tokenId))
            {
                revoked.Remember(tokenId, expires);
                var revokedRow = await db.RevokedAuthTokens.FindAsync(tokenId);
                if (revokedRow is null)
                    db.RevokedAuthTokens.Add(new RevokedAuthTokenRow { TokenId = tokenId, ExpiresUnix = expires });
                else
                    revokedRow.ExpiresUnix = expires;
            }
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapPost("/users/{login}/push", async (string login, PushRegistrationDto body, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            if (!CanReadUser(http, config, login))
                return Results.Unauthorized();
            var user = await db.Users.FindAsync(login);
            if (user is null)
                return Results.NotFound();
            user.PushToken = body.Token;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapGet("/activity-codes", async (GpsTrainerDb db) =>
            Results.Ok(await db.ActivityCodes.OrderBy(c => c.ActKey).ToListAsync()));

        api.MapPost("/activity-codes", async (ActivityCodeRow body, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            if (!IsAdmin(http, config))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            db.ActivityCodes.Add(body);
            await db.SaveChangesAsync();
            return Results.Ok(body);
        });

        api.MapGet("/racing-categories", async (GpsTrainerDb db) =>
            Results.Ok(await db.RacingCategories.OrderBy(c => c.CatKey).ToListAsync()));

        api.MapPost("/racing-categories", async (RacingCategoryRow body, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            if (!IsAdmin(http, config))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            db.RacingCategories.Add(body);
            await db.SaveChangesAsync();
            return Results.Ok(body);
        });

        api.MapGet("/courses", async (string login, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            if (!CanReadUser(http, config, login))
                return Results.Unauthorized();
            var ids = await db.CourseDownloads.Where(d => d.Login == login).Select(d => d.CourseId).ToListAsync();
            var courses = await db.Courses.Where(c => ids.Contains(c.CourseId)).ToListAsync();
            var withIntervals = (await db.CourseIntervals
                .Where(i => ids.Contains(i.CourseId))
                .Select(i => i.CourseId)
                .Distinct()
                .ToListAsync())
                .ToHashSet();
            var remainingIds = ids.Where(id => !withIntervals.Contains(id)).ToList();
            if (remainingIds.Count > 0)
            {
                var markerRows = await db.Waypoints
                    .Where(w => remainingIds.Contains(w.CourseId)
                        && (w.WaypointType == (int)WaypointType.StartInterval || w.WaypointType == (int)WaypointType.FinishInterval))
                    .OrderBy(w => w.TimestampTicks)
                    .ToListAsync();
                foreach (var group in markerRows.GroupBy(w => w.CourseId))
                {
                    var markers = group.Select(w => new Waypoint
                    {
                        CourseId = w.CourseId,
                        TimestampTicks = w.TimestampTicks,
                        WaypointType = (WaypointType)w.WaypointType
                    }).ToList();
                    if (CourseIntervals.Pair(markers).Count > 0)
                        withIntervals.Add(group.Key);
                }
            }
            foreach (var course in courses)
                course.HasIntervals = withIntervals.Contains(course.CourseId);
            return Results.Ok(courses);
        });

        api.MapGet("/courses/{id:guid}", async (Guid id, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            var course = await db.Courses.FindAsync(id);
            if (course is null)
                return Results.NotFound();
            if (!CanReadCourse(http, config, course))
                return Results.NotFound();
            return Results.Ok(course);
        });

        api.MapPost("/courses", async (Course body, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            if (!CanReadUser(http, config, body.Login))
                return Results.Unauthorized();
            if (body.Name is { Length: > 128 })
                return Results.BadRequest();
            if (body.CourseId == Guid.Empty)
                body.CourseId = Guid.NewGuid();
            body.Timestamp = DateTime.UtcNow;
            db.Courses.Add(ToCourseRow(body));
            db.CourseDownloads.Add(new CourseDownload { Login = body.Login, CourseId = body.CourseId });
            await Touch(db, body.Login);
            await db.SaveChangesAsync();
            return Results.Ok(body);
        });

        api.MapPut("/courses/{id:guid}", async (Guid id, Course body, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            var row = await db.Courses.FindAsync(id);
            if (row is null)
                return Results.NotFound();
            if (!CanReadUser(http, config, row.Login))
                return Results.Unauthorized();
            if (body.Name is { Length: > 128 })
                return Results.BadRequest();
            row.Name = body.Name;
            row.Comments = body.Comments;
            row.ActivityCode = body.ActivityCode;
            row.Laps = body.Laps;
            row.ElapsedDistanceKm = body.ElapsedDistanceKm;
            row.AltitudeGainKm = body.AltitudeGainKm;
            row.AltitudeLossKm = body.AltitudeLossKm;
            row.Publish = body.Publish;
            row.Approved = body.Approved;
            row.City = body.City;
            row.State = body.State;
            row.Difficulty = body.Difficulty;
            row.Url = body.Url;
            row.Description = body.Description;
            row.RaceDate = body.RaceDate;
            row.WaypointsVerified = body.WaypointsVerified;
            row.Timestamp = DateTime.UtcNow;
            row.WaypointTimestamp = body.WaypointTimestamp;
            await Touch(db, row.Login);
            await db.SaveChangesAsync();
            return Results.Ok(row);
        });

        api.MapDelete("/courses/{id:guid}", async (Guid id, string login, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            if (!CanReadUser(http, config, login))
                return Results.Unauthorized();
            var row = await db.Courses.FindAsync(id);
            if (row is null)
                return Results.NotFound();
            if (!IsAdmin(http, config) && !string.Equals(row.Login, login, StringComparison.Ordinal))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            db.Courses.Remove(row);
            var downloads = db.CourseDownloads.Where(d => d.CourseId == id);
            db.CourseDownloads.RemoveRange(downloads);
            var points = db.Waypoints.Where(w => w.CourseId == id);
            db.Waypoints.RemoveRange(points);
            db.CourseIntervals.RemoveRange(db.CourseIntervals.Where(i => i.CourseId == id));
            await Touch(db, login);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapPost("/courses/{id:guid}/enable-download", async (Guid id, string login, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            if (!CanReadUser(http, config, login))
                return Results.Unauthorized();
            var course = await db.Courses.FindAsync(id);
            if (course is null)
                return Results.NotFound();
            if (!IsAdmin(http, config)
                && !string.Equals(course.Login, login, StringComparison.Ordinal)
                && !CourseIsPublic(course))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (!await db.CourseDownloads.AnyAsync(d => d.Login == login && d.CourseId == id))
                db.CourseDownloads.Add(new CourseDownload { Login = login, CourseId = id });
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapPost("/courses/{id:guid}/inhibit-download", async (Guid id, string login, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            if (!CanReadUser(http, config, login))
                return Results.Unauthorized();
            var row = await db.CourseDownloads.FindAsync(login, id);
            if (row is not null)
                db.CourseDownloads.Remove(row);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapGet("/courses/{id:guid}/waypoints", async (Guid id, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            var course = await db.Courses.FindAsync(id);
            if (course is not null && !CanReadCourse(http, config, course))
                return Results.NotFound();
            var rows = await db.Waypoints.Where(w => w.CourseId == id).OrderBy(w => w.TimestampTicks).ToListAsync();
            return Results.Ok(rows.Select(w => new Waypoint
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
            }));
        });

        api.MapPut("/courses/{id:guid}/waypoints", async (
            Guid id,
            List<Waypoint> body,
            GpsTrainerDb db,
            HttpContext http,
            IConfiguration config,
            IRoutingProviderFactory providers,
            RoutingSettingsStore store,
            CancellationToken cancellationToken) =>
        {
            var gate = WaypointSaveLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(http.RequestAborted);
            try
            {
                var course = await db.Courses.FindAsync(id);
                if (course is null)
                    return Results.NotFound();
                if (!CanReadUser(http, config, course.Login))
                    return Results.Unauthorized();
                var stored = await db.CourseIntervals.Where(i => i.CourseId == id).ToListAsync();
                var incomingMarkers = body.Any(w => w.WaypointType is WaypointType.StartInterval or WaypointType.FinishInterval);
                IReadOnlyList<Waypoint> finalWaypoints = body;
                IReadOnlyList<CourseIntervalDefinition> intervals;
                if (incomingMarkers || stored.Count == 0)
                {
                    var derived = IntervalCourse.FromMarkers(id, body).ToList();
                    KeepIntervalIds(stored.Select(ToDefinition).ToList(), derived);
                    intervals = derived;
                }
                else
                {
                    var defs = stored.Select(ToDefinition).ToList();
                    foreach (var def in defs)
                        IntervalCourse.Reproject(body, def);
                    intervals = defs;
                    finalWaypoints = IntervalCourse.WithMarkers(body, defs);
                }
                var editable = finalWaypoints as List<Waypoint> ?? finalWaypoints.ToList();
                await TryFillElevationAsync(editable, course, db, config, providers, store, cancellationToken);
                if (!TryStampWaypointRows(id, editable, out var rows))
                    return Results.BadRequest();
                await using var transaction = await db.Database.BeginTransactionAsync();
                db.Waypoints.RemoveRange(db.Waypoints.Where(w => w.CourseId == id));
                db.Waypoints.AddRange(rows);
                ReplaceIntervalRows(db, id, intervals);
                course.WaypointTimestamp = DateTime.UtcNow;
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
                return Results.NoContent();
            }
            finally
            {
                gate.Release();
            }
        });

        api.MapGet("/courses/nearest", async (string login, double latitude, double longitude, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            if (!CanReadUser(http, config, login))
                return Results.Unauthorized();
            var courses = await db.Courses.Where(c => c.Publish && c.Approved).ToListAsync();
            var courseIds = courses.Select(c => c.CourseId).ToList();
            var firstWaypoints = (await db.Waypoints
                .Where(w => courseIds.Contains(w.CourseId))
                .OrderBy(w => w.TimestampTicks)
                .ToListAsync())
                .GroupBy(w => w.CourseId)
                .ToDictionary(g => g.Key, g => g.First());
            CourseRow? best = null;
            var bestMeters = double.MaxValue;
            foreach (var course in courses)
            {
                if (!firstWaypoints.TryGetValue(course.CourseId, out var first))
                    continue;
                var meters = GeoMath.DistanceMeters(latitude, longitude, first.Latitude, first.Longitude);
                if (meters < bestMeters)
                {
                    bestMeters = meters;
                    best = course;
                }
            }
            if (best is null)
                return Results.NoContent();
            if (!await db.CourseDownloads.AnyAsync(d => d.Login == login && d.CourseId == best.CourseId))
                db.CourseDownloads.Add(new CourseDownload { Login = login, CourseId = best.CourseId });
            await db.SaveChangesAsync();
            return Results.Ok(best);
        });

        api.MapGet("/races", async (string login, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            if (!CanReadUser(http, config, login))
                return Results.Unauthorized();
            var ids = await db.RaceDownloads.Where(d => d.Login == login).Select(d => d.RaceId).ToListAsync();
            return Results.Ok(await db.Races.Where(r => ids.Contains(r.RaceId)).ToListAsync());
        });

        api.MapGet("/races/{id:guid}", async (Guid id, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            var race = await db.Races.FindAsync(id);
            if (race is null || (!race.Publish && !CanReadUser(http, config, race.Login)))
                return Results.NotFound();
            return Results.Ok(race);
        });

        api.MapPost("/races", async (Race body, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            if (!CanReadUser(http, config, body.Login))
                return Results.Unauthorized();
            if (body.RaceId == Guid.Empty)
                body.RaceId = Guid.NewGuid();
            body.Timestamp = DateTime.UtcNow;
            db.Races.Add(ToRaceRow(body));
            db.RaceDownloads.Add(new RaceDownload { Login = body.Login, RaceId = body.RaceId });
            await Touch(db, body.Login);
            await db.SaveChangesAsync();
            return Results.Ok(body);
        });

        api.MapPut("/races/{id:guid}", async (Guid id, Race body, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            var row = await db.Races.FindAsync(id);
            if (row is null)
                return Results.NotFound();
            if (!CanReadUser(http, config, row.Login))
                return Results.Unauthorized();
            row.Comments = body.Comments;
            row.GoalTimeTicks = body.GoalTimeTicks;
            row.ElapsedTimeTicks = body.ElapsedTimeTicks;
            row.StartTime = body.StartTime;
            row.FinishTime = body.FinishTime;
            row.ElapsedDistanceKm = body.ElapsedDistanceKm;
            row.AltitudeGainKm = body.AltitudeGainKm;
            row.AltitudeLossKm = body.AltitudeLossKm;
            row.Publish = body.Publish;
            row.GroupRide = body.GroupRide;
            row.Timestamp = DateTime.UtcNow;
            await Touch(db, row.Login);
            await db.SaveChangesAsync();
            return Results.Ok(row);
        });

        api.MapDelete("/races/{id:guid}", async (Guid id, string login, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            if (!CanReadUser(http, config, login))
                return Results.Unauthorized();
            var row = await db.Races.FindAsync(id);
            if (row is null)
                return Results.NotFound();
            if (!IsAdmin(http, config) && !string.Equals(row.Login, login, StringComparison.Ordinal))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            db.Races.Remove(row);
            db.RaceDownloads.RemoveRange(db.RaceDownloads.Where(d => d.RaceId == id));
            db.Positions.RemoveRange(db.Positions.Where(p => p.RaceId == id));
            await Touch(db, login);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapPost("/races/{id:guid}/enable-download", async (Guid id, string login, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            if (!CanReadUser(http, config, login))
                return Results.Unauthorized();
            var race = await db.Races.FindAsync(id);
            if (race is null)
                return Results.NotFound();
            if (!IsAdmin(http, config)
                && !string.Equals(race.Login, login, StringComparison.Ordinal)
                && !race.Publish)
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (!await db.RaceDownloads.AnyAsync(d => d.Login == login && d.RaceId == id))
                db.RaceDownloads.Add(new RaceDownload { Login = login, RaceId = id });
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapPost("/races/{id:guid}/inhibit-download", async (Guid id, string login, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            if (!CanReadUser(http, config, login))
                return Results.Unauthorized();
            var row = await db.RaceDownloads.FindAsync(login, id);
            if (row is not null)
                db.RaceDownloads.Remove(row);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapPost("/races/generate", async (GenerateRaceRequest body, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            if (string.IsNullOrWhiteSpace(body.Login) || body.CourseId == Guid.Empty
                || !TimeSpan.TryParse(body.GoalTime, CultureInfo.InvariantCulture, out var goal))
                return Results.Ok(Guid.Empty);
            if (!CanReadUser(http, config, body.Login))
                return Results.Unauthorized();
            var course = await db.Courses.FindAsync(body.CourseId);
            if (course is null)
                return Results.Ok(Guid.Empty);
            var race = new RaceRow
            {
                RaceId = Guid.NewGuid(),
                CourseId = course.CourseId,
                Login = body.Login,
                GoalTimeTicks = goal.Ticks,
                ElapsedDistanceKm = course.ElapsedDistanceKm,
                AltitudeGainKm = course.AltitudeGainKm,
                AltitudeLossKm = course.AltitudeLossKm,
                StartTime = DateTime.UtcNow,
                FinishTime = DateTime.UtcNow,
                Timestamp = DateTime.UtcNow,
                AutoGenerated = true,
                Publish = true
            };
            db.Races.Add(race);
            db.RaceDownloads.Add(new RaceDownload { Login = body.Login, RaceId = race.RaceId });
            await db.SaveChangesAsync();
            return Results.Ok(race.RaceId);
        });

        api.MapGet("/races/{id:guid}/positions", async (Guid id, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            if (!TryLogin(http, config, out var login))
                return Results.Unauthorized();
            var race = await db.Races.FindAsync(id);
            if (race is null)
                return Results.NotFound();
            if (!await CanReadPositions(db, race, login))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            return Results.Ok(await db.Positions.Where(p => p.RaceId == id).OrderBy(p => p.TimestampTicks).ToListAsync());
        });

        api.MapPost("/races/{id:guid}/positions", async (Guid id, List<TrackPoint> body, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            var race = await db.Races.FindAsync(id);
            if (race is null)
                return Results.NotFound();
            if (!CanReadUser(http, config, race.Login))
                return Results.Unauthorized();
            // An empty post is a no-op. A finished ride posts its whole track, and
            // points from an earlier ride on this race are dropped.
            if (body.Count == 0)
                return Results.NoContent();
            var existing = await db.Positions.Where(p => p.RaceId == id).ToListAsync();
            var keep = new HashSet<long>();
            foreach (var point in body)
            {
                if (!keep.Add(point.TimestampTicks))
                {
                    var duplicate = existing.First(row => row.TimestampTicks == point.TimestampTicks);
                    duplicate.Latitude = point.Latitude;
                    duplicate.Longitude = point.Longitude;
                    continue;
                }
                var row = existing.FirstOrDefault(item => item.TimestampTicks == point.TimestampTicks);
                if (row is null)
                {
                    row = new PositionRow { RaceId = id, TimestampTicks = point.TimestampTicks };
                    existing.Add(row);
                    db.Positions.Add(row);
                }
                row.Latitude = point.Latitude;
                row.Longitude = point.Longitude;
            }
            foreach (var row in existing.Where(item => !keep.Contains(item.TimestampTicks)))
                db.Positions.Remove(row);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapGet("/admin/users", async (GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            if (!IsAdmin(http, config))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var users = await db.Users.OrderBy(u => u.Login).ToListAsync();
            return Results.Ok(users.Select(ToProfile));
        });

        MapWebsite(api);
        MapRouting(api);

        api.MapPost("/admin/users/{login}/active", async (string login, bool active, GpsTrainerDb db, HttpContext http, IConfiguration config) =>
        {
            if (!IsAdmin(http, config))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var user = await db.Users.FindAsync(login);
            if (user is null)
                return Results.NotFound();
            user.IsActive = active;
            await db.SaveChangesAsync();
            return Results.Ok(ToProfile(user));
        });
    }

    public static async Task SeedAsync(GpsTrainerDb db, IConfiguration config)
    {
        if (!await db.ActivityCodes.AnyAsync() && config.GetValue("GpsTrainer:SeedInferredActivityCodes", true))
        {
            var key = 0;
            foreach (var sport in PreferenceCatalog.Sports)
                db.ActivityCodes.Add(new ActivityCodeRow { ActKey = key++, ActValue = sport });
            await db.SaveChangesAsync();
        }
    }

    private static async Task<bool> CanReadPositions(GpsTrainerDb db, RaceRow race, string login)
    {
        if (string.Equals(race.Login, login, StringComparison.Ordinal))
            return true;
        // Interval times for a published ride are computed on the server and do not
        // return this trail. A signed-in rider who enabled the published ride for
        // download is syncing it as the ghost for their own ride.
        if (!race.Publish)
            return false;
        return await db.RaceDownloads.AnyAsync(d => d.Login == login && d.RaceId == race.RaceId);
    }

    private static async Task DeleteAccountAsync(GpsTrainerDb db, UserRow user)
    {
        var login = user.Login;
        var courseIds = await db.Courses.Where(c => c.Login == login).Select(c => c.CourseId).ToListAsync();
        var raceIds = await db.Races.Where(r => r.Login == login).Select(r => r.RaceId).ToListAsync();

        if (raceIds.Count > 0)
        {
            var referencing = await db.Races.Where(r => r.Login != login && raceIds.Contains(r.TargetRaceId)).ToListAsync();
            foreach (var race in referencing)
                race.TargetRaceId = Guid.Empty;
            db.Positions.RemoveRange(await db.Positions.Where(p => raceIds.Contains(p.RaceId)).ToListAsync());
            db.Races.RemoveRange(await db.Races.Where(r => r.Login == login).ToListAsync());
        }

        var raceDownloads = await db.RaceDownloads.Where(d => d.Login == login).ToListAsync();
        if (raceIds.Count > 0)
            raceDownloads.AddRange(await db.RaceDownloads.Where(d => d.Login != login && raceIds.Contains(d.RaceId)).ToListAsync());
        db.RaceDownloads.RemoveRange(raceDownloads);

        if (courseIds.Count > 0)
        {
            db.Waypoints.RemoveRange(await db.Waypoints.Where(w => courseIds.Contains(w.CourseId)).ToListAsync());
            db.CourseIntervals.RemoveRange(await db.CourseIntervals.Where(i => courseIds.Contains(i.CourseId)).ToListAsync());
            db.Courses.RemoveRange(await db.Courses.Where(c => c.Login == login).ToListAsync());
        }

        var courseDownloads = await db.CourseDownloads.Where(d => d.Login == login).ToListAsync();
        if (courseIds.Count > 0)
            courseDownloads.AddRange(await db.CourseDownloads.Where(d => d.Login != login && courseIds.Contains(d.CourseId)).ToListAsync());
        db.CourseDownloads.RemoveRange(courseDownloads);

        var promotionIds = await db.RacePromotions.Where(p => p.Login == login).Select(p => p.RacePromotionId).ToListAsync();
        db.RaceRegistrations.RemoveRange(await db.RaceRegistrations.Where(r => r.Login == login || promotionIds.Contains(r.RacePromotionId)).ToListAsync());
        db.RacePromotions.RemoveRange(await db.RacePromotions.Where(p => p.Login == login).ToListAsync());
        db.PasswordResets.RemoveRange(await db.PasswordResets.Where(r => r.Login == login).ToListAsync());
        db.Users.Remove(user);
    }

    private static async Task Touch(GpsTrainerDb db, string login)
    {
        var user = await db.Users.FindAsync(login);
        if (user is not null)
            user.UserTimestamp = DateTime.UtcNow;
    }

    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> WaypointSaveLocks = new();

    private static readonly string EphemeralSigningKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private static string SigningKey(IConfiguration config)
    {
        var key = config["GpsTrainer:AuthSigningKey"];
        // Empty means a process-local key. Nothing secret is committed. Set AuthSigningKey
        // outside the repo before any shared or long-lived deployment.
        return string.IsNullOrWhiteSpace(key) ? EphemeralSigningKey : key;
    }

    private static bool CanReadUser(HttpContext http, IConfiguration config, string login)
    {
        if (IsAdmin(http, config))
            return true;
        return TryLogin(http, config, out var tokenLogin) && tokenLogin == login;
    }

    // A course other people can open is one the owner published and an admin approved.
    private static bool CourseIsPublic(CourseRow course) => course.Publish && course.Approved;

    private static bool CanReadCourse(HttpContext http, IConfiguration config, CourseRow course) =>
        CourseIsPublic(course) || CanReadUser(http, config, course.Login);

    /// <summary>
    /// Road drawing and phone uploads store the unknown-altitude sentinel.
    /// A configured elevation provider fills those points before they are saved.
    /// A lookup failure leaves the sentinel in place and still saves the course.
    /// </summary>
    private static async Task TryFillElevationAsync(
        List<Waypoint> waypoints,
        CourseRow course,
        GpsTrainerDb db,
        IConfiguration config,
        IRoutingProviderFactory providers,
        RoutingSettingsStore store,
        CancellationToken cancellationToken)
    {
        if (!CourseElevation.NeedsLookup(waypoints))
            return;
        try
        {
            var resolved = await store.ResolveAsync(db, config, cancellationToken);
            if (!string.Equals(resolved.Provider, OpenRouteServiceRoutingProvider.ProviderName, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(resolved.ApiKey))
                return;
            var provider = providers.Get(resolved.Provider);
            if (provider is null)
                return;

            var unknown = waypoints
                .Where(point => !CourseEdits.IsInterval(point) && !SummitSearch.IsKnown(point.AltitudeKm))
                .ToList();
            var altitudes = new List<double?>(unknown.Count);
            for (var offset = 0; offset < unknown.Count; offset += CourseElevation.BatchSize)
            {
                var slice = unknown.Skip(offset).Take(CourseElevation.BatchSize).ToList();
                var response = await provider.ElevationLineAsync(new ElevationLineRequestDto
                {
                    Points = slice.Select(sample => new ElevationSampleDto
                    {
                        Latitude = sample.Latitude,
                        Longitude = sample.Longitude,
                        ElapsedKm = sample.ElapsedDistanceKm,
                        AltitudeKm = sample.AltitudeKm
                    }).ToList()
                }, resolved.ApiKey, cancellationToken);
                if (response is not { Found: true } || response.Points.Count != slice.Count)
                    return;
                altitudes.AddRange(response.Points.Select(sample => (double?)sample.AltitudeKm));
            }

            if (CourseElevation.Apply(waypoints, altitudes) == 0)
                return;
            var climb = ElevationProfile.GainLoss(waypoints);
            course.AltitudeGainKm = climb.GainKm;
            course.AltitudeLossKm = climb.LossKm;
        }
        catch (HttpRequestException)
        {
        }
        catch (TaskCanceledException)
        {
        }
    }

    // A missing tick used to be filled from 1, which collided with an explicit tick of 1
    // and threw on the primary key. Duplicate explicit ticks are rejected before any write.
    // Sea level (0 km) is stored as 0. Unknown elevation is the sentinel the client sends.
    private static bool TryStampWaypointRows(Guid courseId, IReadOnlyList<Waypoint> waypoints, out List<WaypointRow> rows)
    {
        rows = new List<WaypointRow>(waypoints.Count);
        var used = new HashSet<long>();
        foreach (var wp in waypoints)
        {
            if (wp.TimestampTicks != 0 && !used.Add(wp.TimestampTicks))
                return false;
        }
        var next = 1L;
        foreach (var wp in waypoints)
        {
            long tick;
            if (wp.TimestampTicks == 0)
            {
                while (!used.Add(next))
                    next++;
                tick = next;
                next++;
            }
            else
                tick = wp.TimestampTicks;
            rows.Add(new WaypointRow
            {
                CourseId = courseId,
                TimestampTicks = tick,
                Latitude = wp.Latitude,
                Longitude = wp.Longitude,
                Heading = wp.HeadingDegrees,
                WaypointType = (int)wp.WaypointType,
                AltitudeKm = wp.AltitudeKm,
                ElapsedDistanceKm = wp.ElapsedDistanceKm,
                TextDescription = wp.TextDescription
            });
        }
        return true;
    }

    private static bool IsAdmin(HttpContext http, IConfiguration config)
    {
        if (!TryLogin(http, config, out var login))
            return false;
        var admins = config.GetSection("GpsTrainer:AdminLogins").Get<string[]>() ?? [];
        return admins.Contains(login, StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryLogin(HttpContext http, IConfiguration config, out string login)
    {
        if (!AuthTokens.TryRead(ReadBearer(http), SigningKey(config), out login, out _, out var tokenId))
            return false;
        if (http.RequestServices.GetRequiredService<RevokedAuthTokens>().IsRevoked(tokenId))
        {
            login = "";
            return false;
        }
        // A token outlives the row. After the account is deleted, that token must stop.
        var signedIn = login;
        if (!http.RequestServices.GetRequiredService<GpsTrainerDb>().Users.Any(u => u.Login == signedIn))
        {
            login = "";
            return false;
        }
        return true;
    }

    private static string? ReadBearer(HttpContext http)
    {
        var header = http.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? header[prefix.Length..].Trim() : null;
    }

    private static UserProfileDto ToProfile(UserRow user) => new()
    {
        Login = user.Login,
        FirstName = user.FirstName,
        LastName = user.LastName,
        Country = user.Country,
        Street = user.Street,
        City = user.City,
        State = user.State,
        Email = user.Email,
        BirthDate = user.BirthDate,
        IsMale = user.IsMale,
        RacingCategoryKey = user.RacingCategory,
        IsActive = user.IsActive,
        PhoneNumber = user.PhoneNumber,
        WantsEmails = user.WantsEmails,
        ExpirationDate = user.ExpirationDate,
        Latitude = user.Latitude,
        Longitude = user.Longitude
    };

    private static CourseRow ToCourseRow(Course course) => new()
    {
        CourseId = course.CourseId,
        Name = course.Name,
        ElapsedDistanceKm = course.ElapsedDistanceKm,
        Comments = course.Comments,
        AltitudeGainKm = course.AltitudeGainKm,
        ActivityCode = course.ActivityCode,
        Publish = course.Publish,
        Login = course.Login,
        Approved = course.Approved,
        City = course.City,
        State = course.State,
        Difficulty = course.Difficulty,
        Url = course.Url,
        Description = course.Description,
        RaceDate = course.RaceDate,
        Laps = course.Laps,
        AltitudeLossKm = course.AltitudeLossKm,
        WaypointsVerified = course.WaypointsVerified,
        Timestamp = course.Timestamp,
        WaypointTimestamp = course.WaypointTimestamp
    };

    private static RaceRow ToRaceRow(Race race) => new()
    {
        RaceId = race.RaceId,
        CourseId = race.CourseId,
        ElapsedTimeTicks = race.ElapsedTimeTicks,
        Comments = race.Comments,
        GoalTimeTicks = race.GoalTimeTicks,
        StartTime = race.StartTime,
        FinishTime = race.FinishTime,
        StartPositionTicks = race.StartPositionTicks,
        ElapsedDistanceKm = race.ElapsedDistanceKm,
        AltitudeGainKm = race.AltitudeGainKm,
        AltitudeLossKm = race.AltitudeLossKm,
        TargetRaceId = race.TargetRaceId,
        Publish = race.Publish,
        GroupRide = race.GroupRide,
        Login = race.Login,
        Timestamp = race.Timestamp,
        AutoGenerated = race.AutoGenerated
    };
}
