using System.Text.Json;
using GpsTrainer;

namespace GpsTrainer.Server.Routing;

public static class OrsGeoJson
{
    public static RouteResponseDto ParseRoute(string json, double originLatitude, double originLongitude)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return RouteResponseDto.Fail("Routing failed.");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var error))
                return RouteResponseDto.Fail(ReadError(error));

            if (!root.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array || features.GetArrayLength() == 0)
                return RouteResponseDto.Fail("No route found.");

            var feature = features[0];
            if (!feature.TryGetProperty("geometry", out var geometry)
                || !geometry.TryGetProperty("coordinates", out var coordinates)
                || coordinates.ValueKind != JsonValueKind.Array)
                return RouteResponseDto.Fail("No route found.");

            var parsed = new List<ParsedPoint>();
            foreach (var coordinate in coordinates.EnumerateArray())
            {
                if (coordinate.GetArrayLength() < 2)
                    continue;
                parsed.Add(new ParsedPoint(
                    coordinate[1].GetDouble(),
                    coordinate[0].GetDouble(),
                    ReadAltitude(coordinate),
                    WaypointType.Continue,
                    null));
            }
            if (parsed.Count < 2)
                return RouteResponseDto.Fail("No route found.");

            ApplyManeuvers(feature, parsed);

            var start = 0;
            double? originAltitude = null;
            if (GeoMath.DistanceMeters(parsed[0].Latitude, parsed[0].Longitude, originLatitude, originLongitude) < 15)
            {
                originAltitude = parsed[0].AltitudeKm;
                start = 1;
            }
            if (start >= parsed.Count)
                return RouteResponseDto.Fail("No route found.");

            var kept = Thin(parsed, start);
            if (kept.Count == 0)
                return RouteResponseDto.Fail("No route found.");

            return new RouteResponseDto
            {
                Routed = true,
                Provider = OpenRouteServiceRoutingProvider.ProviderName,
                Attribution = MapDefaults.Attribution,
                OriginAltitudeKm = originAltitude,
                Points = kept.Select(point => new RoutePointDto
                {
                    Latitude = point.Latitude,
                    Longitude = point.Longitude,
                    WaypointType = point.Type.ToString(),
                    Text = point.Text,
                    AltitudeKm = point.AltitudeKm ?? Altitude.Unknown.Kilometers
                }).ToList()
            };
        }
    }

    public static GeocodeResponseDto ParseGeocode(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return new GeocodeResponseDto { Message = "Geocoding failed." };
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var error))
                return new GeocodeResponseDto { Message = ReadError(error) };
            if (!root.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array || features.GetArrayLength() == 0)
                return new GeocodeResponseDto { Message = "No location found." };

            var feature = features[0];
            if (!feature.TryGetProperty("geometry", out var geometry)
                || !geometry.TryGetProperty("coordinates", out var coordinates)
                || coordinates.GetArrayLength() < 2)
                return new GeocodeResponseDto { Message = "No location found." };

            var longitude = coordinates[0].GetDouble();
            var latitude = coordinates[1].GetDouble();
            if (!RoutingLimits.IsPosition(latitude, longitude))
                return new GeocodeResponseDto { Message = "No location found." };

            var label = "";
            if (feature.TryGetProperty("properties", out var properties) && properties.TryGetProperty("label", out var labelElement))
                label = labelElement.GetString() ?? "";
            if (label.Length > RoutingLimits.MaxAddressLength)
                label = label[..RoutingLimits.MaxAddressLength];

            return new GeocodeResponseDto
            {
                Found = true,
                Latitude = latitude,
                Longitude = longitude,
                Label = label
            };
        }
    }

    private static void ApplyManeuvers(JsonElement feature, List<ParsedPoint> points)
    {
        if (!feature.TryGetProperty("properties", out var properties)
            || !properties.TryGetProperty("segments", out var segments)
            || segments.ValueKind != JsonValueKind.Array)
            return;

        foreach (var segment in segments.EnumerateArray())
        {
            if (!segment.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var step in steps.EnumerateArray())
            {
                var code = step.TryGetProperty("type", out var typeElement) ? typeElement.GetInt32() : 6;
                if (MapManeuver(code) is not WaypointType maneuver)
                    continue;
                if (!step.TryGetProperty("way_points", out var wayPoints) || wayPoints.GetArrayLength() < 1)
                    continue;
                var index = wayPoints[0].GetInt32();
                if (index < 0 || index >= points.Count)
                    continue;
                var text = step.TryGetProperty("instruction", out var instruction) ? instruction.GetString() : null;
                var current = points[index];
                points[index] = current with { Type = maneuver, Text = text };
            }
        }
    }

    /// <summary>
    /// OpenRouteService instruction codes. Straight, depart, goal, and roundabout stays are not turn markers.
    /// </summary>
    public static WaypointType? MapManeuver(int code) => code switch
    {
        0 or 2 => WaypointType.TurnLeft,
        1 or 3 => WaypointType.TurnRight,
        4 or 12 => WaypointType.BearLeft,
        5 or 13 => WaypointType.BearRight,
        9 => WaypointType.TurnAround,
        _ => null
    };

    private static List<ParsedPoint> Thin(List<ParsedPoint> points, int start)
    {
        var kept = new List<ParsedPoint>();
        for (var i = start; i < points.Count; i++)
        {
            var point = points[i];
            var last = i == points.Count - 1;
            if (!last && point.Type == WaypointType.Continue && kept.Count > 0
                && GeoMath.DistanceMeters(kept[^1].Latitude, kept[^1].Longitude, point.Latitude, point.Longitude) < 12)
                continue;
            kept.Add(point);
        }

        if (kept.Count <= RoutingLimits.MaxStoredPoints)
            return kept;

        var must = new HashSet<int> { 0, kept.Count - 1 };
        for (var i = 0; i < kept.Count; i++)
        {
            if (kept[i].Type != WaypointType.Continue)
                must.Add(i);
        }

        if (must.Count >= RoutingLimits.MaxStoredPoints)
        {
            var reduced = new List<ParsedPoint>();
            foreach (var index in must.OrderBy(i => i))
            {
                if (reduced.Count >= RoutingLimits.MaxStoredPoints - 1 && index != kept.Count - 1)
                    continue;
                reduced.Add(kept[index]);
            }
            if (reduced.Count == 0 || reduced[^1] != kept[^1])
                reduced.Add(kept[^1]);
            return reduced;
        }

        var budget = RoutingLimits.MaxStoredPoints - must.Count;
        var extrasAvailable = kept.Count - must.Count;
        var stride = extrasAvailable / (double)budget;
        var result = new List<ParsedPoint>(RoutingLimits.MaxStoredPoints);
        var credit = 0.0;
        var extras = 0;
        for (var i = 0; i < kept.Count; i++)
        {
            if (must.Contains(i))
            {
                result.Add(kept[i]);
                continue;
            }
            credit += 1;
            if (extras < budget && credit >= stride)
            {
                result.Add(kept[i]);
                extras++;
                credit -= stride;
            }
        }
        return result;
    }

    public readonly record struct ParsedElevation(bool Ok, string Message, double[] Meters);

    /// <summary>
    /// Elevations from an elevation/line body. Accepts a polyline geometry array or a
    /// GeoJSON geometry with coordinates. Values are meters.
    /// </summary>
    public static ParsedElevation ParseElevation(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new ParsedElevation(false, "Elevation lookup failed.", []);
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return new ParsedElevation(false, "Elevation lookup failed.", []);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error))
                return new ParsedElevation(false, TrimElevationError(error), []);
            if (!TryElevationCoordinates(root, out var coordinates))
                return new ParsedElevation(false, "Elevation lookup failed.", []);

            var meters = new List<double>();
            foreach (var item in coordinates.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() < 3 || item[2].ValueKind != JsonValueKind.Number)
                    return new ParsedElevation(false, "Elevation lookup failed.", []);
                var value = item[2].GetDouble();
                if (!double.IsFinite(value))
                    return new ParsedElevation(false, "Elevation lookup failed.", []);
                meters.Add(value);
            }
            if (meters.Count == 0)
                return new ParsedElevation(false, "Elevation lookup failed.", []);
            return new ParsedElevation(true, "", meters.ToArray());
        }
    }

    private static bool TryElevationCoordinates(JsonElement root, out JsonElement coordinates)
    {
        coordinates = default;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("geometry", out var geometry))
            return false;
        if (geometry.ValueKind == JsonValueKind.Array)
        {
            coordinates = geometry;
            return true;
        }
        if (geometry.ValueKind == JsonValueKind.Object
            && geometry.TryGetProperty("coordinates", out var nested)
            && nested.ValueKind == JsonValueKind.Array)
        {
            coordinates = nested;
            return true;
        }
        return false;
    }

    private static string TrimElevationError(JsonElement error)
    {
        string? message = error.ValueKind switch
        {
            JsonValueKind.String => error.GetString(),
            JsonValueKind.Object when error.TryGetProperty("message", out var messageElement) => messageElement.GetString(),
            _ => null
        };
        if (string.IsNullOrWhiteSpace(message))
            return "Elevation lookup failed.";
        var trimmed = message.Trim();
        return trimmed.Length <= 300 ? trimmed : trimmed[..300];
    }

    private static double? ReadAltitude(JsonElement coordinate)
    {
        if (coordinate.GetArrayLength() < 3 || coordinate[2].ValueKind != JsonValueKind.Number)
            return null;
        var meters = coordinate[2].GetDouble();
        return double.IsFinite(meters) ? meters / 1000.0 : null;
    }

    private static string ReadError(JsonElement error)
    {
        string? message = error.ValueKind switch
        {
            JsonValueKind.String => error.GetString(),
            JsonValueKind.Object when error.TryGetProperty("message", out var messageElement) => messageElement.GetString(),
            _ => null
        };
        if (string.IsNullOrWhiteSpace(message))
            return "Routing failed.";
        var trimmed = message.Trim();
        return trimmed.Length <= 300 ? trimmed : trimmed[..300];
    }

    private sealed record ParsedPoint(double Latitude, double Longitude, double? AltitudeKm, WaypointType Type, string? Text);
}
