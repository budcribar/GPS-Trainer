namespace GpsTrainer;

/// <summary>
/// The points of one ride, kept so a finished race can be the ghost next time.
/// 1.44 skipped a fix that had not moved more than five feet, and a second fix
/// with the same timestamp.
/// </summary>
public sealed class RideRecorder
{
    public static readonly double MinimumMoveMeters = Distance.FromFeet(5).ToMeters();

    private readonly List<TrackPoint> _points = [];
    private TrackPoint? _last;

    public IReadOnlyList<TrackPoint> Points => _points;

    public void Clear()
    {
        _points.Clear();
        _last = null;
    }

    public bool Record(double latitude, double longitude, DateTime utc)
    {
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude))
            return false;
        var ticks = SoapDateTime.ToUnixTicks(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
        if (_last is not null && _last.TimestampTicks == ticks)
            return false;
        if (_last is not null
            && GeoMath.DistanceMeters(latitude, longitude, _last.Latitude, _last.Longitude) <= MinimumMoveMeters)
            return false;

        var point = new TrackPoint
        {
            Latitude = latitude,
            Longitude = longitude,
            TimestampTicks = ticks
        };
        _points.Add(point);
        _last = point;
        return true;
    }
}
