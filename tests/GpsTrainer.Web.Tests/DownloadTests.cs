using System.Net;
using System.Text;
using System.Text.Json;
using GpsTrainer.Web.Pages;
using GpsTrainer.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GpsTrainer.Web.Tests;

public sealed class DownloadTests : TestContext
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "   ")]
    [InlineData("javascript:alert(1)", "not-a-url")]
    public void Blank_store_settings_render_muted_coming_soon_badges(string? google, string? apple)
    {
        UseSite(google, apple);
        var cut = RenderComponent<Download>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".store-badge-soon").Count));

        Assert.Contains("Use the gpsTrainer phone app with the same account as this site.", cut.Markup);
        Assert.Contains("Sign in, then enable a course for download. The phone picks it up the next time it synchronizes.", cut.Markup);
        Assert.Equal("Your courses", cut.Find("a[href='courses']").TextContent);
        Assert.Empty(cut.FindAll("a.store-badge"));
        Assert.DoesNotContain("play.google.com", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apps.apple.com", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EXCLUDED", cut.Markup);
        Assert.DoesNotContain("Windows Mobile", cut.Markup);
        Assert.DoesNotContain("MAUI", cut.Markup);
        Assert.DoesNotContain("recovered", cut.Markup);
        var badges = cut.FindAll(".store-badge-soon");
        Assert.Equal("true", badges[0].GetAttribute("aria-disabled"));
        Assert.Contains("Coming soon", badges[0].TextContent);
        Assert.Contains("Get it on Google Play", badges[0].TextContent);
        Assert.Null(badges[0].GetAttribute("href"));
        Assert.Equal("true", badges[1].GetAttribute("aria-disabled"));
        Assert.Contains("Coming soon", badges[1].TextContent);
        Assert.Contains("Download on the App Store", badges[1].TextContent);
        Assert.Null(badges[1].GetAttribute("href"));

        var card = CardRule(".download {");
        Assert.Contains("background: #fff", card, StringComparison.Ordinal);
        Assert.DoesNotContain("#E8B836", card, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("border-top", card, StringComparison.OrdinalIgnoreCase);
        var page = File.ReadAllText(Path.Combine(RepoRoot(), "src", "GpsTrainer.Web", "Pages", "Download.razor"));
        Assert.DoesNotContain("play.google.com", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apps.apple.com", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Configured_store_urls_open_in_a_new_tab()
    {
        const string play = "https://example.com/play";
        const string apple = "https://example.com/ios";
        UseSite("  " + play + "  ", apple);
        var cut = RenderComponent<Download>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("a.store-badge").Count));

        var links = cut.FindAll("a.store-badge");
        Assert.Equal(play, links[0].GetAttribute("href"));
        Assert.Contains("Get it on Google Play", links[0].TextContent);
        Assert.Equal(apple, links[1].GetAttribute("href"));
        Assert.Contains("Download on the App Store", links[1].TextContent);
        foreach (var link in links)
        {
            Assert.Equal("_blank", link.GetAttribute("target"));
            Assert.Contains("noopener", link.GetAttribute("rel"));
            Assert.DoesNotContain("Coming soon", link.TextContent);
        }
        Assert.Empty(cut.FindAll(".store-badge-soon"));
    }

    [Fact]
    public void One_store_url_leaves_the_other_coming_soon()
    {
        UseSite("https://example.com/play", "");
        var cut = RenderComponent<Download>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("a.store-badge")));

        var link = cut.Find("a.store-badge");
        Assert.Equal("https://example.com/play", link.GetAttribute("href"));
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Contains("noopener", link.GetAttribute("rel"));
        Assert.Contains("Get it on Google Play", link.TextContent);
        Assert.DoesNotContain("Coming soon", link.TextContent);

        var soon = cut.Find(".store-badge-soon");
        Assert.Equal("true", soon.GetAttribute("aria-disabled"));
        Assert.Null(soon.GetAttribute("href"));
        Assert.Contains("Coming soon", soon.TextContent);
        Assert.Contains("Download on the App Store", soon.TextContent);
    }

    private void UseSite(string? google, string? apple)
    {
        Services.AddSingleton(new Session());
        Services.AddSingleton(new HttpClient(new SiteHandler(google, apple)) { BaseAddress = new Uri("http://localhost/") });
        Services.AddSingleton<GpsApi>();
    }

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

    private sealed class SiteHandler : HttpMessageHandler
    {
        private readonly string? _google;
        private readonly string? _apple;

        public SiteHandler(string? google, string? apple)
        {
            _google = google;
            _apple = apple;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_google is null && _apple is null)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));

            var json = JsonSerializer.Serialize(new SiteInfoDto
            {
                GooglePlayUrl = _google ?? "",
                AppStoreUrl = _apple ?? ""
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
