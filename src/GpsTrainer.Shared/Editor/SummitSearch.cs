namespace GpsTrainer;

public sealed record SummitHit(double ElapsedKm, double AltitudeKm, bool Plateau);

public readonly record struct SummitRoutePoint(double Latitude, double Longitude, double AltitudeKm);

/// <summary>
/// Finds the high point along a route. The 2013 editor asked Google or Bing for 50
/// elevation samples inside ±0.25 km. This keeps a dense pass in that window, then
/// hill-climbs in 50-foot steps until the ground stops rising, including past the window.
/// </summary>
public static class SummitSearch
{
    public const double InitialWindowKm = 0.25;
    public const double DenseSpacingKm = 0.005;
    public const double StepKm = 15.24 / 1000.0;
    public const double SearchCapKm = 2.0;
    public const double PlateauBandKm = 0.001;
    public const double ClimbThresholdKm = 0.00005;
    public const int MaxSteps = 300;

    public static bool IsKnown(double? altitudeKm) =>
        altitudeKm is double value
        && double.IsFinite(value)
        && Math.Abs(value - Altitude.Unknown.Kilometers) > 1e-6;

    public static async Task<SummitHit?> FindAsync(
        double clickElapsedKm,
        double routeLengthKm,
        Func<IReadOnlyList<double>, Task<IReadOnlyList<double?>>> measure)
    {
        if (measure is null)
            throw new ArgumentNullException(nameof(measure));
        if (routeLengthKm <= 0 || !double.IsFinite(routeLengthKm) || !double.IsFinite(clickElapsedKm))
            return null;

        var click = Math.Clamp(clickElapsedKm, 0, routeLengthKm);
        var samples = new Dictionary<long, double?>();

        async Task Load(IEnumerable<double> stations)
        {
            var pending = new List<double>();
            var seen = new HashSet<long>();
            foreach (var station in stations)
            {
                if (!double.IsFinite(station))
                    continue;
                var elapsed = Math.Clamp(station, 0, routeLengthKm);
                var key = Key(elapsed);
                if (samples.ContainsKey(key) || !seen.Add(key))
                    continue;
                pending.Add(elapsed);
            }
            if (pending.Count == 0)
                return;

            var measured = await measure(pending).ConfigureAwait(false);
            for (var i = 0; i < pending.Count; i++)
            {
                double? altitude = measured is not null && i < measured.Count ? measured[i] : null;
                samples[Key(pending[i])] = IsKnown(altitude) ? altitude : null;
            }
        }

        bool TryAlt(double elapsed, out double altitude)
        {
            altitude = 0;
            if (samples.TryGetValue(Key(elapsed), out var exact))
            {
                if (exact is not double known)
                    return false;
                altitude = known;
                return true;
            }
            return TryInterpolate(elapsed, out altitude);
        }

        bool TryInterpolate(double elapsed, out double altitude)
        {
            altitude = 0;
            long? lower = null;
            long? upper = null;
            var lowerY = 0.0;
            var upperY = 0.0;
            foreach (var pair in samples)
            {
                if (pair.Value is not double y)
                    continue;
                var position = pair.Key / 10000.0;
                if (position <= elapsed + 1e-9 && (lower is null || pair.Key > lower))
                {
                    lower = pair.Key;
                    lowerY = y;
                }
                if (position >= elapsed - 1e-9 && (upper is null || pair.Key < upper))
                {
                    upper = pair.Key;
                    upperY = y;
                }
            }
            if (lower is null || upper is null)
                return false;
            if (upper.Value - lower.Value > (DenseSpacingKm + 0.0002) * 10000.0)
                return false;
            foreach (var pair in samples)
            {
                if (pair.Key > lower && pair.Key < upper && pair.Value is null)
                    return false;
            }
            if (upper == lower)
            {
                altitude = lowerY;
                return true;
            }
            var t = (elapsed - lower.Value / 10000.0) / ((upper.Value - lower.Value) / 10000.0);
            altitude = lowerY + (upperY - lowerY) * t;
            return double.IsFinite(altitude);
        }

        bool Covered(double elapsed) => samples.ContainsKey(Key(elapsed)) || TryInterpolate(elapsed, out _);

        double? NextStation(double pos, int direction)
        {
            var next = pos + direction * StepKm;
            if (next < -1e-9 || next > routeLengthKm + 1e-9)
            {
                var end = direction < 0 ? 0 : routeLengthKm;
                if (Math.Abs(end - pos) < 1e-9 || Math.Abs(end - pos) > StepKm + 1e-6)
                    return null;
                next = end;
            }
            if (next < -1e-9 || next > routeLengthKm + 1e-9)
                return null;
            if (Math.Abs(next - click) > SearchCapKm + 1e-6)
                return null;
            return next;
        }

        List<double> Walk(double from, int direction)
        {
            var list = new List<double>();
            var cursor = from;
            for (var n = 0; n < MaxSteps; n++)
            {
                var next = NextStation(cursor, direction);
                if (next is null || Math.Abs(next.Value - cursor) < 1e-9)
                    break;
                list.Add(next.Value);
                cursor = next.Value;
            }
            return list;
        }

        SummitHit? Pinpoint(IReadOnlyList<double> stations)
        {
            var known = new List<(double X, double Y)>();
            foreach (var station in stations)
            {
                if (TryAlt(station, out var altitude))
                    known.Add((station, altitude));
            }
            if (known.Count == 0)
                return null;

            var max = known.Max(item => item.Y);
            var peak = 0;
            for (var i = 1; i < known.Count; i++)
            {
                if (known[i].Y > known[peak].Y)
                    peak = i;
            }
            var start = peak;
            var end = peak;
            while (start > 0
                && max - known[start - 1].Y <= PlateauBandKm
                && known[start].X - known[start - 1].X <= DenseSpacingKm * 2.5)
                start--;
            while (end + 1 < known.Count
                && max - known[end + 1].Y <= PlateauBandKm
                && known[end + 1].X - known[end].X <= DenseSpacingKm * 2.5)
                end++;

            if (end > start)
                return new SummitHit((known[start].X + known[end].X) / 2.0, max, true);

            if (peak > 0 && peak < known.Count - 1)
            {
                var left = known[peak - 1];
                var crest = known[peak];
                var right = known[peak + 1];
                if (crest.X - left.X <= DenseSpacingKm * 1.5
                    && right.X - crest.X <= DenseSpacingKm * 1.5
                    && left.Y < crest.Y
                    && right.Y < crest.Y
                    && TryParabola(left, crest, right, out var vertex, out var height))
                    return new SummitHit(vertex, height, false);
            }
            return new SummitHit(known[peak].X, known[peak].Y, false);
        }

        var window = Stations(click - InitialWindowKm, click + InitialWindowKm, DenseSpacingKm, routeLengthKm);
        await Load(window).ConfigureAwait(false);
        if (Pinpoint(window) is not SummitHit hit)
            return null;

        var pos = hit.ElapsedKm;
        for (var guard = 0; guard < MaxSteps; guard++)
        {
            if (Math.Abs(pos - click) > SearchCapKm + 1e-6 || !TryAlt(pos, out var here))
                break;
            var behind = NextStation(pos, -1);
            var ahead = NextStation(pos, 1);
            var batch = new List<double>();
            if (behind is double back && !Covered(back))
                batch.AddRange(Walk(pos, -1));
            if (ahead is double forward && !Covered(forward))
                batch.AddRange(Walk(pos, 1));
            if (batch.Count > 0)
                await Load(batch).ConfigureAwait(false);

            var behindAlt = 0.0;
            var aheadAlt = 0.0;
            var behindHigher = false;
            var aheadHigher = false;
            if (behind is double behindAt && TryAlt(behindAt, out behindAlt))
                behindHigher = behindAlt > here + ClimbThresholdKm;
            if (ahead is double aheadAt && TryAlt(aheadAt, out aheadAlt))
                aheadHigher = aheadAlt > here + ClimbThresholdKm;
            if (!behindHigher && !aheadHigher)
                break;
            if (behindHigher && aheadHigher)
                pos = behindAlt >= aheadAlt ? behind!.Value : ahead!.Value;
            else
                pos = behindHigher ? behind!.Value : ahead!.Value;
        }

        var local = Stations(pos - StepKm, pos + StepKm, DenseSpacingKm, routeLengthKm);
        await Load(local).ConfigureAwait(false);
        return Pinpoint(local) ?? new SummitHit(pos, hit.AltitudeKm, hit.Plateau);
    }

    public static List<double> Stations(double fromKm, double toKm, double spacingKm, double routeLengthKm)
    {
        if (spacingKm <= 0 || routeLengthKm < 0 || !double.IsFinite(fromKm) || !double.IsFinite(toKm) || !double.IsFinite(routeLengthKm))
            return [];
        var start = Math.Clamp(Math.Min(fromKm, toKm), 0, routeLengthKm);
        var end = Math.Clamp(Math.Max(fromKm, toKm), 0, routeLengthKm);
        var stations = new List<double>();
        if (end - start > 1e-9)
        {
            var count = (int)Math.Floor((end - start) / spacingKm) + 1;
            if (count > 5000)
                count = 5000;
            for (var i = 0; i < count; i++)
            {
                var cursor = start + i * spacingKm;
                if (cursor >= end - spacingKm * 0.25)
                    break;
                stations.Add(cursor);
            }
        }
        stations.Add(end);
        return stations;
    }

    /// <summary>
    /// Point on the route at an elapsed distance. Altitude is interpolated only when
    /// both ends of the segment are known, so an unknown sample is not blended into a real one.
    /// </summary>
    public static SummitRoutePoint PointAt(IReadOnlyList<Waypoint> waypoints, double elapsedKm)
    {
        var line = new List<Waypoint>();
        foreach (var point in waypoints)
        {
            if (point.WaypointType is WaypointType.StartInterval or WaypointType.FinishInterval)
                continue;
            line.Add(point);
        }
        if (line.Count == 0)
            return new SummitRoutePoint(0, 0, Altitude.Unknown.Kilometers);
        if (line.Count == 1 || !double.IsFinite(elapsedKm) || elapsedKm <= 0)
            return new SummitRoutePoint(line[0].Latitude, line[0].Longitude, line[0].AltitudeKm);

        var distance = 0.0;
        for (var i = 1; i < line.Count; i++)
        {
            var previous = line[i - 1];
            var next = line[i];
            var span = GeoMath.DistanceMeters(previous.Latitude, previous.Longitude, next.Latitude, next.Longitude) / 1000.0;
            var end = distance + span;
            var last = i == line.Count - 1;
            if (elapsedKm <= end || last)
            {
                var fraction = span <= 1e-9 ? 0 : (elapsedKm - distance) / span;
                if (last)
                    fraction = Math.Clamp(fraction, 0, 1);
                else
                    fraction = Math.Clamp(fraction, 0, 1);
                var altitude = IsKnown(previous.AltitudeKm) && IsKnown(next.AltitudeKm)
                    ? previous.AltitudeKm + (next.AltitudeKm - previous.AltitudeKm) * fraction
                    : Altitude.Unknown.Kilometers;
                return new SummitRoutePoint(
                    previous.Latitude + (next.Latitude - previous.Latitude) * fraction,
                    previous.Longitude + (next.Longitude - previous.Longitude) * fraction,
                    altitude);
            }
            distance = end;
        }
        var finish = line[^1];
        return new SummitRoutePoint(finish.Latitude, finish.Longitude, finish.AltitudeKm);
    }

    /// <summary>Maps a profile viewBox x (plot area 68..742) onto elapsed kilometers.</summary>
    public static double ProfileElapsedKm(double viewBoxX, double minKm, double maxKm)
    {
        if (!double.IsFinite(viewBoxX) || !double.IsFinite(minKm) || !double.IsFinite(maxKm))
            return double.IsFinite(minKm) ? minKm : 0;
        var t = Math.Clamp((viewBoxX - 68.0) / (742.0 - 68.0), 0, 1);
        return minKm + t * (maxKm - minKm);
    }

    private static long Key(double elapsedKm) =>
        (long)Math.Round(elapsedKm * 10000.0, MidpointRounding.AwayFromZero);

    private static bool TryParabola(
        (double X, double Y) left,
        (double X, double Y) crest,
        (double X, double Y) right,
        out double vertex,
        out double height)
    {
        vertex = crest.X;
        height = crest.Y;
        var x0 = left.X;
        var x1 = crest.X;
        var x2 = right.X;
        var y0 = left.Y;
        var y1 = crest.Y;
        var y2 = right.Y;
        var denom = (x0 - x1) * (x0 - x2) * (x1 - x2);
        if (Math.Abs(denom) < 1e-18)
            return false;
        var a = (x2 * (y1 - y0) + x1 * (y0 - y2) + x0 * (y2 - y1)) / denom;
        var b = (x2 * x2 * (y0 - y1) + x1 * x1 * (y2 - y0) + x0 * x0 * (y1 - y2)) / denom;
        if (!(a < 0))
            return false;
        var x = -b / (2 * a);
        var low = Math.Min(x0, x2);
        var high = Math.Max(x0, x2);
        if (x <= low || x >= high || !double.IsFinite(x))
            return false;
        var c = y0 - a * x0 * x0 - b * x0;
        var y = a * x * x + b * x + c;
        if (!double.IsFinite(y) || y + 1e-9 < Math.Max(y0, Math.Max(y1, y2)))
            return false;
        vertex = x;
        height = y;
        return true;
    }
}
