using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace GpsTrainer.Server.Tests;

public class AuthLogoutTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public AuthLogoutTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Sign_out_rejects_the_same_token_and_leaves_a_new_login_working()
    {
        using var client = _factory.CreateClient();
        var signup = await client.PostAsJsonAsync("/api/users", new UserWriteDto
        {
            Login = "session",
            Password = "secret",
            Email = "session@example.com",
            FirstName = "Ses",
            LastName = "Sion"
        });
        Assert.Equal("ok", (await signup.Content.ReadFromJsonAsync<InsertUserResponse>())!.Result);

        var first = await Login(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.Token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users/session")).StatusCode);

        var logout = await client.PostAsync("/api/auth/logout", new StringContent(""));
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users/session")).StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        var second = await Login(client);
        Assert.NotEqual(first.Token, second.Token);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", second.Token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users/session")).StatusCode);
    }

    [Fact]
    public async Task Logout_without_a_token_is_rejected()
    {
        using var client = _factory.CreateClient();
        var logout = await client.PostAsync("/api/auth/logout", new StringContent(""));
        Assert.Equal(HttpStatusCode.Unauthorized, logout.StatusCode);
    }

    private static async Task<LoginResponse> Login(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Login = "session", Password = "secret" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }
}
