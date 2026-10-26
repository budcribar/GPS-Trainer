using System.Net;
using GpsTrainer.Web.Layout;
using Microsoft.AspNetCore.Components;
using GpsTrainer.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GpsTrainer.Web.Tests;

public class SiteBrandTests : TestContext
{
    public SiteBrandTests()
    {
        Services.AddScoped<Session>();
        Services.AddScoped(_ => new HttpClient(new OkHandler()) { BaseAddress = new Uri("http://localhost/") });
        Services.AddScoped<GpsApi>();
    }

    [Fact]
    public void Pages_without_the_editor_keep_the_logo_and_subtitle_in_the_header()
    {
        var cut = RenderComponent<MainLayout>();
        var header = cut.Find("header");

        Assert.Contains("site-brand", header.InnerHtml);
        Assert.Contains("SmallLogo.jpg", header.InnerHtml);
        Assert.Contains("domain-name", header.InnerHtml);
        Assert.Contains("gps-trainer", header.TextContent);
        Assert.Contains("domain-com", header.InnerHtml);
        Assert.DoesNotContain("gpsTrainer", header.TextContent);
        Assert.DoesNotContain("gps-trainer", cut.Find("nav").TextContent);
        Assert.Contains(
            "Copyright © Peak Software Consulting, LLC. TruePace and gpsTrainer are trademarks of Peak Software Consulting, LLC.",
            cut.Find("footer").TextContent);
        Assert.Equal("privacy", cut.Find("footer a").GetAttribute("href"));
        Assert.Equal("Privacy policy", cut.Find("footer a").TextContent);
    }

    [Fact]
    public void The_editor_route_does_not_repeat_the_brand_in_the_header()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("editor");

        var cut = RenderComponent<MainLayout>();

        Assert.Empty(cut.FindAll("header .site-brand"));
        Assert.Contains("Home", cut.Find("nav").TextContent);
        Assert.DoesNotContain("gps-trainer", cut.Find("nav").TextContent);
    }

    [Fact]
    public void Subtitle_colors_are_blue_for_the_name_and_gold_for_com()
    {
        var css = File.ReadAllText(Path.Combine(RepoRoot(), "src", "GpsTrainer.Web", "wwwroot", "css", "app.css"));

        Assert.Contains(".site-brand .domain-name { color: #0076BD; }", css, StringComparison.Ordinal);
        Assert.Contains(".site-brand .domain-com { color: #E8B836; }", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".brand {", css, StringComparison.Ordinal);
    }

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GPS-Trainer.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
