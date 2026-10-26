namespace GpsTrainer;

/// <summary>
/// Port of 1.44 <c>TurnDetector.Update</c>. While Learning, a GPS bearing change
/// past 45° inserts a turn, and past 155° inserts a turnaround, at the midpoint
/// of the recent track. The first sample only starts the detector.
/// </summary>
public sealed class TurnDetector
{
    public const string TurnDetectorFlag = WaypointSpeech.TurnDetectorFlag;

    private static readonly Distance MinimumDistance = Distance.FromMeters(60);
    private static readonly Distance LookbackDistance = Distance.FromMeters(150);
    private static readonly Speed OneMph = Speed.FromStatuteMilesPerHour(1);
    private const double TurnThreshold = 45.0;
    private const double TurnAroundThreshold = 155.0;

    private readonly Queue<Waypoint> _queue = new();
    private bool _initialized;

    public void Reset()
    {
        _initialized = false;
        _queue.Clear();
    }

    public Waypoint? Update(Waypoint sample, Speed speed)
    {
        if (!_initialized)
        {
            _initialized = true;
            _queue.Clear();
            return null;
        }

        var metersPerSecond = speed.ToMetersPerSecond();
        if (double.IsNaN(metersPerSecond) || metersPerSecond < OneMph.ToMetersPerSecond())
            return null;

        _queue.Enqueue(sample);
        if (_queue.Count == 1
            || sample.ElapsedDistance.Subtract(_queue.Peek().ElapsedDistance).ToMeters() < MinimumDistance.ToMeters())
            return null;

        while (_queue.Count > 0
            && sample.ElapsedDistance.Subtract(_queue.Peek().ElapsedDistance).GreaterThan(LookbackDistance))
            _queue.Dequeue();

        if (_queue.Count == 0)
            return null;

        var head = _queue.Peek();
        var difference = HeadingTurn.Difference(sample.HeadingDegrees, head.HeadingDegrees);
        if (difference > TurnAroundThreshold)
        {
            var around = Middle(_queue, TurnAroundThreshold);
            if (around is not null)
            {
                around.WaypointType = WaypointType.TurnAround;
                around.CourseId = sample.CourseId;
                _queue.Clear();
                return around;
            }
        }

        if (difference > TurnThreshold)
        {
            var turn = Middle(_queue, TurnThreshold);
            if (turn is not null)
            {
                turn.WaypointType = HeadingTurn.Direction(head.HeadingDegrees, sample.HeadingDegrees);
                turn.TextDescription = TurnDetectorFlag;
                turn.CourseId = sample.CourseId;
                _queue.Clear();
                return turn;
            }
        }

        _queue.Dequeue();
        return null;
    }

    private static Waypoint? Middle(Queue<Waypoint> queue, double degrees)
    {
        var list = queue.ToList();
        Trim(list, degrees);
        if (list.Count == 0)
            return null;
        var span = list[^1].ElapsedDistance.Subtract(list[0].ElapsedDistance);
        return InsertFromFinish(list, span.Multiply(0.5), WaypointType.Continue);
    }

    private static void Trim(List<Waypoint> list, double degrees)
    {
        if (list.Count == 0)
            return;
        var start = list[^1].HeadingDegrees;
        var numberToDelete = 0;
        for (var i = list.Count - 2; i >= 0; i--)
        {
            if (i > 0)
                numberToDelete = i - 1;
            if (HeadingTurn.Difference(start, list[i].HeadingDegrees) > degrees)
                break;
        }

        for (var i = 0; i < numberToDelete && list.Count > 0; i++)
            list.RemoveAt(0);
    }

    private static Waypoint? InsertFromFinish(List<Waypoint> waypoints, Distance remaining, WaypointType waypointType)
    {
        if (waypoints.Count == 0)
            return null;
        var previous = waypoints[^1];
        for (var i = waypoints.Count - 2; i >= 0; i--)
        {
            var older = waypoints[i];
            var leg = older.Position.DistanceTo(previous.Position);
            if (leg.GreaterThanOrEqual(remaining))
            {
                var legKm = leg.ToKilometers().Value;
                var ratio = legKm == 0 ? 0 : remaining.ToKilometers().Value / legKm;
                var bearing = GeoMath.InitialBearingDegrees(previous.Latitude, previous.Longitude, older.Latitude, older.Longitude);
                var moved = GeoMath.Translate(previous.Latitude, previous.Longitude, bearing, remaining.ToKilometers().Value);
                var inserted = new Waypoint
                {
                    CourseId = previous.CourseId,
                    WaypointType = waypointType,
                    Latitude = moved.Latitude,
                    Longitude = moved.Longitude,
                    AltitudeKm = previous.AltitudeKm + (older.AltitudeKm - previous.AltitudeKm) * ratio,
                    HeadingDegrees = GeoMath.InitialBearingDegrees(moved.Latitude, moved.Longitude, previous.Latitude, previous.Longitude),
                    TimestampTicks = (long)(previous.TimestampTicks + (older.TimestampTicks - previous.TimestampTicks) * ratio),
                    ElapsedDistanceKm = previous.ElapsedDistance.Add(older.ElapsedDistance.Subtract(previous.ElapsedDistance).Multiply(ratio)).ToKilometers().Value
                };
                waypoints.Insert(i + 1, inserted);
                return inserted;
            }

            remaining = remaining.Subtract(leg);
            previous = older;
        }

        return null;
    }
}

/// <summary>Port of <c>Utilities.Difference</c> and <c>Utilities.GetDirection</c>.</summary>
public static class HeadingTurn
{
    public static double Difference(double a, double b)
    {
        if (Math.Abs(a - b) > 180.0)
        {
            a = (a + 180.0) % 360.0;
            b = (b + 180.0) % 360.0;
        }

        return Math.Abs(a - b);
    }

    public static WaypointType Direction(double fromHeading, double toHeading)
    {
        var a = fromHeading;
        var b = toHeading;
        if (Math.Abs(a - b) > 180.0)
        {
            a = (a + 180.0) % 360.0;
            b = (b + 180.0) % 360.0;
        }

        return b > a ? WaypointType.TurnRight : WaypointType.TurnLeft;
    }
}
