using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GpsTrainer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GpsTrainer.Server.Tests;

public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _database = Path.Combine(Path.GetTempPath(), "gpstrainer-api-" + Guid.NewGuid().ToString("N") + ".db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:GpsTrainer", "Data Source=" + _database);
        builder.UseSetting("GpsTrainer:AuthSigningKey", "test-signing-key");
        builder.UseSetting("GpsTrainer:AdminLogins:0", "bud");
        builder.UseSetting("GpsTrainer:SeedInferredActivityCodes", "true");
        builder.UseSetting("GpsTrainer:ExposeResetTokens", "true");
        builder.UseSetting("GpsTrainer:PublicHost", "gps-trainer.com");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (File.Exists(_database))
            File.Delete(_database);
    }
}

public class ApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public ApiTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Signup_login_course_race_and_admin_follow_the_client_contract()
    {
        var signup = await _client.PostAsJsonAsync("/api/users", new UserWriteDto
        {
            Login = "bud",
            Password = "secret",
            Email = "bud@example.com",
            FirstName = "Bud",
            LastName = "Cribar",
            Country = "US",
            IsMale = true
        });
        var created = await signup.Content.ReadFromJsonAsync<InsertUserResponse>();
        Assert.Equal("ok", created!.Result);

        var duplicate = await _client.PostAsJsonAsync("/api/users", new UserWriteDto
        {
            Login = "bud",
            Password = "other",
            Email = "other@example.com"
        });
        Assert.Equal(SoapContract.DuplicateUserName, (await duplicate.Content.ReadFromJsonAsync<InsertUserResponse>())!.Result);

        var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Login = "bud", Password = "secret" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loginJson = await login.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret", loginJson, StringComparison.Ordinal);
        var session = System.Text.Json.JsonSerializer.Deserialize<LoginResponse>(loginJson, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.False(string.IsNullOrEmpty(session!.Token));
        Assert.Equal("bud", session.User.Login);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);

        var codes = await _client.GetFromJsonAsync<List<ActivityCode>>("/api/activity-codes");
        Assert.Contains(codes!, c => c.ActValue == "Cycling");

        var course = new Course { Name = "Creek Path", Login = "bud", ActivityCode = 1, Laps = 1 };
        var saved = await (await _client.PostAsJsonAsync("/api/courses", course)).Content.ReadFromJsonAsync<Course>();
        Assert.NotEqual(Guid.Empty, saved!.CourseId);

        var waypoints = new[]
        {
            new Waypoint { Latitude = 40.01, Longitude = -105.27, WaypointType = WaypointType.Start, TimestampTicks = 10 },
            new Waypoint { Latitude = 40.02, Longitude = -105.26, WaypointType = WaypointType.TurnLeft, TimestampTicks = 20, TextDescription = "onto Main" }
        };
        var put = await _client.PutAsJsonAsync($"/api/courses/{saved.CourseId}/waypoints", waypoints);
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        var loaded = await _client.GetFromJsonAsync<List<Waypoint>>($"/api/courses/{saved.CourseId}/waypoints");
        Assert.Equal(WaypointType.TurnLeft, loaded![1].WaypointType);
        Assert.Equal(Altitude.Unknown.Kilometers, loaded[1].AltitudeKm);

        var changed = new[]
        {
            new Waypoint { Latitude = 40.01, Longitude = -105.27, WaypointType = WaypointType.Start, TimestampTicks = 10, AltitudeKm = 1.520 },
            new Waypoint { Latitude = 40.02, Longitude = -105.26, WaypointType = WaypointType.Summit, TimestampTicks = 20, TextDescription = "onto Main", AltitudeKm = 1.610 }
        };
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutAsJsonAsync($"/api/courses/{saved.CourseId}/waypoints", changed)).StatusCode);
        var reloaded = await _client.GetFromJsonAsync<List<Waypoint>>($"/api/courses/{saved.CourseId}/waypoints");
        Assert.Equal(WaypointType.Summit, reloaded![1].WaypointType);
        Assert.Equal(1.520, reloaded[0].AltitudeKm, 5);
        Assert.Equal(1.610, reloaded[1].AltitudeKm, 5);

        var listed = await _client.GetFromJsonAsync<List<Course>>("/api/courses?login=bud");
        Assert.Contains(listed!, c => c.CourseId == saved.CourseId);

        await _client.PostAsync($"/api/courses/{saved.CourseId}/inhibit-download?login=bud", null);
        var hidden = await _client.GetFromJsonAsync<List<Course>>("/api/courses?login=bud");
        Assert.DoesNotContain(hidden!, c => c.CourseId == saved.CourseId);

        var race = new Race { CourseId = saved.CourseId, Login = "bud", Comments = "morning", GoalTime = TimeSpan.FromMinutes(30) };
        var savedRace = await (await _client.PostAsJsonAsync("/api/races", race)).Content.ReadFromJsonAsync<Race>();
        await _client.PostAsJsonAsync($"/api/races/{savedRace!.RaceId}/positions", new[]
        {
            new TrackPoint { TimestampTicks = 100, Latitude = 40.01, Longitude = -105.27 }
        });
        var points = await _client.GetFromJsonAsync<List<TrackPoint>>($"/api/races/{savedRace.RaceId}/positions");
        Assert.Single(points!);

        var status = await _client.GetFromJsonAsync<UserStatus>("/api/users/bud/status");
        Assert.True(status!.IsActive);

        var admins = await _client.GetFromJsonAsync<List<UserProfileDto>>("/api/admin/users");
        Assert.Contains(admins!, u => u.Login == "bud");

        var denied = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Login = "bud", Password = "wrong" });
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Fact]
    public async Task Login_ignores_trailing_spaces_only_and_duplicate_signup_ignores_case()
    {
        using var client = _factory.CreateClient();
        var signup = await client.PostAsJsonAsync("/api/users", new UserWriteDto
        {
            Login = "budcribar",
            Password = "secret  ",
            Email = "budcribar@example.com"
        });
        Assert.Equal(SoapContract.InsertUserOk, (await signup.Content.ReadFromJsonAsync<InsertUserResponse>())!.Result);

        foreach (var attempt in new[]
        {
            new LoginRequest { Login = "BudCribar", Password = "secret  " },
            new LoginRequest { Login = "budcribar   ", Password = "secret  " },
            new LoginRequest { Login = "budcribar", Password = "secret" }
        })
        {
            var login = await client.PostAsJsonAsync("/api/auth/login", attempt);
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var session = await login.Content.ReadFromJsonAsync<LoginResponse>();
            Assert.Equal("budcribar", session!.User.Login);
            Assert.False(string.IsNullOrEmpty(session.Token));
        }

        var leadingLogin = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Login = " budcribar", Password = "secret  " });
        Assert.Equal(HttpStatusCode.Unauthorized, leadingLogin.StatusCode);

        var leadingPassword = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Login = "budcribar", Password = " secret" });
        Assert.Equal(HttpStatusCode.Unauthorized, leadingPassword.StatusCode);

        var duplicate = await client.PostAsJsonAsync("/api/users", new UserWriteDto
        {
            Login = "BudCribar ",
            Password = "other",
            Email = "budcribar-other@example.com"
        });
        Assert.Equal(SoapContract.DuplicateUserName, (await duplicate.Content.ReadFromJsonAsync<InsertUserResponse>())!.Result);
    }

    [Fact]
    public async Task Password_reset_has_no_shared_answer_and_sql_execution_is_gone()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync("/api/sql", new StringContent("select 1"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync("/WebService.asmx/ExecuteSQLCommand", new StringContent("select 1"))).StatusCode);

        await _client.PostAsJsonAsync("/api/users", new UserWriteDto
        {
            Login = "rider",
            Password = "secret",
            Email = "rider@example.com"
        });
        var forgot = await _client.PostAsJsonAsync("/api/auth/forgot", new ForgotPasswordRequest { Login = "rider" });
        var body = await forgot.Content.ReadFromJsonAsync<ForgotPasswordResponse>();
        Assert.True(body!.Accepted);
        Assert.False(string.IsNullOrEmpty(body.ResetToken));
        Assert.DoesNotContain("secret", await forgot.Content.ReadAsStringAsync());

        var reset = await _client.PostAsJsonAsync("/api/auth/reset", new ResetPasswordRequest
        {
            Login = "rider",
            Token = body.ResetToken!,
            NewPassword = "longer-secret"
        });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Login = "rider", Password = "secret" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Login = "rider", Password = "longer-secret" })).StatusCode);

        var site = await _client.GetFromJsonAsync<SiteInfoDto>("/api/site");
        Assert.Equal("gps-trainer.com", site!.Domain);
    }

    [Fact]
    public async Task Interval_results_time_a_race_that_crosses_both_markers()
    {
        await _client.PostAsJsonAsync("/api/users", new UserWriteDto { Login = "timer", Password = "secret", Email = "timer@example.com" });
        var session = await (await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Login = "timer", Password = "secret" })).Content.ReadFromJsonAsync<LoginResponse>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.Token);

        var course = await (await _client.PostAsJsonAsync("/api/courses", new Course { Name = "Intervals", Login = "timer" })).Content.ReadFromJsonAsync<Course>();
        var t0 = new DateTime(2013, 10, 13, 12, 0, 0, DateTimeKind.Utc).Ticks;
        await _client.PutAsJsonAsync($"/api/courses/{course!.CourseId}/waypoints", new[]
        {
            new Waypoint { Latitude = 40.00, Longitude = -105, WaypointType = WaypointType.Start, TimestampTicks = 1, ElapsedDistanceKm = 0 },
            new Waypoint { Latitude = 40.01, Longitude = -105, WaypointType = WaypointType.StartInterval, TimestampTicks = 2, ElapsedDistanceKm = 1.1 },
            new Waypoint { Latitude = 40.02, Longitude = -105, WaypointType = WaypointType.FinishInterval, TimestampTicks = 3, ElapsedDistanceKm = 2.2 },
            new Waypoint { Latitude = 40.03, Longitude = -105, WaypointType = WaypointType.Finish, TimestampTicks = 4, ElapsedDistanceKm = 3.3 }
        });
        var check = await (await _client.PostAsync($"/api/courses/{course.CourseId}/validate", null)).Content.ReadFromJsonAsync<ValidationResultDto>();
        Assert.True(check!.Ok, check.Message);

        var race = await (await _client.PostAsJsonAsync("/api/races", new Race { CourseId = course.CourseId, Login = "timer", Publish = true })).Content.ReadFromJsonAsync<Race>();
        var track = new List<TrackPoint>();
        for (var i = 0; i <= 6; i++)
        {
            track.Add(new TrackPoint
            {
                Latitude = 40.0 + i * 0.005,
                Longitude = -105,
                TimestampTicks = t0 + TimeSpan.FromSeconds(i * 10).Ticks
            });
        }
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsJsonAsync($"/api/races/{race!.RaceId}/positions", track)).StatusCode);

        var rows = await _client.GetFromJsonAsync<List<IntervalResultDto>>($"/api/courses/{course.CourseId}/interval-results");
        var row = Assert.Single(rows!);
        Assert.Single(row.Splits);
        Assert.StartsWith("00:", row.Total);

        using var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/courses?login=timer")).StatusCode);
    }

    [Fact]
    public async Task Posting_a_finished_track_replaces_the_previous_ghost()
    {
        await _client.PostAsJsonAsync("/api/users", new UserWriteDto { Login = "ghost", Password = "secret", Email = "ghost@example.com" });
        var session = await (await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Login = "ghost", Password = "secret" })).Content.ReadFromJsonAsync<LoginResponse>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.Token);

        var course = await (await _client.PostAsJsonAsync("/api/courses", new Course { Name = "Ghost", Login = "ghost" })).Content.ReadFromJsonAsync<Course>();
        var race = await (await _client.PostAsJsonAsync("/api/races", new Race { CourseId = course!.CourseId, Login = "ghost" })).Content.ReadFromJsonAsync<Race>();
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsJsonAsync($"/api/races/{race!.RaceId}/positions", new[]
        {
            new TrackPoint { TimestampTicks = 1, Latitude = 1, Longitude = 1 },
            new TrackPoint { TimestampTicks = 2, Latitude = 2, Longitude = 2 }
        })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsJsonAsync($"/api/races/{race.RaceId}/positions", new[]
        {
            new TrackPoint { TimestampTicks = 9, Latitude = 40, Longitude = -105 }
        })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsJsonAsync($"/api/races/{race.RaceId}/positions", Array.Empty<TrackPoint>())).StatusCode);

        var points = await _client.GetFromJsonAsync<List<TrackPoint>>($"/api/races/{race.RaceId}/positions");
        var point = Assert.Single(points!);
        Assert.Equal(9, point.TimestampTicks);
        Assert.Equal(40, point.Latitude);
    }

    [Fact]
    public void Postgres_is_a_configured_provider_and_unknown_names_are_rejected()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<GpsTrainer.Server.Data.GpsTrainerDb>();
        GpsTrainer.Server.Data.DatabaseSetup.UseProvider(options, "Postgres", "Host=127.0.0.1;Database=gpstrainer;Username=gpstrainer;Password=");
        Assert.Contains(options.Options.Extensions, e => e.GetType().Name.Contains("Npgsql", StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() =>
            GpsTrainer.Server.Data.DatabaseSetup.UseProvider(new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<GpsTrainer.Server.Data.GpsTrainerDb>(), "SqlServer", "Server=localhost"));
    }

    [Fact]
    public async Task Site_publishes_optional_store_urls_without_inventing_them()
    {
        var site = await _client.GetFromJsonAsync<SiteInfoDto>("/api/site");
        Assert.Equal("", site!.GooglePlayUrl);
        Assert.Equal("", site.AppStoreUrl);

        var settings = File.ReadAllText(Path.Combine(RepoRoot(), "src", "GpsTrainer.Server", "appsettings.json"));
        Assert.Contains("\"GooglePlayUrl\": \"\"", settings, StringComparison.Ordinal);
        Assert.Contains("\"AppStoreUrl\": \"\"", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("play.google.com", settings, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apps.apple.com", settings, StringComparison.OrdinalIgnoreCase);

        using var configured = new StoreUrlFactory();
        var set = await configured.CreateClient().GetFromJsonAsync<SiteInfoDto>("/api/site");
        Assert.Equal("https://example.com/play", set!.GooglePlayUrl);
        Assert.Equal("https://example.com/ios", set.AppStoreUrl);

        using var rejected = new RejectedStoreFactory();
        var blank = await rejected.CreateClient().GetFromJsonAsync<SiteInfoDto>("/api/site");
        Assert.Equal("", blank!.GooglePlayUrl);
        Assert.Equal("", blank.AppStoreUrl);
    }

    [Fact]
    public async Task Deleting_an_account_removes_its_data_and_keeps_another_riders_race()
    {
        using var owner = _factory.CreateClient();
        using var other = _factory.CreateClient();
        await owner.PostAsJsonAsync("/api/users", new UserWriteDto { Login = "owner-del", Password = "secret", Email = "owner-del@example.com" });
        await other.PostAsJsonAsync("/api/users", new UserWriteDto { Login = "other-del", Password = "secret", Email = "other-del@example.com" });
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await Token(owner, "owner-del"));
        other.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await Token(other, "other-del"));

        var course = await (await owner.PostAsJsonAsync("/api/courses", new Course { Name = "Owned", Login = "owner-del" })).Content.ReadFromJsonAsync<Course>();
        await owner.PutAsJsonAsync($"/api/courses/{course!.CourseId}/waypoints", new[]
        {
            new Waypoint { Latitude = 40.1, Longitude = -105.2, WaypointType = WaypointType.Start, TimestampTicks = 1 }
        });
        var race = await (await owner.PostAsJsonAsync("/api/races", new Race
        {
            CourseId = course.CourseId,
            Login = "owner-del",
            Publish = false,
            Comments = "private ride"
        })).Content.ReadFromJsonAsync<Race>();
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync($"/api/races/{race!.RaceId}/positions", new[]
        {
            new TrackPoint { TimestampTicks = 5, Latitude = 40.2, Longitude = -105.3 }
        })).StatusCode);
        await owner.PostAsJsonAsync("/api/promotions", new RacePromotionDto
        {
            CourseId = course.CourseId,
            Login = "owner-del",
            RaceDateStart = DateTime.UtcNow,
            RaceDateFinish = DateTime.UtcNow.AddDays(1)
        });
        var forgot = await owner.PostAsJsonAsync("/api/auth/forgot", new ForgotPasswordRequest { Login = "owner-del" });
        var reset = await forgot.Content.ReadFromJsonAsync<ForgotPasswordResponse>();
        Assert.False(string.IsNullOrEmpty(reset!.ResetToken));

        var otherCourse = await (await other.PostAsJsonAsync("/api/courses", new Course { Name = "Theirs", Login = "other-del" })).Content.ReadFromJsonAsync<Course>();
        var otherRace = await (await other.PostAsJsonAsync("/api/races", new Race
        {
            CourseId = otherCourse!.CourseId,
            Login = "other-del",
            TargetRaceId = race.RaceId,
            Comments = "against the ghost"
        })).Content.ReadFromJsonAsync<Race>();
        Assert.Equal(HttpStatusCode.NoContent, (await other.PostAsJsonAsync($"/api/races/{otherRace!.RaceId}/positions", new[]
        {
            new TrackPoint { TimestampTicks = 8, Latitude = 41, Longitude = -106 }
        })).StatusCode);
        await other.PostAsync($"/api/courses/{course.CourseId}/enable-download?login=other-del", null);

        using var earlier = _factory.CreateClient();
        earlier.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await Token(owner, "owner-del"));

        var refused = await owner.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/users/owner-del")
        {
            Content = JsonContent.Create(new AccountDeletionRequest { Confirmation = "nope" })
        });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/users/owner-del")
        {
            Content = JsonContent.Create(new AccountDeletionRequest { Confirmation = "DELETE" })
        })).StatusCode);

        var deleted = await owner.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/users/owner-del")
        {
            Content = JsonContent.Create(new AccountDeletionRequest { Confirmation = "secret" })
        });
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.GetAsync("/api/users/owner-del")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await earlier.GetAsync("/api/users/owner-del")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await earlier.PostAsJsonAsync("/api/courses", new Course { Name = "After", Login = "owner-del" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/courses/{course.CourseId}")).StatusCode);
        Assert.Empty((await other.GetFromJsonAsync<List<Waypoint>>($"/api/courses/{course.CourseId}/waypoints"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/races/{race.RaceId}/positions")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.PostAsJsonAsync("/api/auth/reset", new ResetPasswordRequest
        {
            Login = "owner-del",
            Token = reset.ResetToken!,
            NewPassword = "another-secret"
        })).StatusCode);

        var kept = await other.GetFromJsonAsync<Race>($"/api/races/{otherRace.RaceId}");
        Assert.Equal(Guid.Empty, kept!.TargetRaceId);
        Assert.Equal("against the ghost", kept.Comments);
        var keptPoints = await other.GetFromJsonAsync<List<TrackPoint>>($"/api/races/{otherRace.RaceId}/positions");
        Assert.Equal(8, Assert.Single(keptPoints!).TimestampTicks);
        var promotions = await other.GetFromJsonAsync<List<RacePromotionDto>>("/api/promotions");
        Assert.DoesNotContain(promotions!, p => p.Login == "owner-del");
        var downloads = await other.GetFromJsonAsync<List<Course>>("/api/courses?login=other-del");
        Assert.DoesNotContain(downloads!, c => c.CourseId == course.CourseId);
        Assert.Contains(downloads!, c => c.CourseId == otherCourse.CourseId);
    }

    [Fact]
    public async Task Race_positions_are_visible_to_the_owner_and_a_rider_who_downloaded_a_published_ghost()
    {
        using var owner = _factory.CreateClient();
        using var rider = _factory.CreateClient();
        using var stranger = _factory.CreateClient();
        await owner.PostAsJsonAsync("/api/users", new UserWriteDto { Login = "trail-owner", Password = "secret", Email = "trail-owner@example.com" });
        await rider.PostAsJsonAsync("/api/users", new UserWriteDto { Login = "trail-rider", Password = "secret", Email = "trail-rider@example.com" });
        await stranger.PostAsJsonAsync("/api/users", new UserWriteDto { Login = "trail-stranger", Password = "secret", Email = "trail-stranger@example.com" });
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await Token(owner, "trail-owner"));
        rider.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await Token(rider, "trail-rider"));
        stranger.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await Token(stranger, "trail-stranger"));

        var course = await (await owner.PostAsJsonAsync("/api/courses", new Course { Name = "Trail", Login = "trail-owner" })).Content.ReadFromJsonAsync<Course>();
        var published = await (await owner.PostAsJsonAsync("/api/races", new Race
        {
            CourseId = course!.CourseId,
            Login = "trail-owner",
            Publish = true
        })).Content.ReadFromJsonAsync<Race>();
        var priv = await (await owner.PostAsJsonAsync("/api/races", new Race
        {
            CourseId = course.CourseId,
            Login = "trail-owner",
            Publish = false
        })).Content.ReadFromJsonAsync<Race>();
        foreach (var id in new[] { published!.RaceId, priv!.RaceId })
        {
            Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync($"/api/races/{id}/positions", new[]
            {
                new TrackPoint { TimestampTicks = 3, Latitude = 40, Longitude = -105 }
            })).StatusCode);
        }

        using var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/races/{published.RaceId}/positions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync($"/api/races/{published.RaceId}/positions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.PostAsync($"/api/races/{priv.RaceId}/enable-download?login=trail-stranger", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync($"/api/races/{priv.RaceId}/positions")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await rider.PostAsync($"/api/races/{published.RaceId}/enable-download?login=trail-rider", null)).StatusCode);
        var ghost = await rider.GetFromJsonAsync<List<TrackPoint>>($"/api/races/{published.RaceId}/positions");
        Assert.Equal(40, Assert.Single(ghost!).Latitude);
        var own = await owner.GetFromJsonAsync<List<TrackPoint>>($"/api/races/{priv.RaceId}/positions");
        Assert.Single(own!);

        var rows = await anonymous.GetFromJsonAsync<List<IntervalResultDto>>($"/api/courses/{course.CourseId}/interval-results");
        Assert.NotNull(rows);
    }

    private static async Task<string> Token(HttpClient client, string login)
    {
        var session = await (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Login = login, Password = "secret" })).Content.ReadFromJsonAsync<LoginResponse>();
        return session!.Token;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GPS-Trainer.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private sealed class StoreUrlFactory : ApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Stores:GooglePlayUrl", "  https://example.com/play  ");
            builder.UseSetting("Stores:AppStoreUrl", "https://example.com/ios");
        }
    }

    private sealed class RejectedStoreFactory : ApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Stores:GooglePlayUrl", "javascript:alert(1)");
            builder.UseSetting("Stores:AppStoreUrl", "   ");
        }
    }
}
