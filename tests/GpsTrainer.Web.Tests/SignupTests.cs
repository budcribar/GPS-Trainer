using System.Net;
using System.Net.Http.Json;
using GpsTrainer.Web.Pages;
using GpsTrainer.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace GpsTrainer.Web.Tests;

public sealed class SignupTests : TestContext
{
    private readonly SignupApi _api = new();

    public SignupTests()
    {
        Services.AddSingleton(new Session());
        Services.AddSingleton(new HttpClient(_api) { BaseAddress = new Uri("http://localhost/") });
        Services.AddSingleton<GpsApi>();
    }

    [Fact]
    public void The_form_names_every_field_and_links_to_sign_in()
    {
        var cut = RenderComponent<Signup>();

        Assert.Contains("gpstrainer-icon.svg", cut.Find(".signup-mark").GetAttribute("src"));
        Assert.NotNull(cut.Find("form").GetAttribute("novalidate"));
        Assert.Equal("Create your account", cut.Find("h1").TextContent);
        Assert.Equal("username", cut.Find("#signup-login").GetAttribute("autocomplete"));
        Assert.Equal("password", cut.Find("#signup-password").GetAttribute("type"));
        Assert.Equal("new-password", cut.Find("#signup-password").GetAttribute("autocomplete"));
        Assert.Equal("Show password", cut.Find(".signup-reveal").GetAttribute("aria-label"));
        Assert.Equal("Show password", cut.Find(".signup-reveal").GetAttribute("title"));
        Assert.Equal("false", cut.Find(".signup-reveal").GetAttribute("aria-pressed"));
        Assert.NotNull(cut.Find(".signup-reveal svg.password-eye"));
        Assert.Equal("email", cut.Find("#signup-email").GetAttribute("type"));
        Assert.Equal("email", cut.Find("#signup-email").GetAttribute("autocomplete"));
        Assert.Equal("given-name", cut.Find("#signup-first").GetAttribute("autocomplete"));
        Assert.Equal("family-name", cut.Find("#signup-last").GetAttribute("autocomplete"));
        Assert.Equal("tel", cut.Find("#signup-phone").GetAttribute("type"));
        Assert.Equal("tel", cut.Find("#signup-phone").GetAttribute("autocomplete"));
        Assert.Equal("street-address", cut.Find("#signup-street").GetAttribute("autocomplete"));
        Assert.Equal("address-level2", cut.Find("#signup-city").GetAttribute("autocomplete"));
        Assert.Equal("address-level1", cut.Find("#signup-state").GetAttribute("autocomplete"));
        Assert.Equal("country", cut.Find("#signup-country").GetAttribute("autocomplete"));
        Assert.Equal("US", cut.Find("#signup-country").GetAttribute("value"));
        Assert.NotNull(cut.Find("#signup-male").GetAttribute("checked"));
        Assert.NotNull(cut.Find("#signup-mail").GetAttribute("checked"));
        Assert.Equal("Create account", cut.Find(".signup-submit").TextContent.Trim());
        Assert.Contains("login", cut.Find(".signup-switch a").GetAttribute("href"));
        Assert.Equal("Sign in", cut.Find(".signup-switch a").TextContent.Trim());
        Assert.DoesNotContain("Strava", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Show_reveals_the_password_and_hide_covers_it_again()
    {
        var cut = RenderComponent<Signup>();
        cut.Find("#signup-password").Change("ride-map");

        cut.Find(".signup-reveal").Click();

        Assert.Equal("text", cut.Find("#signup-password").GetAttribute("type"));
        Assert.Equal("ride-map", cut.Find("#signup-password").GetAttribute("value"));
        Assert.Equal("Hide password", cut.Find(".signup-reveal").GetAttribute("aria-label"));
        Assert.Equal("true", cut.Find(".signup-reveal").GetAttribute("aria-pressed"));

        cut.Find(".signup-reveal").Click();

        Assert.Equal("password", cut.Find("#signup-password").GetAttribute("type"));
        Assert.Equal("ride-map", cut.Find("#signup-password").GetAttribute("value"));
        Assert.Equal("Show password", cut.Find(".signup-reveal").GetAttribute("aria-label"));
        Assert.Equal("false", cut.Find(".signup-reveal").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void An_invalid_email_is_shown_on_the_field_and_is_not_sent()
    {
        var cut = RenderComponent<Signup>();
        cut.Find("#signup-login").Change("rider");
        cut.Find("#signup-password").Change("ride-map");
        cut.Find("#signup-email").Change("not-an-email");

        cut.Find(".signup-submit").Click();

        Assert.Empty(_api.Posts);
        Assert.Equal("Enter a valid email address.", cut.Find("#signup-email-error").TextContent);
        Assert.Equal("true", cut.Find("#signup-email").GetAttribute("aria-invalid"));
    }

    [Fact]
    public void A_duplicate_name_stays_on_the_user_name_field()
    {
        _api.Result = "DuplicateUserName";
        var cut = RenderComponent<Signup>();
        Fill(cut);

        cut.Find(".signup-submit").Click();

        Assert.Single(_api.Posts);
        Assert.Equal("That user name is already registered.", cut.Find("#signup-login-error").TextContent);
        Assert.Empty(cut.FindAll("#signup-email-error"));
        Assert.DoesNotContain("/login", Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public void An_autofill_with_no_field_event_creates_the_account()
    {
        JSInterop.Setup<Dictionary<string, string>>("gpsTrainerFields.read", _ => true)
            .SetResult(new Dictionary<string, string>
            {
                ["signup-login"] = "rider",
                ["signup-password"] = "ride-map",
                ["signup-email"] = "rider@example.com"
            });
        var cut = RenderComponent<Signup>();

        cut.Find(".signup-submit").Click();

        var posted = Assert.Single(_api.Posts);
        Assert.Equal("rider", posted.Login);
        Assert.Equal("ride-map", posted.Password);
        Assert.Equal("rider@example.com", posted.Email);
        Assert.Equal("US", posted.Country);
    }

    [Fact]
    public void A_new_account_goes_to_sign_in()
    {
        var cut = RenderComponent<Signup>();
        Fill(cut);

        cut.Find(".signup-submit").Click();

        var posted = Assert.Single(_api.Posts);
        Assert.Equal("rider", posted.Login);
        Assert.Equal("ride-map", posted.Password);
        Assert.Equal("rider@example.com", posted.Email);
        Assert.Equal("US", posted.Country);
        Assert.True(posted.IsMale);
        Assert.True(posted.WantsEmails);
        Assert.EndsWith("/login", Services.GetRequiredService<NavigationManager>().Uri);
    }

    private static void Fill(IRenderedComponent<Signup> cut)
    {
        cut.Find("#signup-login").Change("rider");
        cut.Find("#signup-password").Change("ride-map");
        cut.Find("#signup-email").Change("rider@example.com");
    }

    private sealed class SignupApi : HttpMessageHandler
    {
        public List<UserWriteDto> Posts { get; } = [];
        public string Result { get; set; } = "ok";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadFromJsonAsync<UserWriteDto>(cancellationToken) ?? new UserWriteDto();
            Posts.Add(body);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new InsertUserResponse { Result = Result })
            };
        }
    }
}
