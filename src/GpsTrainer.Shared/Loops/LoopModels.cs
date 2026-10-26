namespace GpsTrainer;

public enum LoopFacility
{
    Trail,
    Lane,
    Link
}

public enum LoopSurface
{
    Paved,
    Unpaved,
    Unknown
}

public enum LoopRouteType
{
    TrailFirst,
    BikeLanes
}

public enum ClimbPreference
{
    Flat,
    Moderate,
    Hilly
}

public readonly record struct GeoCoord(double Latitude, double Longitude, double AltitudeKm)
{
    public static GeoCoord At(double latitude, double longitude, double? altitudeKm = null) =>
        new(latitude, longitude, altitudeKm ?? Altitude.Unknown.Kilometers);

    public bool HasAltitude => SummitSearch.IsKnown(AltitudeKm);
}

public sealed class NetworkSegment
{
    public string Name { get; init; } = "";
    public LoopFacility Facility { get; init; }
    public LoopSurface Surface { get; init; }
    public string Source { get; init; } = "";
    public IReadOnlyList<GeoCoord> Points { get; init; } = [];
}

public sealed class NetworkPlace
{
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "";
    public double Latitude { get; init; }
    public double Longitude { get; init; }
}

public sealed class LoopNetwork
{
    public string FetchedUtc { get; init; } = "";
    public string Attribution { get; init; } = "";
    public IReadOnlyList<NetworkSegment> Segments { get; init; } = [];
    public IReadOnlyList<NetworkPlace> Places { get; init; } = [];
}

public sealed record LoopRequest(
    double Latitude,
    double Longitude,
    double TargetDistanceMeters,
    ClimbPreference Climbing,
    LoopRouteType Type);

public sealed record LoopGraphOptions
{
    public double SnapMeters { get; init; } = 40;
    public double LinkMeters { get; init; } = 90;
    public int MaxLinksPerNode { get; init; } = 2;

    public static LoopGraphOptions Default { get; } = new();
}

public sealed class LoopNode
{
    public LoopNode(int id, double latitude, double longitude)
    {
        Id = id;
        Latitude = latitude;
        Longitude = longitude;
    }

    public int Id { get; }
    public double Latitude { get; }
    public double Longitude { get; }
}

public sealed class LoopEdge
{
    public int Id { get; set; }
    public int A { get; init; }
    public int B { get; init; }
    public double LengthMeters { get; init; }
    public LoopFacility Facility { get; init; }
    public LoopSurface Surface { get; init; }
    public string Name { get; init; } = "";
    public string Source { get; init; } = "";
    public IReadOnlyList<GeoCoord> Geometry { get; init; } = [];

    public int Other(int node) => node == A ? B : A;
}

public readonly record struct LoopScore(double Distance, double Climbing, double Backtracking, double BikeLegal, double Total);

public sealed record LoopRoute
{
    public required string Name { get; init; }
    public required IReadOnlyList<GeoCoord> Points { get; init; }
    public required IReadOnlyList<int> EdgeIds { get; init; }
    public double DistanceMeters { get; init; }
    public double ClimbingMeters { get; init; }
    public double BikeLegalShare { get; init; }
    public double TrailShare { get; init; }
    public double LaneShare { get; init; }
    public double BacktrackShare { get; init; }
    public double CorridorShare { get; init; }
    public double Score { get; init; }
    public LoopScore Breakdown { get; init; }
    public bool ElevationKnown { get; init; }
    public bool RoadSnapped { get; init; }
}
