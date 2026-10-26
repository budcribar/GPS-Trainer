using System.Net;

namespace GpsTrainer.Server.Tests;

public class StaticAssetTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;

    public StaticAssetTests(ApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Home_and_deep_links_serve_a_boot_script_that_exists()
    {
        foreach (var path in new[] { "/", "/find" })
        {
            var page = await _client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            var html = await page.Content.ReadAsStringAsync();
            Assert.DoesNotContain("#[.", html, StringComparison.Ordinal);
            Assert.Contains("src=\"_framework/blazor.webassembly.js\"", html, StringComparison.Ordinal);
        }

        var script = await _client.GetAsync("/_framework/blazor.webassembly.js");
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
        Assert.StartsWith("text/javascript", script.Content.Headers.ContentType?.MediaType);

        var runtime = await _client.GetAsync("/_framework/dotnet.js");
        Assert.Equal(HttpStatusCode.OK, runtime.StatusCode);
    }
}
