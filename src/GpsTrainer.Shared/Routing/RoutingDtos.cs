namespace GpsTrainer;

public sealed class RouteRequestDto
{
    public double FromLatitude { get; set; }
    public double FromLongitude { get; set; }
    public double ToLatitude { get; set; }
    public double ToLongitude { get; set; }
    public string? Activity { get; set; }

    /// <summary>
    /// Widens the road snap for both ends, in meters. Null keeps the router's own snap.
    /// The server only forwards a radius inside <see cref="LoopClose.CloseSnapMeters"/>.
    /// </summary>
    public double? SnapRadiusMeters { get; set; }
}

public sealed class RoutePointDto
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string WaypointType { get; set; } = "Continue";
    public string? Text { get; set; }
    public double AltitudeKm { get; set; } = Altitude.Unknown.Kilometers;
}

public sealed class RouteResponseDto
{
    public bool Routed { get; set; }
    public bool StraightFallback { get; set; } = true;
    public string Message { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Attribution { get; set; } = "";
    public double? OriginAltitudeKm { get; set; }
    public List<RoutePointDto> Points { get; set; } = [];

    public static RouteResponseDto Fail(string message, bool straightFallback = true) => new()
    {
        Routed = false,
        StraightFallback = straightFallback,
        Message = message,
        Attribution = MapDefaults.Attribution
    };
}

public sealed class GeocodeRequestDto
{
    public string Address { get; set; } = "";
}

public sealed class GeocodeResponseDto
{
    public bool Found { get; set; }
    public string Message { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Label { get; set; } = "";
}

public sealed class UserLocationDto
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

public sealed class RoutingSettingsDto
{
    public string Provider { get; set; } = "OpenRouteService";
    public string[] Providers { get; set; } = ["OpenRouteService"];
    public bool KeyConfigured { get; set; }
    public string KeyMask { get; set; } = "";
    public string KeySource { get; set; } = "none";
}

public sealed class RoutingSettingsWriteDto
{
    public string Provider { get; set; } = "OpenRouteService";
    public string? ApiKey { get; set; }
    public bool ClearStoredKey { get; set; }
}

public sealed class ElevationSampleDto
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double ElapsedKm { get; set; }
    public double AltitudeKm { get; set; } = Altitude.Unknown.Kilometers;
}

public sealed class ElevationLineRequestDto
{
    public List<ElevationSampleDto> Points { get; set; } = [];
}

public sealed class ElevationLineResponseDto
{
    public bool Found { get; set; }
    public bool FromCache { get; set; }
    public string Message { get; set; } = "";
    public List<ElevationSampleDto> Points { get; set; } = [];
}
