namespace GpsTrainer;

/// <summary>
/// Port of <c>DataLayer/Utilities.InterpolatePosition</c> and <c>Intersection</c>.
/// A miss is <see cref="DateTime.MinValue"/>. The 30 m road width and the 150 m
/// midpoint split are the original constants. Angle and intersection are combined
/// with the original operator precedence.
/// </summary>
public static class TrackInterpolation
{
    public const double RoadWidthMeters = 30;
    public const double SplitAboveMeters = 150;

    public static GeoPosition Intersection(GeoPosition prev, GeoPosition curr, GeoPosition target)
    {
        var magnitude = Magnitude(prev, curr);
        if (magnitude < 0.00000001)
            return GeoPosition.Empty;

        var u = ((target.Longitude - prev.Longitude) * (curr.Longitude - prev.Longitude))
            + ((target.Latitude - prev.Latitude) * (curr.Latitude - prev.Latitude));
        u /= magnitude * magnitude;
        if (u < .00001 || u > 1)
            return GeoPosition.Empty;

        return new GeoPosition(
            prev.Latitude + u * (curr.Latitude - prev.Latitude),
            prev.Longitude + u * (curr.Longitude - prev.Longitude),
            false);
    }

    public static DateTime Interpolate(GeoPosition prevPosition, GeoPosition curPosition, GeoPosition targetPosition, DateTime prevTime, DateTime curTime)
    {
        var prevToCurDist = prevPosition.DistanceTo(curPosition).ToMeters();
        var prevToTarDist = prevPosition.DistanceTo(targetPosition).ToMeters();
        var curToTarDist = curPosition.DistanceTo(targetPosition).ToMeters();

        if (prevToCurDist == 0.0)
            return DateTime.MinValue;
        if (prevToTarDist == 0.0)
            return prevTime;
        if (curToTarDist == 0.0)
            return curTime;
        if (prevToCurDist + curToTarDist == prevToTarDist)
            return DateTime.MinValue;

        var v2 = (prevToCurDist * prevToCurDist + curToTarDist * curToTarDist - prevToTarDist * prevToTarDist)
            / (2 * prevToCurDist * prevToTarDist);
        if (v2 < -1) v2 = -1;
        if (v2 > 1) v2 = 1;
        var angleDegrees = Math.Acos(v2) * 180.0 / Math.PI;
        var intersection = Intersection(prevPosition, curPosition, targetPosition);
        var triggered = angleDegrees < 90.0 && curToTarDist < RoadWidthMeters
            || intersection.DistanceTo(targetPosition).ToMeters() < RoadWidthMeters;

        if (triggered)
        {
            var p = intersection;
            if (p.IsEmpty)
            {
                var bearingBack = GeoMath.InitialBearingDegrees(curPosition.Latitude, curPosition.Longitude, prevPosition.Latitude, prevPosition.Longitude);
                var extended = GeoMath.Translate(prevPosition.Latitude, prevPosition.Longitude, bearingBack, 0.040);
                var prevPrev = new GeoPosition(extended.Latitude, extended.Longitude, false);
                p = Intersection(prevPrev, curPosition, targetPosition);
                if (p.IsEmpty)
                    return prevTime;

                var spanMeters = prevPrev.DistanceTo(curPosition).ToMeters();
                var ratio = spanMeters == 0.0 ? 1.0 : prevPosition.DistanceTo(p).ToMeters() / spanMeters;
                return AddRatio(prevTime, curTime, ratio);
            }
            else
            {
                var ratio = prevPosition.DistanceTo(p).ToMeters() / prevToCurDist;
                return AddRatio(prevTime, curTime, ratio);
            }
        }

        if (prevToCurDist > SplitAboveMeters)
        {
            var bearing = GeoMath.InitialBearingDegrees(prevPosition.Latitude, prevPosition.Longitude, curPosition.Latitude, curPosition.Longitude);
            var middle = GeoMath.Translate(prevPosition.Latitude, prevPosition.Longitude, bearing, (prevToCurDist / 2) / 1000.0);
            var middlePos = new GeoPosition(middle.Latitude, middle.Longitude, false);
            var middleTime = prevTime.AddTicks((curTime.Ticks - prevTime.Ticks) / 2);
            var first = Interpolate(prevPosition, middlePos, targetPosition, prevTime, middleTime);
            if (first > curTime)
                throw new InvalidOperationException("InterpolatePosition");
            if (first != DateTime.MinValue)
                return first;
            var second = Interpolate(middlePos, curPosition, targetPosition, middleTime, curTime);
            if (second > curTime)
                throw new InvalidOperationException("InterpolatePosition");
            return second;
        }

        return DateTime.MinValue;
    }

    private static DateTime AddRatio(DateTime prevTime, DateTime curTime, double ratio)
    {
        var ts = curTime - prevTime;
        var interpolated = prevTime.AddTicks((long)Math.Round(ts.Ticks * ratio));
        if (interpolated > curTime)
            throw new InvalidOperationException("InterpolatePosition");
        return interpolated;
    }

    private static double Magnitude(GeoPosition a, GeoPosition b)
    {
        var dLon = b.Longitude - a.Longitude;
        var dLat = b.Latitude - a.Latitude;
        return Math.Sqrt(dLon * dLon + dLat * dLat);
    }
}
