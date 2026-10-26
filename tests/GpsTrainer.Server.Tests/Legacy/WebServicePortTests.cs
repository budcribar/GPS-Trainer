using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace GpsTrainer.Server.Tests.Legacy;

public class WebServicePortTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;

    public WebServicePortTests(ApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task TestRaces_round_trip_course_and_race()
    {
        await SignUp("race-owner");
        var token = await Token("race-owner");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var course = await (await _client.PostAsJsonAsync("/api/courses", new Course { Name = "Loop", Login = "race-owner", ElapsedDistanceKm = 5 })).Content.ReadFromJsonAsync<Course>();
        var race = await (await _client.PostAsJsonAsync("/api/races", new Race
        {
            CourseId = course!.CourseId,
            Login = "race-owner",
            StartTime = new DateTime(2013, 10, 13, 0, 0, 0, DateTimeKind.Utc),
            FinishTime = new DateTime(2013, 10, 13, 1, 0, 0, DateTimeKind.Utc),
            Publish = true
        })).Content.ReadFromJsonAsync<Race>();
        var loadedRace = await _client.GetFromJsonAsync<Race>($"/api/races/{race!.RaceId}");
        var loadedCourse = await _client.GetFromJsonAsync<Course>($"/api/courses/{course.CourseId}");
        Assert.NotNull(loadedRace);
        Assert.Equal("Loop", loadedCourse!.Name);
        Assert.True(loadedRace!.StartTime >= new DateTime(100, 1, 1));
        var list = await _client.GetFromJsonAsync<List<Race>>("/api/races?login=race-owner");
        Assert.Contains(list!, r => r.RaceId == race.RaceId);
    }

    [Fact]
    public async Task TestInsertUser_round_trip_uses_fake_contact_data()
    {
        var birth = new DateTime(1980, 4, 2, 0, 0, 0, DateTimeKind.Utc);
        var expires = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var signup = await _client.PostAsJsonAsync("/api/users", new UserWriteDto
        {
            Login = "gtest6",
            FirstName = "First",
            LastName = "Last",
            IsMale = true,
            Country = "US",
            Password = "gps",
            PhoneNumber = "555-0100",
            RacingCategoryKey = 0,
            State = "CO",
            Street = "100 Test Lane",
            City = "Fort Collins",
            WantsEmails = true,
            Email = "gtest6@example.com",
            BirthDate = birth,
            ExpirationDate = expires
        });
        Assert.Equal("ok", (await signup.Content.ReadFromJsonAsync<InsertUserResponse>())!.Result);

        var token = await Token("gtest6", "gps");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var json = await _client.GetStringAsync("/api/users/gtest6");
        Assert.DoesNotContain("gps", json, StringComparison.Ordinal);
        var user = JsonSerializer.Deserialize<UserProfileDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.Equal("gtest6", user!.Login);
        Assert.Equal("First", user.FirstName);
        Assert.Equal("Last", user.LastName);
        Assert.True(user.IsActive);
        Assert.True(user.IsMale);
        Assert.Equal("US", user.Country);
        Assert.Equal("555-0100", user.PhoneNumber);
        Assert.Equal(0, user.RacingCategoryKey);
        Assert.Equal("CO", user.State);
        Assert.Equal("100 Test Lane", user.Street);
        Assert.Equal("Fort Collins", user.City);
        Assert.True(user.WantsEmails);
        Assert.Equal("gtest6@example.com", user.Email);
        Assert.Equal(birth, user.BirthDate);
        Assert.Equal(expires, user.ExpirationDate);

        var deleted = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/users/gtest6")
        {
            Content = JsonContent.Create(new AccountDeletionRequest { Confirmation = "DELETE" })
        });
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/users/gtest6")).StatusCode);
    }

    [Fact]
    public async Task TestGenerateRace_rejects_bad_arguments_and_creates_a_row()
    {
        await SignUp("gen");
        var token = await Token("gen");
        var missing = await _client.PostAsJsonAsync("/api/races/generate", new GenerateRaceRequest { Login = "", CourseId = Guid.NewGuid(), GoalTime = "00:10:00" });
        Assert.Equal(Guid.Empty, await missing.Content.ReadFromJsonAsync<Guid>());
        var badGoal = await _client.PostAsJsonAsync("/api/races/generate", new GenerateRaceRequest { Login = "gen", CourseId = Guid.NewGuid(), GoalTime = "goal" });
        Assert.Equal(Guid.Empty, await badGoal.Content.ReadFromJsonAsync<Guid>());

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var unknown = await _client.PostAsJsonAsync("/api/races/generate", new GenerateRaceRequest { Login = "gen", CourseId = Guid.NewGuid(), GoalTime = "00:10:00" });
        Assert.Equal(Guid.Empty, await unknown.Content.ReadFromJsonAsync<Guid>());
        var course = await (await _client.PostAsJsonAsync("/api/courses", new Course { Name = "Gen", Login = "gen" })).Content.ReadFromJsonAsync<Course>();
        var created = await _client.PostAsJsonAsync("/api/races/generate", new GenerateRaceRequest { Login = "gen", CourseId = course!.CourseId, GoalTime = "00:10:00" });
        var id = await created.Content.ReadFromJsonAsync<Guid>();
        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact(Skip = "WebApi.Tests ValuesController is the ASP.NET Web API project template. This server has no ValuesController.")]
    public void ValuesController_template()
    {
    }

    [Fact(Skip = "NotifyMobileDevice posted to Google GCM with a redacted server key. Push is not sent.")]
    public void TestNotify()
    {
    }

    [Fact(Skip = "TestBingMap, TestGoogleMap, and TestMap drive Bing and Google map widgets. The Blazor editor uses Leaflet and those keys were redacted.")]
    public void Bing_and_Google_map_widgets()
    {
    }

    [Fact(Skip = "TestGeoLocation, TestRouting, and TestWebApi call the old ASMX host asynchronously. They are not run against a live gpsTrainer.com.")]
    public void Live_asmx_qunit_calls()
    {
    }

    [Fact(Skip = "TestCourseEditor drives the old CourseEditor page (save, delete, profile) through the browser. The Blazor editor is covered by EditorCourse unit tests.")]
    public void CourseEditor_page_harness()
    {
    }

    private async Task SignUp(string login)
    {
        var response = await _client.PostAsJsonAsync("/api/users", new UserWriteDto
        {
            Login = login,
            Password = "secret",
            Email = login + "@example.com"
        });
        response.EnsureSuccessStatusCode();
    }

    private async Task<string> Token(string login, string password = "secret")
    {
        var session = await (await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Login = login, Password = password })).Content.ReadFromJsonAsync<LoginResponse>();
        return session!.Token;
    }
}
