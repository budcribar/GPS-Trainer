using System.Text.Json;
using System.Text.Json.Serialization;
using GpsTrainer;

namespace GpsTrainer.Server.Routing;

public interface IRoutingProvider
{
    string Name { get; }
    Task<RouteResponseDto> RouteAsync(RouteRequestDto request, string apiKey, CancellationToken cancellationToken);
    Task<GeocodeResponseDto> GeocodeAsync(string address, string apiKey, CancellationToken cancellationToken);
}

public interface IRoutingProviderFactory
{
    IRoutingProvider? Get(string name);
    IReadOnlyList<string> Names { get; }
}

public sealed class RoutingProviderFactory : IRoutingProviderFactory
{
    private readonly IReadOnlyDictionary<string, IRoutingProvider> _providers;

    public RoutingProviderFactory(IEnumerable<IRoutingProvider> providers)
    {
        _providers = providers.ToDictionary(provider => provider.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> Names { get; } = [OpenRouteServiceRoutingProvider.ProviderName];

    public IRoutingProvider? Get(string name) =>
        _providers.TryGetValue(name, out var provider) ? provider : null;
}

public sealed class OpenRouteServiceRoutingProvider : IRoutingProvider
{
    public const string ProviderName = "OpenRouteService";

    private readonly IOpenRouteServiceClient _client;
    private readonly OpenRouteServiceEndpoints _endpoints;

    public OpenRouteServiceRoutingProvider(
        IOpenRouteServiceClient client,
        OpenRouteServiceEndpoints? endpoints = null)
    {
        _client = client;
        _endpoints = endpoints ?? OpenRouteServiceEndpoints.Defaults;
    }

    public string Name => ProviderName;

    public async Task<RouteResponseDto> RouteAsync(RouteRequestDto request, string apiKey, CancellationToken cancellationToken)
    {
        var rejected = RoutingLimits.RejectSegment(request.FromLatitude, request.FromLongitude, request.ToLatitude, request.ToLongitude);
        if (rejected is not null)
            return Scrub(RouteResponseDto.Fail(rejected, RoutingLimits.AllowsStraightFallback(rejected)), apiKey);

        var profile = OrsProfile.ForActivity(request.Activity);
        if (!OrsProfile.IsAllowed(profile))
            return RouteResponseDto.Fail("Routing failed.");

        var body = JsonSerializer.Serialize(new OrsDirectionsBody
        {
            Coordinates =
            [
                [request.FromLongitude, request.FromLatitude],
                [request.ToLongitude, request.ToLatitude]
            ],
            Radiuses = SnapRadiuses(request.SnapRadiusMeters)
        });

        OrsHttpResult result;
        try
        {
            result = await _client.PostJsonAsync(_endpoints.Directions(profile), body, apiKey, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return RouteResponseDto.Fail("Routing failed.");
        }
        catch (TaskCanceledException)
        {
            return RouteResponseDto.Fail("Routing timed out.");
        }

        if (string.IsNullOrWhiteSpace(result.Body))
            return RouteResponseDto.Fail(result.StatusCode is >= 200 and < 300 ? "No route found." : "Routing failed.");

        var parsed = OrsGeoJson.ParseRoute(result.Body, request.FromLatitude, request.FromLongitude);
        if (!parsed.Routed && result.StatusCode is < 200 or >= 300 && string.IsNullOrWhiteSpace(parsed.Message))
            parsed.Message = "Routing failed.";
        return Scrub(parsed, apiKey);
    }

    public async Task<GeocodeResponseDto> GeocodeAsync(string address, string apiKey, CancellationToken cancellationToken)
    {
        var trimmed = address.Trim();
        if (trimmed.Length == 0)
            return new GeocodeResponseDto { Message = "Enter an address." };
        if (trimmed.Length > RoutingLimits.MaxAddressLength)
            return new GeocodeResponseDto { Message = "That address is too long." };

        OrsHttpResult result;
        try
        {
            result = await _client.GetAsync(_endpoints.Geocode(trimmed), apiKey, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return new GeocodeResponseDto { Message = "Geocoding failed." };
        }
        catch (TaskCanceledException)
        {
            return new GeocodeResponseDto { Message = "Geocoding timed out." };
        }

        if (string.IsNullOrWhiteSpace(result.Body))
            return new GeocodeResponseDto { Message = "No location found." };
        var parsed = OrsGeoJson.ParseGeocode(result.Body);
        if (!string.IsNullOrEmpty(apiKey) && parsed.Message.Contains(apiKey, StringComparison.Ordinal))
            parsed.Message = "Geocoding failed.";
        return parsed;
    }

    /// <summary>
    /// ORS searches this many meters for a road at each end. A missing radius
    /// keeps the service default. Values outside the close-snap limit are ignored.
    /// </summary>
    private static double[]? SnapRadiuses(double? meters)
    {
        if (meters is null || meters is < 1 or > LoopClose.CloseSnapMeters)
            return null;
        return [meters.Value, meters.Value];
    }

    private static RouteResponseDto Scrub(RouteResponseDto response, string apiKey)
    {
        if (!string.IsNullOrEmpty(apiKey) && response.Message.Contains(apiKey, StringComparison.Ordinal))
            response.Message = "Routing failed.";
        return response;
    }

    private sealed class OrsDirectionsBody
    {
        [JsonPropertyName("coordinates")]
        public double[][] Coordinates { get; set; } = [];

        [JsonPropertyName("instructions")]
        public bool Instructions { get; set; } = true;

        [JsonPropertyName("instructions_format")]
        public string InstructionsFormat { get; set; } = "text";

        [JsonPropertyName("elevation")]
        public bool Elevation { get; set; } = true;

        [JsonPropertyName("radiuses")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double[]? Radiuses { get; set; }
    }
}
