namespace GpsTrainer.Server.Routing;

public sealed record OrsHttpResult(int StatusCode, string Body);

public interface IOpenRouteServiceClient
{
    Task<OrsHttpResult> PostJsonAsync(string relativeUrl, string json, string apiKey, CancellationToken cancellationToken);
    Task<OrsHttpResult> GetAsync(string relativeUrl, string apiKey, CancellationToken cancellationToken);
}

public sealed class OpenRouteServiceClient : IOpenRouteServiceClient
{
    private readonly HttpClient _http;

    public OpenRouteServiceClient(HttpClient http) => _http = http;

    public Task<OrsHttpResult> PostJsonAsync(string relativeUrl, string json, string apiKey, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, relativeUrl, json, apiKey, cancellationToken);

    public Task<OrsHttpResult> GetAsync(string relativeUrl, string apiKey, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, relativeUrl, null, apiKey, cancellationToken);

    private async Task<OrsHttpResult> SendAsync(HttpMethod method, string relativeUrl, string? json, string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativeUrl);
        // The key is a header, never a query parameter, so it stays out of access logs.
        request.Headers.TryAddWithoutValidation("Authorization", apiKey);
        if (json is not null)
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return new OrsHttpResult((int)response.StatusCode, body);
    }
}
