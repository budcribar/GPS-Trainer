using System.Net;
using System.Net.Http.Json;
using GpsTrainer.Web.Pages;
using GpsTrainer.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GpsTrainer.Web.Tests;

public sealed class AccountTests : TestContext
{
    private readonly AccountApi _api = new();
    private readonly Session _session = new();

    public AccountTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_session);
        Services.AddSingleton(new HttpClient(_api) { BaseAddress = new Uri("http://localhost/") });
        Services.AddSingleton<GpsApi>();
    }

    [Fact]
    public void A_signed_out_visitor_is_asked_to_sign_in()
    {
        var cut = RenderComponent<Account>();

        Assert.Contains("gpstrainer-icon.svg", cut.Find(".account-mark").GetAttribute("src"));
        Assert.Equal("Account", cut.Find("h1").TextContent);
        Assert.Contains("Sign in", cut.Find("a[href='login']").TextContent);
        Assert.Contains("change your password or set your map location", cut.Markup);
        Assert.Empty(cut.FindAll("#profile-map"));
        Assert.Empty(cut.FindAll("#account-current"));
        Assert.DoesNotContain("Strava", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#E8B836", CardRule(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("border-top", CardRule(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_signed_in_page_shows_password_fields_and_the_home_map()
    {
        SignIn();
        var cut = RenderComponent<Account>();
        await cut.InvokeAsync(() => Task.CompletedTask);

        Assert.Contains("Signed in as bud.", cut.Find(".account-lead").TextContent);
        Assert.Equal("current-password", cut.Find("#account-current").GetAttribute("autocomplete"));
        Assert.Equal("password", cut.Find("#account-current").GetAttribute("type"));
        Assert.Equal("new-password", cut.Find("#account-next").GetAttribute("autocomplete"));
        Assert.Equal("Change password", cut.Find(".account-form .account-submit").TextContent.Trim());
        Assert.Equal("Map location", cut.Find("h2").TextContent);
        Assert.Empty(cut.FindAll(".account-coords"));
        Assert.DoesNotContain("40.50000", cut.Markup);
        Assert.DoesNotContain("-105.10000", cut.Markup);
        Assert.Equal("City, region", cut.Find("#account-address").GetAttribute("placeholder"));
        Assert.NotNull(cut.Find("section.account-location #profile-map"));
        Assert.Equal("Find address", cut.FindAll(".account-submit")[1].TextContent.Trim());
        Assert.Empty(cut.FindAll(".account-signout"));
        Assert.DoesNotContain("Sign out", cut.Markup);
        Assert.Contains("openrouteservice", cut.Markup);
        var map = MapRule();
        Assert.Contains("width: 100%", map, StringComparison.Ordinal);
        Assert.Contains("height: 520px", map, StringComparison.Ordinal);
    }

    [Fact]
    public void Show_reveals_the_current_password()
    {
        SignIn();
        var cut = RenderComponent<Account>();
        cut.Find("#account-current").Change("ride-map");

        var reveals = cut.FindAll(".account-reveal");
        Assert.Equal(2, reveals.Count);
        Assert.All(reveals, button =>
        {
            Assert.Equal("Show password", button.GetAttribute("aria-label"));
            Assert.Equal("Show password", button.GetAttribute("title"));
            Assert.Equal("false", button.GetAttribute("aria-pressed"));
            Assert.NotNull(button.QuerySelector("svg.password-eye"));
        });

        reveals[0].Click();

        Assert.Equal("text", cut.Find("#account-current").GetAttribute("type"));
        Assert.Equal("ride-map", cut.Find("#account-current").GetAttribute("value"));
        Assert.Equal("Hide password", cut.FindAll(".account-reveal")[0].GetAttribute("aria-label"));
        Assert.Equal("true", cut.FindAll(".account-reveal")[0].GetAttribute("aria-pressed"));
        Assert.Equal("password", cut.Find("#account-next").GetAttribute("type"));
        Assert.Equal("Show password", cut.FindAll(".account-reveal")[1].GetAttribute("aria-label"));
    }

    [Fact]
    public void A_rejected_password_stays_on_the_field_and_still_calls_the_endpoint()
    {
        SignIn();
        _api.AcceptPassword = false;
        var cut = RenderComponent<Account>();
        cut.Find("#account-current").Change("old");
        cut.Find("#account-next").Change("new");

        cut.Find(".account-form .account-submit").Click();

        var posted = Assert.Single(_api.Passwords);
        Assert.Equal("old", posted.CurrentPassword);
        Assert.Equal("new", posted.NewPassword);
        Assert.Equal("The current password was not accepted.", cut.Find("#account-password-note").TextContent);
        Assert.Equal("true", cut.Find("#account-current").GetAttribute("aria-invalid"));
    }

    [Fact]
    public void An_autofill_with_no_field_event_changes_the_password()
    {
        SignIn();
        JSInterop.Setup<Dictionary<string, string>>("gpsTrainerFields.read", _ => true)
            .SetResult(new Dictionary<string, string>
            {
                ["account-current"] = "old-secret",
                ["account-next"] = "new-secret"
            });
        var cut = RenderComponent<Account>();

        cut.Find(".account-form .account-submit").Click();

        var posted = Assert.Single(_api.Passwords);
        Assert.Equal("old-secret", posted.CurrentPassword);
        Assert.Equal("new-secret", posted.NewPassword);
        Assert.Equal("Password updated.", cut.Find("#account-password-note").TextContent);
    }

    [Fact]
    public void An_accepted_password_reports_the_update()
    {
        SignIn();
        var cut = RenderComponent<Account>();

        cut.Find(".account-form .account-submit").Click();

        Assert.Single(_api.Passwords);
        Assert.Equal("Password updated.", cut.Find("#account-password-note").TextContent);
        Assert.Null(cut.Find("#account-current").GetAttribute("aria-invalid"));
    }

    [Fact]
    public async Task Clicking_the_map_saves_the_home_location()
    {
        SignIn();
        var cut = RenderComponent<Account>();

        await cut.InvokeAsync(() => cut.Instance.SetHomeLocation(40.01, -105.25));

        var saved = Assert.Single(_api.Locations);
        Assert.Equal(40.01, saved.Latitude, 5);
        Assert.Equal(-105.25, saved.Longitude, 5);
        Assert.Empty(cut.FindAll(".account-coords"));
        Assert.DoesNotContain("40.01000", cut.Markup);
        Assert.Equal("Map location saved.", cut.Find("#account-location-note").TextContent);
    }

    [Fact]
    public async Task A_failed_home_location_save_is_shown_on_the_map_card()
    {
        SignIn();
        _api.SaveLocation = false;
        var cut = RenderComponent<Account>();

        await cut.InvokeAsync(() => cut.Instance.SetHomeLocation(40.01, -105.25));

        Assert.Equal("The location was not saved.", cut.Find("#account-location-note").TextContent);
        Assert.Equal("true", cut.Find("#account-address").GetAttribute("aria-invalid"));
    }

    [Fact]
    public void A_missed_address_does_not_move_the_saved_point()
    {
        SignIn();
        _api.Geocode = new GeocodeResponseDto { Found = false, Message = "No location found." };
        var cut = RenderComponent<Account>();
        cut.Find("#account-address").Change("nowhere");

        cut.FindAll(".account-submit").Last().Click();

        Assert.Empty(_api.Locations);
        Assert.Equal("No location found.", cut.Find("#account-location-note").TextContent);
        Assert.Empty(cut.FindAll(".account-coords"));
        Assert.DoesNotContain("40.50000", cut.Markup);
    }

    [Fact]
    public void A_found_address_saves_that_point()
    {
        SignIn();
        _api.Geocode = new GeocodeResponseDto
        {
            Found = true,
            Latitude = 40.5853,
            Longitude = -105.0844,
            Label = "Fort Collins"
        };
        var cut = RenderComponent<Account>();
        cut.Find("#account-address").Change("Fort Collins");

        cut.FindAll(".account-submit").Last().Click();

        var saved = Assert.Single(_api.Locations);
        Assert.Equal(40.5853, saved.Latitude, 5);
        Assert.Equal(-105.0844, saved.Longitude, 5);
        Assert.Equal("Map location saved: Fort Collins", cut.Find("#account-location-note").TextContent);
        Assert.Empty(cut.FindAll(".account-coords"));
        Assert.DoesNotContain("40.58530", cut.Markup);
    }

    [Fact]
    public void The_account_page_has_no_sign_out_button()
    {
        SignIn();
        var cut = RenderComponent<Account>();

        Assert.Empty(cut.FindAll(".account-signout"));
        Assert.DoesNotContain("Sign out", cut.Markup);
        Assert.Equal(0, _api.Logouts);
        Assert.Equal("bud", _session.Login);
    }

    [Fact]
    public void Delete_account_confirms_with_the_password_or_DELETE_and_signs_out()
    {
        SignIn();
        var cut = RenderComponent<Account>();

        Assert.Equal("Delete account", cut.Find("section.account-delete h2").TextContent);
        Assert.Empty(cut.FindAll("#account-delete-confirm"));
        var button = CardRule(".account-delete-open, .account-delete-submit {");
        Assert.Contains("#9b1c31", button, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#E8B836", button, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("border-top", CardRule(".account.account-delete {"), StringComparison.OrdinalIgnoreCase);

        cut.Find(".account-delete-open").Click();
        _api.AcceptDeletion = false;
        cut.Find("#account-delete-confirm").Change("nope");
        cut.Find(".account-delete-submit").Click();

        Assert.Equal("bud", _session.Login);
        Assert.Equal(0, _api.Logouts);
        Assert.Contains("was not deleted", cut.Find("#account-delete-note").TextContent);
        Assert.Equal("nope", _api.Deletions.Single().Confirmation);

        _api.AcceptDeletion = true;
        cut.Find("#account-delete-confirm").Change("DELETE");
        cut.Find(".account-delete-submit").Click();

        Assert.Null(_session.Login);
        Assert.Equal(1, _api.Logouts);
        Assert.Equal("DELETE", _api.Deletions.Last().Confirmation);
    }

    private void SignIn() =>
        _session.Set(new LoginResponse { Token = "token", User = new UserProfileDto { Login = "bud" } });

    private static string MapRule()
    {
        var css = File.ReadAllText(Path.Combine(RepoRoot(), "src", "GpsTrainer.Web", "wwwroot", "css", "app.css"));
        var start = css.IndexOf(".account #profile-map {", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = css.IndexOf('}', start);
        return css[start..end];
    }

    private static string CardRule() => CardRule(".signup, .signin, .account {");

    private static string CardRule(string selector)
    {
        var css = File.ReadAllText(Path.Combine(RepoRoot(), "src", "GpsTrainer.Web", "wwwroot", "css", "app.css"));
        var start = css.IndexOf(selector, StringComparison.Ordinal);
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

    private sealed class AccountApi : HttpMessageHandler
    {
        public UserProfileDto Profile { get; set; } = new() { Login = "bud", Latitude = 40.5, Longitude = -105.1 };
        public GeocodeResponseDto Geocode { get; set; } = new() { Found = false, Message = "No location found." };
        public List<PasswordChangeRequest> Passwords { get; } = [];
        public List<UserLocationDto> Locations { get; } = [];
        public int Logouts { get; private set; }
        public bool AcceptPassword { get; set; } = true;
        public bool AcceptDeletion { get; set; } = true;
        public bool SaveLocation { get; set; } = true;
        public List<AccountDeletionRequest> Deletions { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.Contains("/location", StringComparison.Ordinal))
            {
                Locations.Add(await request.Content!.ReadFromJsonAsync<UserLocationDto>(cancellationToken) ?? new UserLocationDto());
                if (!SaveLocation)
                    return new HttpResponseMessage(HttpStatusCode.BadRequest);
                var saved = Locations[^1];
                return Json(new UserProfileDto { Login = "bud", Latitude = saved.Latitude, Longitude = saved.Longitude });
            }
            if (path.Contains("password", StringComparison.Ordinal))
            {
                Passwords.Add(await request.Content!.ReadFromJsonAsync<PasswordChangeRequest>(cancellationToken) ?? new PasswordChangeRequest());
                return new HttpResponseMessage(AcceptPassword ? HttpStatusCode.OK : HttpStatusCode.Unauthorized);
            }
            if (path.Contains("geocode", StringComparison.Ordinal))
                return Json(Geocode);
            if (request.Method == HttpMethod.Delete && path.Contains("/users/", StringComparison.Ordinal))
            {
                Deletions.Add(await request.Content!.ReadFromJsonAsync<AccountDeletionRequest>(cancellationToken) ?? new AccountDeletionRequest());
                return new HttpResponseMessage(AcceptDeletion ? HttpStatusCode.NoContent : HttpStatusCode.BadRequest);
            }
            if (path.Contains("logout", StringComparison.Ordinal))
            {
                Logouts++;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (path.Contains("/users/", StringComparison.Ordinal))
                return Json(Profile);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json<T>(T body) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
    }
}
