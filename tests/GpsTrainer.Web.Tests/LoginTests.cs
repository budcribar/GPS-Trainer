using System.Net;
using System.Net.Http.Json;
using GpsTrainer.Web.Pages;
using GpsTrainer.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace GpsTrainer.Web.Tests;

public sealed class LoginTests : TestContext
{
    private readonly LoginApi _api = new();
    private readonly Session _session = new();

    public LoginTests()
    {
        Services.AddSingleton(_session);
        Services.AddSingleton(new HttpClient(_api) { BaseAddress = new Uri("http://localhost/") });
        Services.AddSingleton<GpsApi>();
    }

    [Fact]
    public void The_card_matches_sign_up_and_keeps_forgot_password()
    {
        var cut = RenderComponent<Login>();

        Assert.Contains("gpstrainer-icon.svg", cut.Find(".signin-mark").GetAttribute("src"));
        Assert.Equal("Sign in", cut.Find("h1").TextContent);
        Assert.NotNull(cut.Find("form").GetAttribute("novalidate"));
        Assert.Equal("username", cut.Find("#signin-login").GetAttribute("autocomplete"));
        Assert.Equal("password", cut.Find("#signin-password").GetAttribute("type"));
        Assert.Equal("current-password", cut.Find("#signin-password").GetAttribute("autocomplete"));
        AssertReveal(cut.Find(".signin-reveal"), shown: false);
        Assert.Equal("Sign in", cut.Find(".signin-submit").TextContent.Trim());
        Assert.Equal("Forgot password", cut.Find("a[href='forgot']").TextContent.Trim());
        Assert.Equal("Sign up", cut.Find("a[href='signup']").TextContent.Trim());
        Assert.DoesNotContain("Strava", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#E8B836", CardRule(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("border-top", CardRule(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Show_reveals_the_password_and_hide_covers_it_again()
    {
        var cut = RenderComponent<Login>();
        cut.Find("#signin-password").Change("ride-map");

        cut.Find(".signin-reveal").Click();

        Assert.Equal("text", cut.Find("#signin-password").GetAttribute("type"));
        Assert.Equal("ride-map", cut.Find("#signin-password").GetAttribute("value"));
        AssertReveal(cut.Find(".signin-reveal"), shown: true);

        cut.Find(".signin-reveal").Click();

        Assert.Equal("password", cut.Find("#signin-password").GetAttribute("type"));
        Assert.Equal("ride-map", cut.Find("#signin-password").GetAttribute("value"));
        AssertReveal(cut.Find(".signin-reveal"), shown: false);
    }

    [Fact]
    public void Empty_fields_are_shown_inline_and_are_not_sent()
    {
        var cut = RenderComponent<Login>();

        cut.Find(".signin-submit").Click();

        Assert.Empty(_api.Logins);
        Assert.Equal("Enter a user name.", cut.Find("#signin-login-error").TextContent);
        Assert.Equal("Enter a password.", cut.Find("#signin-password-error").TextContent);
        Assert.Equal("true", cut.Find("#signin-login").GetAttribute("aria-invalid"));
        Assert.Equal("true", cut.Find("#signin-password").GetAttribute("aria-invalid"));
    }

    [Fact]
    public void A_rejected_sign_in_stays_on_the_page()
    {
        _api.Accept = false;
        var cut = RenderComponent<Login>();
        cut.Find("#signin-login").Change("bud");
        cut.Find("#signin-password").Change("wrong");

        cut.Find(".signin-submit").Click();

        var posted = Assert.Single(_api.Logins);
        Assert.Equal("bud", posted.Login);
        Assert.Equal("wrong", posted.Password);
        Assert.Equal("The user name or password is incorrect.", cut.Find("#signin-form-error").TextContent);
        Assert.Equal("true", cut.Find("#signin-login").GetAttribute("aria-invalid"));
        Assert.Equal("true", cut.Find("#signin-password").GetAttribute("aria-invalid"));
        Assert.DoesNotContain("/courses", Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public void Typing_reaches_the_model_on_input_and_on_change()
    {
        var cut = RenderComponent<Login>();
        cut.Find("#signin-login").Input("bud");
        cut.Find("#signin-password").Change("ride-map");

        cut.Find(".signin-submit").Click();

        var posted = Assert.Single(_api.Logins);
        Assert.Equal("bud", posted.Login);
        Assert.Equal("ride-map", posted.Password);
    }

    [Fact]
    public void An_autofill_with_no_field_event_is_still_submitted()
    {
        JSInterop.Setup<Dictionary<string, string>>("gpsTrainerFields.read", _ => true)
            .SetResult(new Dictionary<string, string>
            {
                ["signin-login"] = "bud",
                ["signin-password"] = "ride-map"
            });
        var cut = RenderComponent<Login>();

        cut.Find(".signin-submit").Click();

        var posted = Assert.Single(_api.Logins);
        Assert.Equal("bud", posted.Login);
        Assert.Equal("ride-map", posted.Password);
        Assert.EndsWith("/courses", Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public void A_correct_sign_in_opens_courses()
    {
        var cut = RenderComponent<Login>();
        cut.Find("#signin-login").Change("bud");
        cut.Find("#signin-password").Change("ride-map");

        cut.Find(".signin-submit").Click();

        Assert.Single(_api.Logins);
        Assert.Equal("bud", _session.Login);
        Assert.EndsWith("/courses", Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public void Sign_out_uses_the_existing_session_message()
    {
        _session.Set(new LoginResponse { Token = "token", User = new UserProfileDto { Login = "bud" } });
        var cut = RenderComponent<Login>();
        Assert.Contains("Signed in as bud", cut.Find(".signin-signed").TextContent);

        cut.Find(".signin-signed button").Click();

        Assert.Equal(1, _api.Logouts);
        Assert.Null(_session.Login);
        Assert.Equal("Signed out.", cut.Find("#signin-form-error").TextContent);
    }

    private static string CardRule()
    {
        var css = File.ReadAllText(Path.Combine(RepoRoot(), "src", "GpsTrainer.Web", "wwwroot", "css", "app.css"));
        var start = css.IndexOf(".signup, .signin, .account {", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = css.IndexOf('}', start);
        return css[start..end];
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GPS-Trainer.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static void AssertReveal(AngleSharp.Dom.IElement button, bool shown)
    {
        Assert.Equal(shown ? "Hide password" : "Show password", button.GetAttribute("aria-label"));
        Assert.Equal(shown ? "Hide password" : "Show password", button.GetAttribute("title"));
        Assert.Equal(shown ? "true" : "false", button.GetAttribute("aria-pressed"));
        Assert.Equal("button", button.GetAttribute("type"));
        Assert.NotNull(button.QuerySelector("svg.password-eye"));
        Assert.Equal("true", button.QuerySelector("svg")?.GetAttribute("aria-hidden"));
        Assert.Empty(button.TextContent.Trim());
    }

    private sealed class LoginApi : HttpMessageHandler
    {
        public List<LoginRequest> Logins { get; } = [];
        public int Logouts { get; private set; }
        public bool Accept { get; set; } = true;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.Contains("logout", StringComparison.Ordinal))
            {
                Logouts++;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            var body = await request.Content!.ReadFromJsonAsync<LoginRequest>(cancellationToken) ?? new LoginRequest();
            Logins.Add(body);
            if (!Accept)
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new LoginResponse
                {
                    Token = "token",
                    User = new UserProfileDto { Login = body.Login }
                })
            };
        }
    }
}
