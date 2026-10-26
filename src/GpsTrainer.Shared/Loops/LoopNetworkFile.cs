using System.Globalization;
using System.Text.Json;

namespace GpsTrainer;

public static class LoopNetworkFile
{
    public const string AttributionText =
        "Trail corridors © COTREX / Colorado Parks and Wildlife. Bike lanes © OpenStreetMap contributors (ODbL).";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static LoopNetwork Load(string json)
    {
        var file = JsonSerializer.Deserialize<FileModel>(json, JsonOptions) ?? new FileModel();
        var segments = new List<NetworkSegment>();
        foreach (var segment in file.Segments ?? [])
        {
            var points = new List<GeoCoord>();
            foreach (var coord in segment.Coords ?? [])
            {
                if (coord is null || coord.Length < 2)
                    continue;
                var longitude = coord[0];
                var latitude = coord[1];
                if (!RoutingLimits.IsPosition(latitude, longitude))
                    continue;
                points.Add(GeoCoord.At(latitude, longitude));
            }
            if (points.Count < 2)
                continue;
            segments.Add(new NetworkSegment
            {
                Name = (segment.Name ?? "").Trim(),
                Facility = ParseFacility(segment.Kind),
                Surface = ParseSurface(segment.Surface),
                Source = (segment.Source ?? "").Trim(),
                Points = points
            });
        }

        var places = new List<NetworkPlace>();
        foreach (var place in file.Places ?? [])
        {
            if (!RoutingLimits.IsPosition(place.Lat, place.Lon))
                continue;
            var name = (place.Name ?? "").Trim();
            if (name.Length == 0)
                continue;
            places.Add(new NetworkPlace
            {
                Name = name,
                Kind = (place.Kind ?? "").Trim(),
                Latitude = place.Lat,
                Longitude = place.Lon
            });
        }

        return new LoopNetwork
        {
            FetchedUtc = file.FetchedUtc ?? "",
            Attribution = AttributionText,
            Segments = segments,
            Places = places
        };
    }

    public static LoopNetwork LoadFile(string path) =>
        Load(File.ReadAllText(path));

    private static LoopFacility ParseFacility(string? kind) =>
        (kind ?? "").Trim().ToLower(CultureInfo.InvariantCulture) switch
        {
            "lane" => LoopFacility.Lane,
            "link" => LoopFacility.Link,
            _ => LoopFacility.Trail
        };

    private static LoopSurface ParseSurface(string? surface) =>
        (surface ?? "").Trim().ToLower(CultureInfo.InvariantCulture) switch
        {
            "paved" => LoopSurface.Paved,
            "unpaved" => LoopSurface.Unpaved,
            _ => LoopSurface.Unknown
        };

    private sealed class FileModel
    {
        public string? FetchedUtc { get; set; }
        public List<PlaceModel>? Places { get; set; }
        public List<SegmentModel>? Segments { get; set; }
    }

    private sealed class PlaceModel
    {
        public string? Name { get; set; }
        public string? Kind { get; set; }
        public double Lat { get; set; }
        public double Lon { get; set; }
    }

    private sealed class SegmentModel
    {
        public string? Name { get; set; }
        public string? Kind { get; set; }
        public string? Surface { get; set; }
        public string? Source { get; set; }
        public List<double[]?>? Coords { get; set; }
    }
}
