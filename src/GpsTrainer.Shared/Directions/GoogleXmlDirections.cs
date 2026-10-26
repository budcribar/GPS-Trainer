using System.Xml.Linq;

namespace GpsTrainer;

public sealed class TurnStep
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Maneuver { get; set; } = "";
    public string Instructions { get; set; } = "";
}

public interface ITurnDirections
{
    Task<IReadOnlyList<TurnStep>> GetStepsAsync(double originLat, double originLng, double destLat, double destLng, bool walking, CancellationToken cancellationToken = default);
}

/// <summary>
/// Legacy Directions XML client. The 2013 URL had no key. The current HTTPS endpoint
/// needs a key, supplied here as a placeholder and omitted from the request when blank.
/// </summary>
public sealed class GoogleXmlDirections : ITurnDirections
{
    public const string LegacyUrl = "http://maps.googleapis.com/maps/api/directions/xml";
    public const int MinimumGapMilliseconds = 100;

    private readonly HttpClient _http;
    private readonly string? _apiKey;

    public GoogleXmlDirections(HttpClient http, string? apiKey = null)
    {
        _http = http;
        _apiKey = apiKey;
    }

    public static string BuildUrl(double originLat, double originLng, double destLat, double destLng, bool walking, string? apiKey)
    {
        var url = $"{LegacyUrl}?&origin={originLat.ToString(System.Globalization.CultureInfo.InvariantCulture)},{originLng.ToString(System.Globalization.CultureInfo.InvariantCulture)}&destination={destLat.ToString(System.Globalization.CultureInfo.InvariantCulture)},{destLng.ToString(System.Globalization.CultureInfo.InvariantCulture)}&sensor=true";
        if (walking)
            url += "&mode=walking";
        if (!string.IsNullOrWhiteSpace(apiKey))
            url += "&key=" + Uri.EscapeDataString(apiKey);
        return url;
    }

    public async Task<IReadOnlyList<TurnStep>> GetStepsAsync(double originLat, double originLng, double destLat, double destLng, bool walking, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            return [];

        var url = BuildUrl(originLat, originLng, destLat, destLng, walking, _apiKey);
        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var xml = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return Parse(xml);
    }

    public static IReadOnlyList<TurnStep> Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var status = doc.Descendants("status").FirstOrDefault()?.Value;
        if (!string.Equals(status, "OK", StringComparison.Ordinal))
            return [];

        var steps = new List<TurnStep>();
        foreach (var step in doc.Descendants("step"))
        {
            var maneuver = step.Element("maneuver")?.Value;
            var html = step.Element("html_instructions")?.Value;
            var start = step.Element("start_location");
            if (maneuver is null || html is null || start is null)
                continue;
            if (!double.TryParse(start.Element("lat")?.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lat))
                continue;
            if (!double.TryParse(start.Element("lng")?.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lng))
                continue;
            steps.Add(new TurnStep
            {
                Latitude = lat,
                Longitude = lng,
                Maneuver = maneuver,
                Instructions = StripTags(html)
            });
        }
        return steps;
    }

    public static string StripTags(string html)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        return string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
