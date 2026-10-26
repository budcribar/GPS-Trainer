using System.Net;
using GpsTrainer;
using GpsTrainer.Web.Layout;
using GpsTrainer.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace GpsTrainer.Web.Tests;

public sealed class HeaderTests : TestContext
{
    private readonly LogoutHandler _api = new();
    private readonly Session _session = new();

    public HeaderTests()
    {
        Services.AddSingleton(_session);
        Services.AddSingleton(new HttpClient(_api) { BaseAddress = new Uri("http://localhost/") });
        Services.AddSingleton<GpsApi>();
    }

    [Fact]
    public void A_signed_in_header_shows_the_user_and_sign_out()
    {
        SignIn();

        var cut = RenderComponent<MainLayout>();
        var user = cut.Find("header a.header-user");

        Assert.Equal("bud", user.TextContent);
        Assert.Equal("account", user.GetAttribute("href"));
        Assert.Equal("Sign out", cut.Find("header button.header-signout").TextContent.Trim());
        Assert.Empty(cut.FindAll("header a[href='login']"));
    }

    [Fact]
    public void The_editor_header_shows_sign_out_too()
    {
        SignIn();
        Services.GetRequiredService<NavigationManager>().NavigateTo("editor");

        var cut = RenderComponent<MainLayout>();

        Assert.Empty(cut.FindAll("header .site-brand"));
        Assert.Equal("Sign out", cut.Find("header button.header-signout").TextContent.Trim());
        Assert.Equal("account", cut.Find("header a.header-user").GetAttribute("href"));
    }

    [Fact]
    public void Clicking_sign_out_clears_the_session_and_returns_home()
    {
        SignIn();
        var cut = RenderComponent<MainLayout>();

        cut.Find("header button.header-signout").Click();

        Assert.Equal(1, _api.Logouts);
        Assert.Null(_session.Login);
        Assert.Null(_session.Token);
        Assert.Empty(cut.FindAll("header button.header-signout"));
        Assert.Empty(cut.FindAll("header a.header-user"));
        Assert.Contains("Sign in", cut.Find("header a[href='login']").TextContent);
        Assert.Equal("http://localhost/", Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public void A_signed_out_header_has_no_sign_out_control()
    {
        var cut = RenderComponent<MainLayout>();

        Assert.Empty(cut.FindAll("header button.header-signout"));
        Assert.Contains("Sign in", cut.Find("header a[href='login']").TextContent);
    }

    private void SignIn() =>
        _session.Set(new LoginResponse { Token = "token", User = new UserProfileDto { Login = "bud" } });

    private sealed class LogoutHandler : HttpMessageHandler
    {
        public int Logouts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath.Contains("logout", StringComparison.OrdinalIgnoreCase) == true)
                Logouts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }
}
