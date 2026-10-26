using System.Globalization;
using System.Text.Json;
using GpsTrainer;
using GpsTrainer.Server.Routing;

namespace GpsTrainer.Server.Loops;

public sealed class LoopGenerationService
{
    public const int MaxOrsCalls = 4;
    private readonly FortCollinsNetworkCatalog _network;
    private readonly IOpenRouteServiceClient _client;
    private readonly OpenRouteServiceEndpoints _endpoints;
    private readonly ElevationLineCache _elevationCache;
    private readonly LoopSnapCache _snapCache;
    private readonly OrsQuotaGate _quota;

    public LoopGenerationService(
        FortCollinsNetworkCatalog network,
        IOpenRouteServiceClient client,
        OpenRouteServiceEndpoints endpoints,
        ElevationLineCache elevationCache,
        LoopSnapCache snapCache,
        OrsQuotaGate quota)
    {
        _network = network;
        _client = client;
        _endpoints = endpoints;
        _elevationCache = elevationCache;
        _snapCache = snapCache;
        _quota = quota;
    }

    public async Task<LoopGenerateResponseDto> GenerateAsync(
        LoopRequest request,
        bool allowOrs,
        string? apiKey,
        CancellationToken cancellationToken)
    {
        LoopGraph graph;
        LoopNetwork network;
        try
        {
            network = _network.Network;
            graph = _network.Graph;
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException)
        {
            return new LoopGenerateResponseDto
            {
                Message = "The Fort Collins network is not available.",
                Attribution = LoopNetworkFile.AttributionText
            };
        }

        var generated = LoopGenerator.Generate(graph, request, elevationMeters: null, take: 8);
        var calls = 0;
        var quotaHit = _quota.IsBlocked;
        var elevationUsed = false;
        var snappedAny = false;
        var enriched = new List<LoopRoute>();
        foreach (var route in generated)
        {
            var current = route;
            if (allowOrs && !string.IsNullOrWhiteSpace(apiKey) && !quotaHit && calls < MaxOrsCalls
                && request.Type == LoopRouteType.BikeLanes && enriched.Count(item => item.RoadSnapped) < 2)
            {
                var snap = await TrySnapAsync(graph, current, apiKey, cancellationToken).ConfigureAwait(false);
                calls += snap.Calls;
                if (snap.Quota)
                    quotaHit = true;
                if (snap.Route is not null)
                {
                    current = snap.Route;
                    snappedAny = true;
                    elevationUsed |= current.ElevationKnown;
                }
            }

            if (allowOrs && !string.IsNullOrWhiteSpace(apiKey) && !quotaHit && calls < MaxOrsCalls && !current.ElevationKnown)
            {
                var elevated = await TryElevationAsync(current, apiKey, cancellationToken).ConfigureAwait(false);
                calls += elevated.Calls;
                if (elevated.Quota)
                    quotaHit = true;
                if (elevated.Route is not null)
                {
                    current = elevated.Route;
                    elevationUsed = true;
                }
            }

            enriched.Add(LoopScorer.Score(current, request));
        }

        if (quotaHit)
            _quota.Block();

        var top = Pick(enriched, request, 3);
        var candidates = new List<LoopCandidateDto>();
        foreach (var route in top)
        {
            var waypoints = await LoopCourseBuilder.BuildAsync(route, network.Places, cancellationToken).ConfigureAwait(false);
            if (waypoints.Count < 2)
                continue;
            var gain = ElevationProfile.GainLoss(waypoints);
            var line = LoopGeometry.Sample(route.Points, 700);
            candidates.Add(new LoopCandidateDto
            {
                Name = route.Name,
                Comments = Comments(route, gain.GainKm),
                City = "Fort Collins",
                State = "CO",
                DistanceMeters = route.DistanceMeters,
                ClimbingMeters = gain.GainKm * 1000.0,
                AltitudeGainKm = gain.GainKm,
                AltitudeLossKm = gain.LossKm,
                BikeLegalShare = route.BikeLegalShare,
                TrailShare = route.TrailShare,
                LaneShare = route.LaneShare,
                BacktrackShare = route.BacktrackShare,
                Score = route.Score,
                ElevationKnown = route.ElevationKnown,
                RoadSnapped = route.RoadSnapped,
                Line = line.Select(point => new[] { point.Latitude, point.Longitude }).ToList(),
                Waypoints = waypoints
            });
        }

        return new LoopGenerateResponseDto
        {
            Message = Message(allowOrs, string.IsNullOrWhiteSpace(apiKey), quotaHit, elevationUsed, candidates.Count),
            ElevationUsed = elevationUsed,
            RoadSnapped = snappedAny,
            QuotaLimited = quotaHit,
            Attribution = LoopNetworkFile.AttributionText,
            Candidates = candidates
        };
    }

    private static List<LoopRoute> Pick(List<LoopRoute> routes, LoopRequest request, int take)
    {
        routes.Sort((left, right) => LoopScorer.SortBias(right, request).CompareTo(LoopScorer.SortBias(left, request)));
        var chosen = new List<LoopRoute>();
        foreach (var route in routes)
        {
            if (chosen.Any(other => Similar(route, other)))
                continue;
            chosen.Add(route);
            if (chosen.Count == take)
                break;
        }
        return chosen;
    }

    private static bool Similar(LoopRoute left, LoopRoute right)
    {
        var a = left.EdgeIds.ToHashSet();
        var b = right.EdgeIds.ToHashSet();
        if (a.Count == 0 || b.Count == 0)
            return false;
        var intersection = a.Intersect(b).Count();
        var union = a.Count + b.Count - intersection;
        return (double)intersection / union >= 0.72;
    }

    private async Task<(LoopRoute? Route, int Calls, bool Quota)> TryElevationAsync(
        LoopRoute route, string apiKey, CancellationToken cancellationToken)
    {
        var sampled = LoopGeometry.Sample(route.Points, 160);
        if (sampled.Count < 2)
            return (null, 0, false);
        var samples = sampled.Select(point => new ElevationSampleDto
        {
            Latitude = point.Latitude,
            Longitude = point.Longitude
        }).ToList();
        var key = _elevationCache.KeyFor(samples);
        if (_elevationCache.TryGet(key, out var cached) && cached.Length == samples.Count)
            return (ApplyElevation(route, sampled, cached), 0, false);

        var body = JsonSerializer.Serialize(new
        {
            format_in = "polyline",
            format_out = "polyline",
            geometry = samples.Select(point => new[] { point.Longitude, point.Latitude }).ToArray()
        });
        OrsHttpResult result;
        try
        {
            result = await _client.PostJsonAsync(_endpoints.ElevationLine(), body, apiKey, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return (null, 1, false);
        }
        catch (TaskCanceledException)
        {
            return (null, 1, false);
        }

        if (OrsQuotaGate.IsQuota(result.StatusCode, result.Body))
            return (null, 1, true);
        if (result.StatusCode is < 200 or >= 300)
            return (null, 1, false);
        var parsed = OrsGeoJson.ParseElevation(result.Body);
        if (!parsed.Ok || parsed.Meters.Length != samples.Count)
            return (null, 1, false);
        var kilometers = parsed.Meters.Select(meters => meters / 1000.0).ToArray();
        _elevationCache.Store(key, kilometers);
        return (ApplyElevation(route, sampled, kilometers), 1, false);
    }

    private static LoopRoute ApplyElevation(LoopRoute route, IReadOnlyList<GeoCoord> sampled, IReadOnlyList<double> altitudeKm)
    {
        var stamps = sampled.Select((point, index) => new GeoCoord(
            point.Latitude,
            point.Longitude,
            index < altitudeKm.Count && SummitSearch.IsKnown(altitudeKm[index]) ? altitudeKm[index] : Altitude.Unknown.Kilometers)).ToList();
        var stampElapsed = LoopGeometry.ElapsedMeters(stamps);
        var elapsed = LoopGeometry.ElapsedMeters(route.Points);
        var points = new List<GeoCoord>(route.Points.Count);
        for (var i = 0; i < route.Points.Count; i++)
        {
            var at = LoopGeometry.AtDistance(stamps, stampElapsed, elapsed[i] * (stampElapsed[^1] / Math.Max(elapsed[^1], 1)));
            points.Add(new GeoCoord(route.Points[i].Latitude, route.Points[i].Longitude, at.AltitudeKm));
        }
        var known = points.Any(point => point.HasAltitude);
        return route with
        {
            Points = points,
            ElevationKnown = known,
            ClimbingMeters = known ? LoopGeometry.ClimbingMeters(points) : 0
        };
    }

    private async Task<(LoopRoute? Route, int Calls, bool Quota)> TrySnapAsync(
        LoopGraph graph, LoopRoute route, string apiKey, CancellationToken cancellationToken)
    {
        var elapsed = LoopGeometry.ElapsedMeters(route.Points);
        if (elapsed.Length == 0 || elapsed[^1] < 100)
            return (null, 0, false);
        var via = new List<GeoCoord>();
        foreach (var fraction in new[] { 0.0, 0.25, 0.5, 0.75 })
            via.Add(LoopGeometry.AtDistance(route.Points, elapsed, elapsed[^1] * fraction));
        via.Add(route.Points[0]);
        var cacheKey = _snapCache.KeyFor(via);
        if (_snapCache.TryGet(cacheKey, out var cached))
            return (AcceptSnap(graph, route, cached), 0, false);

        var body = JsonSerializer.Serialize(new
        {
            coordinates = via.Select(point => new[] { point.Longitude, point.Latitude }).ToArray(),
            instructions = false,
            elevation = true
        });
        OrsHttpResult result;
        try
        {
            result = await _client.PostJsonAsync(_endpoints.Directions(OrsProfile.CyclingRoad), body, apiKey, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return (null, 1, false);
        }
        catch (TaskCanceledException)
        {
            return (null, 1, false);
        }

        if (OrsQuotaGate.IsQuota(result.StatusCode, result.Body))
            return (null, 1, true);
        if (result.StatusCode is < 200 or >= 300)
            return (null, 1, false);
        var snapped = ReadLine(result.Body);
        if (snapped.Count < 2)
            return (null, 1, false);
        var accepted = AcceptSnap(graph, route, snapped);
        if (accepted is null)
            return (null, 1, false);
        _snapCache.Store(cacheKey, accepted.Points.ToArray());
        return (accepted, 1, false);
    }

    private static LoopRoute? AcceptSnap(LoopGraph graph, LoopRoute route, IReadOnlyList<GeoCoord> snapped)
    {
        var length = LoopGeometry.LengthMeters(snapped);
        if (route.DistanceMeters <= 0)
            return null;
        var ratio = length / route.DistanceMeters;
        if (ratio < 0.70 || ratio > 1.35)
            return null;
        var samples = LoopGeometry.Sample(snapped, 40);
        var offsets = samples.Select(point => LoopGeometry.Closest(route.Points, point.Latitude, point.Longitude).OffsetMeters).OrderBy(value => value).ToList();
        if (offsets.Count == 0)
            return null;
        var median = offsets[offsets.Count / 2];
        if (median > 160 || offsets[^1] > 500)
            return null;

        var legal = 0;
        var trail = 0;
        var lane = 0;
        var probed = LoopGeometry.Sample(snapped, 50);
        foreach (var point in probed)
        {
            var hit = LoopGeometry.Closest(route.Points, point.Latitude, point.Longitude);
            if (hit.OffsetMeters > 55)
                continue;
            legal++;
            var facility = FacilityAt(graph, route, hit.AlongMeters);
            if (facility == LoopFacility.Trail)
                trail++;
            else if (facility == LoopFacility.Lane)
                lane++;
        }
        var count = Math.Max(probed.Count, 1);
        var known = snapped.Any(point => point.HasAltitude);
        return route with
        {
            Points = snapped.ToList(),
            DistanceMeters = length,
            RoadSnapped = true,
            ElevationKnown = known || route.ElevationKnown,
            ClimbingMeters = known ? LoopGeometry.ClimbingMeters(snapped) : route.ClimbingMeters,
            BikeLegalShare = (double)legal / count,
            TrailShare = (double)trail / count,
            LaneShare = (double)lane / count
        };
    }

    private static LoopFacility FacilityAt(LoopGraph graph, LoopRoute route, double alongMeters)
    {
        double cursor = 0;
        LoopFacility last = LoopFacility.Lane;
        foreach (var id in route.EdgeIds)
        {
            if ((uint)id >= (uint)graph.Edges.Count)
                continue;
            var edge = graph.Edges[id];
            last = edge.Facility;
            if (alongMeters <= cursor + edge.LengthMeters)
                return edge.Facility;
            cursor += edge.LengthMeters;
        }
        return last;
    }

    private static List<GeoCoord> ReadLine(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("features", out var features) || features.GetArrayLength() == 0)
                return [];
            var feature = features[0];
            if (!feature.TryGetProperty("geometry", out var geometry) || !geometry.TryGetProperty("coordinates", out var coordinates))
                return [];
            var line = new List<GeoCoord>();
            foreach (var coordinate in coordinates.EnumerateArray())
            {
                if (coordinate.GetArrayLength() < 2)
                    continue;
                var altitude = coordinate.GetArrayLength() >= 3 && coordinate[2].ValueKind == JsonValueKind.Number
                    ? coordinate[2].GetDouble() / 1000.0
                    : Altitude.Unknown.Kilometers;
                line.Add(new GeoCoord(coordinate[1].GetDouble(), coordinate[0].GetDouble(), altitude));
            }
            return line;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string Comments(LoopRoute route, double gainKm)
    {
        var miles = route.DistanceMeters / Distance.MetersPerStatuteMile;
        var climbFeet = gainKm * 1000.0 / 0.3048;
        return string.Create(CultureInfo.InvariantCulture, $"{route.Name}. About {miles:0.0} mi with {climbFeet:0} ft of climbing. {route.BikeLegalShare * 100:0}% on bike paths or lanes, {route.TrailShare * 100:0}% trail, {route.LaneShare * 100:0}% bike lane. {LoopNetworkFile.AttributionText} Elevation from openrouteservice by HeiGIT when it is available.");
    }

    private static string Message(bool allowOrs, bool missingKey, bool quota, bool elevationUsed, int count)
    {
        if (count == 0)
            return "No loop was found near that start. Move it closer to a trail or a bike lane.";
        if (!allowOrs)
            return "Sign in for elevation and road snapping. This preview uses the Fort Collins network only. Save from the editor if you want to keep the course.";
        if (missingKey)
            return "Elevation is not configured, so climbing and summit pins were left off. Save from the editor if you want to keep the course.";
        if (quota)
            return "OpenRouteService quota was reached, so elevation or road snapping was skipped. Save from the editor if you want to keep the course.";
        if (!elevationUsed)
            return "Elevation was not available, so climbing is unknown. Save from the editor if you want to keep the course.";
        return "Open a loop in the editor. Nothing is saved until you save the course.";
    }
}
