namespace GpsTrainer.Server.Routing;

/// <summary>
/// HeiGIT gives each OpenRouteService its own prefix. The defaults are those prefixes.
/// <c>Routing:BaseUrl</c> is used only when none of the three bases is set, so a leftover
/// Railway value cannot put calls back on <c>api.openrouteservice.org</c>.
/// </summary>
public sealed class OpenRouteServiceEndpoints
{
    public const string DefaultDirectionsBaseUrl = "https://api.heigit.org/openrouteservice/";
    public const string DefaultElevationBaseUrl = "https://api.heigit.org/openelevationservice/v0/";
    public const string DefaultGeocodeBaseUrl = "https://api.heigit.org/pelias/v1/";

    public OpenRouteServiceEndpoints(string directionsBaseUrl, string elevationBaseUrl, string geocodeBaseUrl)
    {
        DirectionsBaseUrl = Normalize(directionsBaseUrl);
        ElevationBaseUrl = Normalize(elevationBaseUrl);
        GeocodeBaseUrl = Normalize(geocodeBaseUrl);
    }

    public string DirectionsBaseUrl { get; }
    public string ElevationBaseUrl { get; }
    public string GeocodeBaseUrl { get; }

    public static OpenRouteServiceEndpoints Defaults { get; } = new(
        DefaultDirectionsBaseUrl,
        DefaultElevationBaseUrl,
        DefaultGeocodeBaseUrl);

    public static OpenRouteServiceEndpoints From(IConfiguration config)
    {
        var directions = Blank(config["Routing:DirectionsBaseUrl"]);
        var elevation = Blank(config["Routing:ElevationBaseUrl"]);
        var geocode = Blank(config["Routing:GeocodeBaseUrl"]);
        var legacy = Blank(config["Routing:BaseUrl"]);

        if (directions is null && elevation is null && geocode is null && legacy is not null)
            return new OpenRouteServiceEndpoints(legacy, legacy, legacy);

        return new OpenRouteServiceEndpoints(
            directions ?? DefaultDirectionsBaseUrl,
            elevation ?? DefaultElevationBaseUrl,
            geocode ?? DefaultGeocodeBaseUrl);
    }

    public string Directions(string profile) =>
        Combine(DirectionsBaseUrl, "v2/directions/" + profile + "/geojson");

    public string ElevationLine() =>
        Combine(ElevationBaseUrl, UsesLegacyPaths(ElevationBaseUrl) ? "elevation/line" : "line");

    public string Geocode(string text)
    {
        var path = UsesLegacyPaths(GeocodeBaseUrl) ? "geocode/search" : "search";
        return Combine(GeocodeBaseUrl, path + "?size=1&text=" + Uri.EscapeDataString(text));
    }

    public static bool UsesLegacyPaths(string baseUrl)
    {
        if (!Uri.TryCreate(Normalize(baseUrl), UriKind.Absolute, out var uri))
            return false;
        return uri.Host.Equals("api.openrouteservice.org", StringComparison.OrdinalIgnoreCase);
    }

    private static string Combine(string baseUrl, string relative) =>
        new Uri(new Uri(Normalize(baseUrl), UriKind.Absolute), relative).AbsoluteUri;

    private static string Normalize(string url)
    {
        var trimmed = url.Trim();
        return trimmed.EndsWith('/') ? trimmed : trimmed + "/";
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
