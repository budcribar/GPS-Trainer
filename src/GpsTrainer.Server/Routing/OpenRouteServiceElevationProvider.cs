using System.Text.Json;
using System.Text.Json.Serialization;
using GpsTrainer;
using GpsTrainer.Server.Data;
using GpsTrainer.Server.Loops;

namespace GpsTrainer.Server.Routing;

/// <summary>
/// OpenRouteService elevation on the HeiGIT host. The free plan allows 2,000 elevation
/// requests a day and 40 a minute. This server does not add a second daily cap for
/// signed-in riders. A quota response is reported as <see cref="ElevationLineResponseDto.Limited"/>
/// and is not cached. <see cref="OrsQuotaGate"/> still decides when loop generation stops calling.
/// </summary>
public sealed class OpenRouteServiceElevationProvider : IElevationProvider
{
    public const string ProviderName = "OpenRouteService";

    public const int MaxElevationPoints = 300;

    private readonly IOpenRouteServiceClient _client;
    private readonly ElevationLineCache _cache;
    private readonly OpenRouteServiceEndpoints _endpoints;
    private readonly Func<CancellationToken, Task<string?>> _apiKey;
    private string? _resolvedKey;
    private bool _hasResolvedKey;

    public OpenRouteServiceElevationProvider(
        IOpenRouteServiceClient client,
        ElevationLineCache cache,
        OpenRouteServiceEndpoints endpoints,
        RoutingSettingsStore store,
        GpsTrainerDb db,
        IConfiguration configuration)
        : this(client, cache, endpoints, cancellationToken => CurrentKey(store, db, configuration, cancellationToken))
    {
    }

    public OpenRouteServiceElevationProvider(
        IOpenRouteServiceClient client,
        string apiKey,
        ElevationLineCache? cache = null,
        OpenRouteServiceEndpoints? endpoints = null)
        : this(
            client,
            cache ?? new ElevationLineCache(),
            endpoints ?? OpenRouteServiceEndpoints.Defaults,
            _ => Task.FromResult<string?>(apiKey))
    {
    }

    public OpenRouteServiceElevationProvider(
        IOpenRouteServiceClient client,
        ElevationLineCache cache,
        OpenRouteServiceEndpoints endpoints,
        Func<CancellationToken, Task<string?>> apiKey)
    {
        _client = client;
        _cache = cache;
        _endpoints = endpoints;
        _apiKey = apiKey;
    }

    public string Name => ProviderName;

    public async Task<ElevationLineResponseDto> ElevationLineAsync(
        IReadOnlyList<(double Longitude, double Latitude)> points,
        CancellationToken cancellationToken)
    {
        var line = points ?? [];
        var apiKey = await ApiKeyAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(apiKey))
            return new ElevationLineResponseDto { Unavailable = true, Message = "Elevation is not configured." };
        if (line.Count == 0)
            return ElevationFail("No elevation was found.");
        if (line.Count > MaxElevationPoints)
            return ElevationFail("That elevation line is too long.");
        foreach (var point in line)
        {
            if (!RoutingLimits.IsPosition(point.Latitude, point.Longitude))
                return ElevationFail("Those coordinates are not on the map.");
        }

        var samples = line.Select(point => new ElevationSampleDto
        {
            Longitude = point.Longitude,
            Latitude = point.Latitude
        }).ToList();
        var cacheKey = _cache.KeyFor(samples);
        if (_cache.TryGet(cacheKey, out var cached))
            return ElevationResult(line, cached, fromCache: true);

        var body = JsonSerializer.Serialize(new OrsElevationBody
        {
            Geometry = line.Select(point => new[] { point.Longitude, point.Latitude }).ToArray()
        });

        OrsHttpResult result;
        try
        {
            result = await _client.PostJsonAsync(_endpoints.ElevationLine(), body, apiKey, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return ElevationFail("Elevation lookup failed.");
        }
        catch (TaskCanceledException)
        {
            return ElevationFail("Elevation lookup timed out.");
        }

        if (result.StatusCode is < 200 or >= 300)
            return Quota(Scrub(ElevationFail(ElevationMessage(result.Body)), apiKey), result);

        var parsed = OrsGeoJson.ParseElevation(result.Body);
        if (!parsed.Ok || parsed.Meters.Length != line.Count)
            return Quota(Scrub(ElevationFail(string.IsNullOrWhiteSpace(parsed.Message) ? "Elevation lookup failed." : parsed.Message), apiKey), result);

        var kilometers = parsed.Meters.Select(meters => meters / 1000.0).ToArray();
        _cache.Store(cacheKey, kilometers);
        return Scrub(ElevationResult(line, kilometers, fromCache: false), apiKey);
    }

    private async Task<string?> ApiKeyAsync(CancellationToken cancellationToken)
    {
        if (_hasResolvedKey)
            return _resolvedKey;
        _resolvedKey = await _apiKey(cancellationToken).ConfigureAwait(false);
        _hasResolvedKey = true;
        return _resolvedKey;
    }

    private static async Task<string?> CurrentKey(
        RoutingSettingsStore store,
        GpsTrainerDb db,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var resolved = await store.ResolveAsync(db, configuration, cancellationToken).ConfigureAwait(false);
        return resolved.ApiKey;
    }

    private static ElevationLineResponseDto ElevationResult(
        IReadOnlyList<(double Longitude, double Latitude)> requested,
        double[] altitudeKm,
        bool fromCache)
    {
        var points = new List<ElevationSampleDto>(requested.Count);
        for (var i = 0; i < requested.Count; i++)
        {
            var source = requested[i];
            points.Add(new ElevationSampleDto
            {
                Latitude = source.Latitude,
                Longitude = source.Longitude,
                AltitudeKm = i < altitudeKm.Length && SummitSearch.IsKnown(altitudeKm[i])
                    ? altitudeKm[i]
                    : Altitude.Unknown.Kilometers
            });
        }
        return new ElevationLineResponseDto { Found = true, FromCache = fromCache, Points = points };
    }

    private static ElevationLineResponseDto ElevationFail(string message) => new() { Message = message };

    private static ElevationLineResponseDto Quota(ElevationLineResponseDto response, OrsHttpResult result)
    {
        if (OrsQuotaGate.IsQuota(result.StatusCode, result.Body))
            response.Limited = true;
        return response;
    }

    private static string ElevationMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "Elevation lookup failed.";
        var parsed = OrsGeoJson.ParseElevation(body);
        return string.IsNullOrWhiteSpace(parsed.Message) ? "Elevation lookup failed." : parsed.Message;
    }

    private static ElevationLineResponseDto Scrub(ElevationLineResponseDto response, string apiKey)
    {
        if (!string.IsNullOrEmpty(apiKey) && response.Message.Contains(apiKey, StringComparison.Ordinal))
            response.Message = "Elevation lookup failed.";
        return response;
    }

    private sealed class OrsElevationBody
    {
        [JsonPropertyName("format_in")]
        public string FormatIn { get; set; } = "polyline";

        [JsonPropertyName("format_out")]
        public string FormatOut { get; set; } = "polyline";

        [JsonPropertyName("geometry")]
        public double[][] Geometry { get; set; } = [];
    }
}
