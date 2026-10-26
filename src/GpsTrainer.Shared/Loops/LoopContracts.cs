using System.Globalization;

namespace GpsTrainer;

public sealed class LoopGenerateRequestDto
{
    public double Latitude { get; set; } = MapDefaults.FortCollinsLatitude;
    public double Longitude { get; set; } = MapDefaults.FortCollinsLongitude;
    public double Distance { get; set; } = 12;
    public string Unit { get; set; } = "mi";
    public string Climbing { get; set; } = "flat";
    public string Type { get; set; } = "trail";
}

public sealed class LoopGenerateResponseDto
{
    public string Message { get; set; } = "";
    public bool ElevationUsed { get; set; }
    public bool RoadSnapped { get; set; }
    public bool QuotaLimited { get; set; }
    public string Attribution { get; set; } = LoopNetworkFile.AttributionText;
    public List<LoopCandidateDto> Candidates { get; set; } = [];
}

public sealed class LoopCandidateDto
{
    public string Name { get; set; } = "";
    public string Comments { get; set; } = "";
    public string City { get; set; } = "Fort Collins";
    public string State { get; set; } = "CO";
    public double DistanceMeters { get; set; }
    public double ClimbingMeters { get; set; }
    public double AltitudeGainKm { get; set; }
    public double AltitudeLossKm { get; set; }
    public double BikeLegalShare { get; set; }
    public double TrailShare { get; set; }
    public double LaneShare { get; set; }
    public double BacktrackShare { get; set; }
    public double Score { get; set; }
    public bool ElevationKnown { get; set; }
    public bool RoadSnapped { get; set; }
    public List<double[]> Line { get; set; } = [];
    public List<Waypoint> Waypoints { get; set; } = [];
}

public static class LoopRequests
{
    public static bool TryParse(LoopGenerateRequestDto? dto, out LoopRequest request, out string error)
    {
        request = new LoopRequest(MapDefaults.FortCollinsLatitude, MapDefaults.FortCollinsLongitude, 12 * Distance.MetersPerStatuteMile, ClimbPreference.Flat, LoopRouteType.TrailFirst);
        dto ??= new LoopGenerateRequestDto();
        if (!RoutingLimits.IsPosition(dto.Latitude, dto.Longitude))
        {
            error = "Those coordinates are not on the map.";
            return false;
        }

        var unit = (dto.Unit ?? "mi").Trim().ToLower(CultureInfo.InvariantCulture);
        double meters;
        if (unit is "km" or "kilometer" or "kilometers")
        {
            if (dto.Distance is < 3 or > 65)
            {
                error = "Distance must be between 3 and 65 km.";
                return false;
            }
            meters = dto.Distance * 1000.0;
        }
        else if (unit is "mi" or "mile" or "miles")
        {
            if (dto.Distance is < 2 or > 40)
            {
                error = "Distance must be between 2 and 40 miles.";
                return false;
            }
            meters = dto.Distance * Distance.MetersPerStatuteMile;
        }
        else
        {
            error = "Distance unit must be miles or km.";
            return false;
        }

        var climbing = (dto.Climbing ?? "flat").Trim().ToLower(CultureInfo.InvariantCulture) switch
        {
            "flat" => ClimbPreference.Flat,
            "moderate" or "rolling" => ClimbPreference.Moderate,
            "hilly" or "hill" => ClimbPreference.Hilly,
            _ => (ClimbPreference?)null
        };
        if (climbing is null)
        {
            error = "Climbing must be flat, moderate, or hilly.";
            return false;
        }

        var type = (dto.Type ?? "trail").Trim().ToLower(CultureInfo.InvariantCulture) switch
        {
            "trail" or "trails" or "trail-first" or "trailfirst" => LoopRouteType.TrailFirst,
            "lane" or "lanes" or "bike" or "bike-lanes" or "bikelanes" => LoopRouteType.BikeLanes,
            _ => (LoopRouteType?)null
        };
        if (type is null)
        {
            error = "Route type must be trail or bike lanes.";
            return false;
        }

        request = new LoopRequest(dto.Latitude, dto.Longitude, meters, climbing.Value, type.Value);
        error = "";
        return true;
    }
}
