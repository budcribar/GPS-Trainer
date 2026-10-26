namespace GpsTrainer;

/// <summary>
/// Turns a generated loop into an unsaved course. Summit pins reuse
/// <see cref="SummitSearch"/>. Feed zones sit on the route about every 10 miles,
/// labeled with a nearby trailhead or park when one is within 400 m.
/// </summary>
public static class LoopCourseBuilder
{
    public const double FeedSpacingMeters = 10 * Distance.MetersPerStatuteMile;
    public const double FeedPlaceMeters = 400;

    public static async Task<List<Waypoint>> BuildAsync(
        LoopRoute route,
        IReadOnlyList<NetworkPlace> places,
        CancellationToken cancellationToken = default)
    {
        var points = route.Points;
        if (points.Count < 2)
            return [];
        var elapsed = LoopGeometry.ElapsedMeters(points);
        var length = elapsed[^1];
        if (length < 50)
            return [];

        var summits = await FindSummitsAsync(points, elapsed, cancellationToken).ConfigureAwait(false);
        var feeds = PlaceFeeds(points, elapsed, places);
        var kept = Thin(points, elapsed, 120, 28);
        var markers = new List<Marker>();
        foreach (var index in kept)
            markers.Add(new Marker(elapsed[index], points[index], WaypointType.Continue, null));
        foreach (var summit in summits)
            Overlay(markers, summit.ElapsedMeters, WaypointType.Summit, "Summit", summit.AltitudeKm, points, elapsed);
        foreach (var feed in feeds)
            Overlay(markers, feed.ElapsedMeters, WaypointType.FeedZone, feed.Label, null, points, elapsed);

        markers.Sort((left, right) => left.ElapsedMeters.CompareTo(right.ElapsedMeters));
        var compact = new List<Marker>();
        foreach (var marker in markers)
        {
            if (compact.Count > 0 && marker.ElapsedMeters - compact[^1].ElapsedMeters < 25)
            {
                if (Rank(marker.Type) > Rank(compact[^1].Type))
                    compact[^1] = marker;
                continue;
            }
            compact.Add(marker);
        }
        if (compact.Count < 2)
            return [];

        compact[0] = compact[0] with { Type = WaypointType.Start, Text = null, ElapsedMeters = 0 };
        compact[^1] = compact[^1] with { Type = WaypointType.Finish, Text = null, ElapsedMeters = length };
        for (var i = 1; i < compact.Count - 1; i++)
        {
            if (compact[i].Type is WaypointType.Summit or WaypointType.FeedZone)
                continue;
            var turn = Turn(compact, i);
            compact[i] = compact[i] with { Type = GpxCourse.ClassifyTurn(turn) };
        }

        var waypoints = new List<Waypoint>(compact.Count);
        for (var i = 0; i < compact.Count; i++)
        {
            var marker = compact[i];
            var next = i + 1 < compact.Count ? compact[i + 1].Point : marker.Point;
            var heading = GeoMath.InitialBearingDegrees(marker.Point.Latitude, marker.Point.Longitude, next.Latitude, next.Longitude);
            waypoints.Add(new Waypoint
            {
                TimestampTicks = 1_000_000 + i,
                Latitude = marker.Point.Latitude,
                Longitude = marker.Point.Longitude,
                AltitudeKm = marker.AltitudeKm ?? marker.Point.AltitudeKm,
                ElapsedDistanceKm = i == 0 ? 0 : marker.ElapsedMeters / 1000.0,
                HeadingDegrees = heading,
                WaypointType = marker.Type,
                TextDescription = marker.Text
            });
        }
        if (waypoints.Count >= 2)
            waypoints[^1].HeadingDegrees = waypoints[^2].HeadingDegrees;
        return waypoints;
    }

    public static async Task<IReadOnlyList<(double ElapsedMeters, double AltitudeKm)>> FindSummitsAsync(
        IReadOnlyList<GeoCoord> points,
        IReadOnlyList<double> elapsedMeters,
        CancellationToken cancellationToken = default)
    {
        if (points.Count < 3 || !points.Any(point => point.HasAltitude))
            return [];
        var lengthKm = elapsedMeters[^1] / 1000.0;
        var seeds = new List<(double ElapsedKm, double Prominence)>();
        for (var i = 1; i < points.Count - 1; i++)
        {
            if (!points[i].HasAltitude)
                continue;
            var elapsedKm = elapsedMeters[i] / 1000.0;
            if (elapsedKm < 0.2 || lengthKm - elapsedKm < 0.2)
                continue;
            double? left = null;
            double? right = null;
            var max = points[i].AltitudeKm;
            var isMax = true;
            for (var j = 0; j < points.Count; j++)
            {
                if (!points[j].HasAltitude)
                    continue;
                var delta = Math.Abs(elapsedMeters[j] - elapsedMeters[i]) / 1000.0;
                if (delta > 0.55)
                    continue;
                if (points[j].AltitudeKm > max + 1e-6)
                    isMax = false;
                if (elapsedMeters[j] < elapsedMeters[i])
                    left = left is null ? points[j].AltitudeKm : Math.Min(left.Value, points[j].AltitudeKm);
                else if (elapsedMeters[j] > elapsedMeters[i])
                    right = right is null ? points[j].AltitudeKm : Math.Min(right.Value, points[j].AltitudeKm);
            }
            if (!isMax || left is null || right is null)
                continue;
            var prominence = points[i].AltitudeKm - Math.Max(left.Value, right.Value);
            if (prominence >= 0.008)
                seeds.Add((elapsedKm, prominence));
        }

        var hits = new List<(double ElapsedMeters, double AltitudeKm, double Prominence)>();
        foreach (var seed in seeds.OrderByDescending(seed => seed.Prominence).Take(6))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hit = await SummitSearch.FindAsync(seed.ElapsedKm, lengthKm, stations =>
            {
                IReadOnlyList<double?> measured = stations
                    .Select(station => LoopGeometry.InterpolateAltitudeKm(points, elapsedMeters, station))
                    .ToList();
                return Task.FromResult(measured);
            }).ConfigureAwait(false);
            if (hit is null || !SummitSearch.IsKnown(hit.AltitudeKm))
                continue;
            if (hit.ElapsedKm < 0.15 || lengthKm - hit.ElapsedKm < 0.15)
                continue;
            hits.Add((hit.ElapsedKm * 1000.0, hit.AltitudeKm, seed.Prominence));
        }

        hits.Sort((left, right) => right.Prominence.CompareTo(left.Prominence));
        var kept = new List<(double ElapsedMeters, double AltitudeKm)>();
        foreach (var hit in hits)
        {
            if (kept.Any(other => Math.Abs(other.ElapsedMeters - hit.ElapsedMeters) < 350))
                continue;
            kept.Add((hit.ElapsedMeters, hit.AltitudeKm));
            if (kept.Count == 4)
                break;
        }
        return kept;
    }

    public static IReadOnlyList<(double ElapsedMeters, string Label)> PlaceFeeds(
        IReadOnlyList<GeoCoord> points,
        IReadOnlyList<double> elapsedMeters,
        IReadOnlyList<NetworkPlace> places)
    {
        var length = elapsedMeters.Count == 0 ? 0 : elapsedMeters[^1];
        var stations = new List<double>();
        for (var distance = FeedSpacingMeters; distance < length - 0.5 * Distance.MetersPerStatuteMile; distance += FeedSpacingMeters)
            stations.Add(distance);
        if (stations.Count == 0 && length >= 8 * Distance.MetersPerStatuteMile)
            stations.Add(length * 0.5);

        var feeds = new List<(double ElapsedMeters, string Label)>();
        foreach (var station in stations)
        {
            var point = LoopGeometry.AtDistance(points, elapsedMeters, station);
            var label = "Feed zone";
            var best = FeedPlaceMeters;
            foreach (var place in places)
            {
                var distance = GeoMath.DistanceMeters(point.Latitude, point.Longitude, place.Latitude, place.Longitude);
                if (distance < best)
                {
                    best = distance;
                    label = string.IsNullOrWhiteSpace(place.Name) ? "Feed zone" : place.Name;
                }
            }
            feeds.Add((station, label));
        }
        return feeds;
    }

    private static List<int> Thin(IReadOnlyList<GeoCoord> points, IReadOnlyList<double> elapsed, double minMeters, double turnDegrees)
    {
        var keep = new List<int> { 0 };
        for (var i = 1; i < points.Count - 1; i++)
        {
            var since = elapsed[i] - elapsed[keep[^1]];
            var turn = i > 0 && i < points.Count - 1 ? HeadingChange(points[i - 1], points[i], points[i + 1]) : 0;
            if (since >= minMeters || Math.Abs(turn) >= turnDegrees)
                keep.Add(i);
        }
        keep.Add(points.Count - 1);
        if (keep.Count > 220 && minMeters < 400)
            return Thin(points, elapsed, minMeters * 1.5, turnDegrees);
        return keep;
    }

    private static void Overlay(
        List<Marker> markers,
        double elapsedMeters,
        WaypointType type,
        string text,
        double? altitudeKm,
        IReadOnlyList<GeoCoord> points,
        IReadOnlyList<double> elapsed)
    {
        for (var i = 0; i < markers.Count; i++)
        {
            if (Math.Abs(markers[i].ElapsedMeters - elapsedMeters) > 30)
                continue;
            if (Rank(type) >= Rank(markers[i].Type))
            {
                var point = markers[i].Point;
                if (altitudeKm is double altitude)
                    point = new GeoCoord(point.Latitude, point.Longitude, altitude);
                markers[i] = markers[i] with { Type = type, Text = text, Point = point, AltitudeKm = altitudeKm };
            }
            return;
        }
        var at = LoopGeometry.AtDistance(points, elapsed, elapsedMeters);
        if (altitudeKm is double known)
            at = new GeoCoord(at.Latitude, at.Longitude, known);
        markers.Add(new Marker(elapsedMeters, at, type, text, altitudeKm));
    }

    private static int Rank(WaypointType type) => type switch
    {
        WaypointType.Summit => 3,
        WaypointType.FeedZone => 2,
        WaypointType.Start or WaypointType.Finish => 4,
        _ => 1
    };

    private static double Turn(IReadOnlyList<Marker> markers, int index)
    {
        var previous = markers[index - 1].Point;
        var current = markers[index].Point;
        var next = markers[index + 1].Point;
        return HeadingChange(previous, current, next);
    }

    private static double HeadingChange(GeoCoord previous, GeoCoord current, GeoCoord next)
    {
        var incoming = GeoMath.InitialBearingDegrees(previous.Latitude, previous.Longitude, current.Latitude, current.Longitude);
        var outgoing = GeoMath.InitialBearingDegrees(current.Latitude, current.Longitude, next.Latitude, next.Longitude);
        var delta = outgoing - incoming;
        while (delta > 180)
            delta -= 360;
        while (delta < -180)
            delta += 360;
        return delta;
    }

    private sealed record Marker(double ElapsedMeters, GeoCoord Point, WaypointType Type, string? Text, double? AltitudeKm = null);
}
