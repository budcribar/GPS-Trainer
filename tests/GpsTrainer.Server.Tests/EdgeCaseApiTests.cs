using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using GpsTrainer.Server.Security;

namespace GpsTrainer.Server.Tests;

/// <summary>
/// Course CRUD, account deletion, validation, and authorization.
/// Tests marked Category=KnownBug assert the correct behavior and fail while the bug remains.
/// </summary>
public class EdgeCaseApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public EdgeCaseApiTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Course_crud_lists_renames_replaces_waypoints_and_deletes()
    {
        var owner = await Rider("crud");
        var created = await CreateCourse(owner, "North loop", publish: false);
        var listed = await owner.Client.GetFromJsonAsync<List<Course>>("/api/courses?login=" + owner.Login);
        Assert.Contains(listed!, course => course.CourseId == created.CourseId && course.Name == "North loop");

        created.Name = "北环线 café";
        created.Comments = "<b>not markup</b>";
        var renamed = await owner.Client.PutAsJsonAsync($"/api/courses/{created.CourseId}", created);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var loaded = await owner.Client.GetFromJsonAsync<Course>($"/api/courses/{created.CourseId}");
        Assert.Equal("北环线 café", loaded!.Name);
        Assert.Equal("<b>not markup</b>", loaded.Comments);

        var waypoints = Enum.GetValues<WaypointType>().Select((type, index) => new Waypoint
        {
            Latitude = 40.5 + index * 0.01,
            Longitude = -105.1 - index * 0.01,
            WaypointType = type,
            TimestampTicks = 1000 + index,
            ElapsedDistanceKm = index * 0.2,
            AltitudeKm = 1.25 + index * 0.001,
            HeadingDegrees = index,
            TextDescription = "type " + type
        }).ToArray();
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.PutAsJsonAsync($"/api/courses/{created.CourseId}/waypoints", waypoints)).StatusCode);
        var stored = await owner.Client.GetFromJsonAsync<List<Waypoint>>($"/api/courses/{created.CourseId}/waypoints");
        Assert.Equal(waypoints.Length, stored!.Count);
        Assert.Equal(waypoints.Select(point => point.WaypointType), stored.Select(point => point.WaypointType));
        Assert.Equal(1.25, stored[0].AltitudeKm, 5);

        var xml = await owner.Client.GetStringAsync($"/api/courses/{created.CourseId}/export/gpx");
        Assert.Contains("北环线 café", xml, StringComparison.Ordinal);
        Assert.Contains("type Summit", xml, StringComparison.Ordinal);
        Assert.Contains("Summit", xml, StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.DeleteAsync($"/api/courses/{created.CourseId}?login={owner.Login}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.GetAsync($"/api/courses/{created.CourseId}")).StatusCode);
        var after = await owner.Client.GetFromJsonAsync<List<Course>>("/api/courses?login=" + owner.Login);
        Assert.DoesNotContain(after!, course => course.CourseId == created.CourseId);
    }

    [Fact]
    public async Task Duplicate_names_unicode_and_html_are_stored_as_text()
    {
        var owner = await Rider("name");
        var first = await CreateCourse(owner, "Same name", publish: false);
        var second = await CreateCourse(owner, "Same name", publish: false);
        Assert.NotEqual(first.CourseId, second.CourseId);
        var html = await CreateCourse(owner, "<img src=x onerror=alert(1)>", publish: false);
        var loaded = await owner.Client.GetFromJsonAsync<Course>($"/api/courses/{html.CourseId}");
        Assert.Equal("<img src=x onerror=alert(1)>", loaded!.Name);

        var blank = await owner.Client.PostAsJsonAsync("/api/courses", new Course { Name = "", Login = owner.Login, Publish = false });
        Assert.Equal(HttpStatusCode.OK, blank.StatusCode);
        Assert.Equal("", (await blank.Content.ReadFromJsonAsync<Course>())!.Name);
    }

    [Fact]
    public async Task One_waypoint_and_an_empty_course_fail_validation_without_being_stored_as_verified()
    {
        var owner = await Rider("valid");
        var course = await CreateCourse(owner, "Short", publish: false);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.PutAsJsonAsync($"/api/courses/{course.CourseId}/waypoints", new[]
        {
            new Waypoint { Latitude = 40.01, Longitude = -105.27, WaypointType = WaypointType.Start, TimestampTicks = 1, AltitudeKm = 1.5 }
        })).StatusCode);

        var validated = await owner.Client.PostAsync($"/api/courses/{course.CourseId}/validate", null);
        var body = await validated.Content.ReadFromJsonAsync<ValidationResultDto>();
        Assert.Equal(HttpStatusCode.OK, validated.StatusCode);
        Assert.False(body!.Ok);
        Assert.False(string.IsNullOrWhiteSpace(body.Message));

        var empty = await CreateCourse(owner, "Empty", publish: false);
        var emptyResult = await (await owner.Client.PostAsync($"/api/courses/{empty.CourseId}/validate", null))
            .Content.ReadFromJsonAsync<ValidationResultDto>();
        Assert.False(emptyResult!.Ok);
        Assert.Equal("Course is too short", emptyResult.Message);
    }

    [Fact]
    public async Task Gpx_round_trip_keeps_types_order_and_nonzero_elevation()
    {
        var owner = await Rider("gpx");
        var course = await CreateCourse(owner, "Export loop", publish: false);
        var waypoints = new[]
        {
            new Waypoint { Latitude = 40.00, Longitude = -105.00, WaypointType = WaypointType.Start, TimestampTicks = 10, ElapsedDistanceKm = 0, AltitudeKm = 1.610, TextDescription = "start" },
            new Waypoint { Latitude = 40.01, Longitude = -105.01, WaypointType = WaypointType.BearLeft, TimestampTicks = 20, ElapsedDistanceKm = 0.4, AltitudeKm = 1.620, TextDescription = "bear" },
            new Waypoint { Latitude = 40.02, Longitude = -105.02, WaypointType = WaypointType.FeedZone, TimestampTicks = 30, ElapsedDistanceKm = 0.8, AltitudeKm = 1.500, TextDescription = "feed" },
            new Waypoint { Latitude = 40.00, Longitude = -105.00, WaypointType = WaypointType.Finish, TimestampTicks = 40, ElapsedDistanceKm = 1.2, AltitudeKm = 1.610, TextDescription = "finish" }
        };
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.PutAsJsonAsync($"/api/courses/{course.CourseId}/waypoints", waypoints)).StatusCode);

        using var export = new HttpRequestMessage(HttpMethod.Get, $"/api/courses/{course.CourseId}/export/gpx");
        export.Content = new StringContent("", Encoding.UTF8, "application/gpx+xml");
        var xml = await (await owner.Client.SendAsync(export)).Content.ReadAsStringAsync();
        using var import = new HttpRequestMessage(HttpMethod.Post, "/api/courses/import/gpx?fileName=export.gpx");
        import.Content = new StringContent(xml, Encoding.UTF8, "application/gpx+xml");
        var preview = await (await owner.Client.SendAsync(import)).Content.ReadFromJsonAsync<GpxImportResult>();
        Assert.NotNull(preview);
        Assert.True(preview!.PreservedGpsTrainerExtensions);
        Assert.Equal(waypoints.Select(point => point.WaypointType), preview.Waypoints.Select(point => point.WaypointType));
        Assert.Equal(waypoints.Select(point => point.TimestampTicks), preview.Waypoints.Select(point => point.TimestampTicks));
        Assert.Equal(1.610, preview.Waypoints[0].AltitudeKm, 3);
        Assert.Equal(1.620, preview.Waypoints[1].AltitudeKm, 3);
    }

    [Fact]
    public async Task Signup_rejects_blanks_and_exact_duplicates_and_a_normal_password_can_be_changed()
    {
        using var client = _factory.CreateClient();
        var missing = await client.PostAsJsonAsync("/api/users", new UserWriteDto { Login = " ", Password = " ", Email = "nope" });
        Assert.Equal("MissingLogin", (await missing.Content.ReadFromJsonAsync<InsertUserResponse>())!.Result);

        var owner = await Rider("pw");
        var again = await owner.Client.PostAsJsonAsync("/api/users", new UserWriteDto
        {
            Login = owner.Login.ToUpperInvariant(),
            Password = "other-password",
            Email = "other-" + owner.Login + "@example.com"
        });
        Assert.Equal(SoapContract.DuplicateUserName, (await again.Content.ReadFromJsonAsync<InsertUserResponse>())!.Result);

        var sameEmail = await owner.Client.PostAsJsonAsync("/api/users", new UserWriteDto
        {
            Login = owner.Login + "2",
            Password = "other-password",
            Email = owner.Login + "@example.com"
        });
        Assert.Equal(SoapContract.DuplicateEmail, (await sameEmail.Content.ReadFromJsonAsync<InsertUserResponse>())!.Result);

        var wrong = await owner.Client.PostAsJsonAsync("/api/auth/password", new PasswordChangeRequest
        {
            CurrentPassword = "not-the-password",
            NewPassword = "replacement-password"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        var changed = await owner.Client.PostAsJsonAsync("/api/auth/password", new PasswordChangeRequest
        {
            CurrentPassword = owner.Password,
            NewPassword = "replacement-password"
        });
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        using var fresh = _factory.CreateClient();
        var signed = await fresh.PostAsJsonAsync("/api/auth/login", new LoginRequest { Login = owner.Login, Password = "replacement-password" });
        Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await fresh.PostAsJsonAsync("/api/auth/login", new LoginRequest { Login = owner.Login, Password = owner.Password })).StatusCode);
    }

    [Fact]
    public async Task The_owner_can_delete_the_account_and_a_stranger_cannot()
    {
        var owner = await Rider("del");
        var stranger = await Rider("spy");
        var course = await CreateCourse(owner, "Goes away", publish: false);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.PutAsJsonAsync($"/api/courses/{course.CourseId}/waypoints", new[]
        {
            new Waypoint { Latitude = 40.1, Longitude = -105.2, WaypointType = WaypointType.Start, TimestampTicks = 1, AltitudeKm = 1.5 },
            new Waypoint { Latitude = 40.2, Longitude = -105.1, WaypointType = WaypointType.Finish, TimestampTicks = 2, AltitudeKm = 1.6, ElapsedDistanceKm = 5 }
        })).StatusCode);

        var rejected = await stranger.Client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/users/" + owner.Login)
        {
            Content = JsonContent.Create(new AccountDeletionRequest { Confirmation = "DELETE" })
        });
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.Client.GetAsync("/api/users/" + owner.Login)).StatusCode);

        var badWord = await owner.Client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/users/" + owner.Login)
        {
            Content = JsonContent.Create(new AccountDeletionRequest { Confirmation = "delete" })
        });
        Assert.Equal(HttpStatusCode.BadRequest, badWord.StatusCode);

        var deleted = await owner.Client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/users/" + owner.Login)
        {
            Content = JsonContent.Create(new AccountDeletionRequest { Confirmation = "DELETE" })
        });
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.Client.GetAsync("/api/users/" + owner.Login)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.Client.GetAsync("/api/courses?login=" + owner.Login)).StatusCode);
    }

    [Fact]
    public async Task A_stranger_cannot_edit_or_delete_another_course_and_bad_tokens_are_rejected()
    {
        var owner = await Rider("own");
        var stranger = await Rider("other");
        var course = await CreateCourse(owner, "Private", publish: false);
        course.Name = "Stolen";
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.Client.PutAsJsonAsync($"/api/courses/{course.CourseId}", course)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.Client.DeleteAsync($"/api/courses/{course.CourseId}?login={stranger.Login}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.Client.PutAsJsonAsync($"/api/courses/{course.CourseId}/waypoints", new[]
        {
            new Waypoint { Latitude = 1, Longitude = 2, WaypointType = WaypointType.Start, TimestampTicks = 1, AltitudeKm = 1 }
        })).StatusCode);

        using var bare = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await bare.GetAsync("/api/courses?login=" + owner.Login)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await bare.GetAsync("/api/users/" + owner.Login)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await bare.PutAsJsonAsync($"/api/courses/{course.CourseId}", course)).StatusCode);

        var expired = AuthTokens.Issue(owner.Login, "test-signing-key", DateTimeOffset.UtcNow.AddMinutes(-5));
        using var stale = _factory.CreateClient();
        stale.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expired);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stale.GetAsync("/api/courses?login=" + owner.Login)).StatusCode);
        stale.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await stale.GetAsync("/api/users/" + owner.Login)).StatusCode);

        using var garbage = new HttpRequestMessage(HttpMethod.Post, "/api/courses");
        garbage.Content = new StringContent("{", Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await bare.SendAsync(garbage)).StatusCode);

        var still = await owner.Client.GetFromJsonAsync<Course>($"/api/courses/{course.CourseId}");
        Assert.Equal("Private", still!.Name);
    }

    [Fact]
    public async Task KnownBug_Parallel_waypoint_saves_leave_one_complete_list()
    {
        var owner = await Rider("race");
        var course = await CreateCourse(owner, "Race", publish: false);
        var first = new[]
        {
            new Waypoint { Latitude = 40.1, Longitude = -105.1, WaypointType = WaypointType.Start, TimestampTicks = 11, AltitudeKm = 1.5 },
            new Waypoint { Latitude = 40.2, Longitude = -105.2, WaypointType = WaypointType.Finish, TimestampTicks = 12, ElapsedDistanceKm = 1, AltitudeKm = 1.6 }
        };
        var second = new[]
        {
            new Waypoint { Latitude = 41.1, Longitude = -106.1, WaypointType = WaypointType.Start, TimestampTicks = 21, AltitudeKm = 1.7 },
            new Waypoint { Latitude = 41.2, Longitude = -106.2, WaypointType = WaypointType.TurnLeft, TimestampTicks = 22, ElapsedDistanceKm = 1, AltitudeKm = 1.8, TextDescription = "left" },
            new Waypoint { Latitude = 41.3, Longitude = -106.3, WaypointType = WaypointType.Finish, TimestampTicks = 23, ElapsedDistanceKm = 2, AltitudeKm = 1.9 }
        };
        var url = $"/api/courses/{course.CourseId}/waypoints";
        var together = await Task.WhenAll(owner.Client.PutAsJsonAsync(url, first), owner.Client.PutAsJsonAsync(url, second));
        Assert.All(together, response => Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode));
        Assert.All(together, response => Assert.Equal(HttpStatusCode.NoContent, response.StatusCode));
        var stored = await owner.Client.GetFromJsonAsync<List<Waypoint>>(url);
        var ticks = stored!.Select(point => point.TimestampTicks).OrderBy(tick => tick).ToArray();
        Assert.True(
            ticks.SequenceEqual([11L, 12L]) || ticks.SequenceEqual([21L, 22L, 23L]),
            "stored " + string.Join(",", ticks));
    }

    [Fact]
    public async Task KnownBug_An_unpublished_course_is_not_readable_without_the_owner()
    {
        var owner = await Rider("priv");
        var course = await CreateCourse(owner, "Hidden loop", publish: false);
        Assert.False(course.Approved);
        await owner.Client.PutAsJsonAsync($"/api/courses/{course.CourseId}/waypoints", new[]
        {
            new Waypoint { Latitude = 40.11, Longitude = -105.22, WaypointType = WaypointType.Start, TimestampTicks = 5, AltitudeKm = 1.5, TextDescription = "home street" },
            new Waypoint { Latitude = 40.19, Longitude = -105.11, WaypointType = WaypointType.Finish, TimestampTicks = 6, ElapsedDistanceKm = 4, AltitudeKm = 1.6 }
        });

        using var stranger = _factory.CreateClient();
        var courseResponse = await stranger.GetAsync($"/api/courses/{course.CourseId}");
        var waypointResponse = await stranger.GetAsync($"/api/courses/{course.CourseId}/waypoints");
        var essential = await stranger.GetAsync($"/api/courses/{course.CourseId}/waypoints/essential");
        Assert.True(courseResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound or HttpStatusCode.Forbidden);
        Assert.True(waypointResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound or HttpStatusCode.Forbidden);
        Assert.True(essential.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound or HttpStatusCode.Forbidden);

        course.Publish = true;
        course.Approved = false;
        Assert.Equal(HttpStatusCode.OK, (await owner.Client.PutAsJsonAsync($"/api/courses/{course.CourseId}", course)).StatusCode);
        var unapproved = await stranger.GetAsync($"/api/courses/{course.CourseId}/waypoints");
        Assert.True(unapproved.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound or HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task KnownBug_Another_user_cannot_attach_a_private_course_to_their_account()
    {
        var owner = await Rider("src");
        var stranger = await Rider("dst");
        var course = await CreateCourse(owner, "Not yours", publish: false);
        var enabled = await stranger.Client.PostAsync($"/api/courses/{course.CourseId}/enable-download?login={stranger.Login}", null);
        Assert.True(enabled.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Unauthorized);
        var listed = await stranger.Client.GetFromJsonAsync<List<Course>>("/api/courses?login=" + stranger.Login);
        Assert.DoesNotContain(listed!, item => item.CourseId == course.CourseId);
    }

    [Fact]
    public async Task KnownBug_A_changed_password_with_a_trailing_space_can_still_sign_in()
    {
        var owner = await Rider("trim");
        var changed = await owner.Client.PostAsJsonAsync("/api/auth/password", new PasswordChangeRequest
        {
            CurrentPassword = owner.Password,
            NewPassword = "ridge-pass "
        });
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        using var fresh = _factory.CreateClient();
        var trimmed = await fresh.PostAsJsonAsync("/api/auth/login", new LoginRequest { Login = owner.Login, Password = "ridge-pass" });
        var exact = await fresh.PostAsJsonAsync("/api/auth/login", new LoginRequest { Login = owner.Login, Password = "ridge-pass " });
        Assert.True(trimmed.StatusCode == HttpStatusCode.OK || exact.StatusCode == HttpStatusCode.OK);
    }

    [Fact]
    public async Task KnownBug_The_same_email_with_different_letter_case_is_rejected()
    {
        var owner = await Rider("mail");
        var second = await _factory.CreateClient().PostAsJsonAsync("/api/users", new UserWriteDto
        {
            Login = owner.Login + "b",
            Password = "another-password",
            Email = (owner.Login + "@example.com").ToUpperInvariant()
        });
        Assert.Equal(SoapContract.DuplicateEmail, (await second.Content.ReadFromJsonAsync<InsertUserResponse>())!.Result);
    }

    [Fact]
    public async Task KnownBug_Sea_level_elevation_is_stored_as_zero_meters()
    {
        var owner = await Rider("sea");
        var course = await CreateCourse(owner, "Sea level", publish: false);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.PutAsJsonAsync($"/api/courses/{course.CourseId}/waypoints", new[]
        {
            new Waypoint { Latitude = 51.50, Longitude = 0.00, WaypointType = WaypointType.Start, TimestampTicks = 1, AltitudeKm = 0 },
            new Waypoint { Latitude = 51.51, Longitude = 0.01, WaypointType = WaypointType.Finish, TimestampTicks = 2, ElapsedDistanceKm = 1.2, AltitudeKm = 0.010 }
        })).StatusCode);

        var stored = await owner.Client.GetFromJsonAsync<List<Waypoint>>($"/api/courses/{course.CourseId}/waypoints");
        Assert.Equal(0, stored![0].AltitudeKm, 6);
        Assert.Equal(0.010, stored[1].AltitudeKm, 6);
        Assert.NotEqual(Altitude.Unknown.Kilometers, stored[0].AltitudeKm);
    }

    [Fact]
    public async Task KnownBug_Duplicate_waypoint_timestamps_are_rejected_instead_of_crashing()
    {
        var owner = await Rider("tick");
        var course = await CreateCourse(owner, "Ticks", publish: false);
        var response = await owner.Client.PutAsJsonAsync($"/api/courses/{course.CourseId}/waypoints", new[]
        {
            new Waypoint { Latitude = 40.0, Longitude = -105.0, WaypointType = WaypointType.Start, TimestampTicks = 5, AltitudeKm = 1.5 },
            new Waypoint { Latitude = 40.1, Longitude = -105.1, WaypointType = WaypointType.Finish, TimestampTicks = 5, ElapsedDistanceKm = 1, AltitudeKm = 1.6 }
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var colliding = await owner.Client.PutAsJsonAsync($"/api/courses/{course.CourseId}/waypoints", new[]
        {
            new Waypoint { Latitude = 40.0, Longitude = -105.0, WaypointType = WaypointType.Start, TimestampTicks = 0, AltitudeKm = 1.5 },
            new Waypoint { Latitude = 40.1, Longitude = -105.1, WaypointType = WaypointType.Finish, TimestampTicks = 1, ElapsedDistanceKm = 1, AltitudeKm = 1.6 }
        });
        Assert.NotEqual(HttpStatusCode.InternalServerError, colliding.StatusCode);
        Assert.True(colliding.IsSuccessStatusCode);
        var stored = await owner.Client.GetFromJsonAsync<List<Waypoint>>($"/api/courses/{course.CourseId}/waypoints");
        Assert.Equal(2, stored!.Select(point => point.TimestampTicks).Distinct().Count());
    }

    [Fact]
    public async Task KnownBug_Account_status_is_not_public()
    {
        var owner = await Rider("stat");
        using var stranger = _factory.CreateClient();
        var status = await stranger.GetAsync("/api/users/" + owner.Login + "/status");
        Assert.True(status.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
        var missing = await stranger.GetAsync("/api/users/nobody-coverage/status");
        Assert.True(missing.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task KnownBug_A_course_name_longer_than_128_characters_is_rejected()
    {
        var owner = await Rider("long");
        var response = await owner.Client.PostAsJsonAsync("/api/courses", new Course
        {
            Name = new string('N', 200),
            Login = owner.Login,
            Publish = false
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task KnownBug_A_login_longer_than_32_characters_is_rejected()
    {
        using var client = _factory.CreateClient();
        var signup = await client.PostAsJsonAsync("/api/users", new UserWriteDto
        {
            Login = new string('u', 40),
            Password = "long-password",
            Email = "long-login@example.com"
        });
        var body = await signup.Content.ReadFromJsonAsync<InsertUserResponse>();
        Assert.True(
            signup.StatusCode == HttpStatusCode.BadRequest || body?.Result == "MissingLogin",
            "status " + (int)signup.StatusCode + " result " + body?.Result);
    }

    [Fact]
    public async Task KnownBug_A_missing_timestamp_does_not_collide_with_tick_one()
    {
        var owner = await Rider("tick0");
        var course = await CreateCourse(owner, "Zero tick", publish: false);
        var colliding = await owner.Client.PutAsJsonAsync($"/api/courses/{course.CourseId}/waypoints", new[]
        {
            new Waypoint { Latitude = 40.0, Longitude = -105.0, WaypointType = WaypointType.Start, TimestampTicks = 0, AltitudeKm = 1.5 },
            new Waypoint { Latitude = 40.1, Longitude = -105.1, WaypointType = WaypointType.Finish, TimestampTicks = 1, ElapsedDistanceKm = 1, AltitudeKm = 1.6 }
        });
        Assert.NotEqual(HttpStatusCode.InternalServerError, colliding.StatusCode);
        Assert.True(colliding.IsSuccessStatusCode, "status " + (int)colliding.StatusCode);
        var stored = await owner.Client.GetFromJsonAsync<List<Waypoint>>($"/api/courses/{course.CourseId}/waypoints");
        Assert.Equal(2, stored!.Select(point => point.TimestampTicks).Distinct().Count());
    }

    private async Task<Course> CreateCourse(RiderSession owner, string name, bool publish)
    {
        var response = await owner.Client.PostAsJsonAsync("/api/courses", new Course
        {
            Name = name,
            Login = owner.Login,
            Publish = publish,
            ActivityCode = 3,
            Laps = 1
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var course = await response.Content.ReadFromJsonAsync<Course>();
        Assert.NotNull(course);
        Assert.NotEqual(Guid.Empty, course!.CourseId);
        return course;
    }

    private async Task<RiderSession> Rider(string prefix)
    {
        var client = _factory.CreateClient();
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var login = prefix + stamp;
        var password = "Pw-" + stamp + "-ok";
        var signup = await client.PostAsJsonAsync("/api/users", new UserWriteDto
        {
            Login = login,
            Password = password,
            Email = login + "@example.com",
            Country = "US",
            IsMale = true
        });
        Assert.Equal(SoapContract.InsertUserOk, (await signup.Content.ReadFromJsonAsync<InsertUserResponse>())!.Result);
        var signed = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Login = login, Password = password });
        Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
        var session = await signed.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.False(string.IsNullOrEmpty(session!.Token));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        return new RiderSession(client, login, password);
    }

    private sealed record RiderSession(HttpClient Client, string Login, string Password);
}
