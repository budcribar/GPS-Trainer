namespace GpsTrainer;

/// <summary>
/// Appends a map click to a course. Road-following stores every routed point as a waypoint.
/// Undo removes the whole last segment, including the intermediate road points.
/// </summary>
public sealed class CourseSketch
{
    private readonly Stack<UndoStep> _undo = new();
    private long _ticks = DateTime.UtcNow.Ticks;

    public List<Waypoint> Waypoints { get; } = [];
    public string? Notice { get; private set; }
    public bool CanUndo => _undo.Count > 0;

    public Waypoint? LastRoutePoint() =>
        Waypoints.LastOrDefault(w => w.WaypointType is not WaypointType.StartInterval and not WaypointType.FinishInterval);

    public void Replace(IReadOnlyList<Waypoint> waypoints)
    {
        Waypoints.Clear();
        Waypoints.AddRange(waypoints);
        var floor = Waypoints.Count == 0 ? 0 : Waypoints.Max(w => w.TimestampTicks);
        if (_ticks < floor)
            _ticks = floor;
    }

    public void AddClick(double latitude, double longitude, WaypointType requestedType, bool followRoads, RouteResponseDto? route)
    {
        Notice = null;
        if (requestedType is WaypointType.StartInterval or WaypointType.FinishInterval)
        {
            var line = IntervalCourse.RouteLine(Waypoints);
            if (line.Count >= 2)
            {
                var hit = IntervalCourse.Nearest(line, latitude, longitude);
                AddOne(hit.Latitude, hit.Longitude, requestedType, hit.ElapsedKm);
                return;
            }
        }

        var previous = LastRoutePoint();
        if (followRoads && previous is not null)
        {
            if (route is { Routed: true, Points.Count: > 0 })
            {
                AddRoute(route, requestedType, latitude, longitude);
                return;
            }

            var message = string.IsNullOrWhiteSpace(route?.Message) ? "Routing failed." : route!.Message.Trim();
            if (route is { StraightFallback: false } || !RoutingLimits.AllowsStraightFallback(message))
            {
                Notice = message;
                return;
            }

            Notice = message.Contains("straight", StringComparison.OrdinalIgnoreCase)
                ? message
                : message.TrimEnd('.') + ". A straight segment was added.";
            AddOne(latitude, longitude, requestedType, 0);
            return;
        }

        AddOne(latitude, longitude, requestedType, 0);
    }

    public bool Undo()
    {
        if (_undo.Count == 0)
            return false;
        var step = _undo.Pop();
        if (step.Restore is not null)
        {
            Waypoints.Clear();
            Waypoints.AddRange(step.Restore.Select(Clone));
        }
        else if (step.RemoveTicks is not null)
        {
            var ticks = step.RemoveTicks.ToHashSet();
            Waypoints.RemoveAll(w => ticks.Contains(w.TimestampTicks));
        }
        Notice = null;
        return true;
    }

    public CourseEdit DeleteAt(int index) => Commit(() => CourseEdits.Delete(Waypoints, index));

    public CourseEdit Connect(int selectedIndex, int targetIndex, WaypointType type) =>
        Commit(() => CourseEdits.Connect(Waypoints, selectedIndex, targetIndex, type, NextTick));

    public CourseEdit MoveTo(int index, double latitude, double longitude, Guid courseId) =>
        Commit(() => CourseEdits.Move(Waypoints, index, latitude, longitude, courseId));

    public CourseEdit PlaceInterval(int index, string? distanceLabel, Guid courseId) =>
        Commit(() => CourseEdits.PlaceInterval(Waypoints, index, distanceLabel, courseId));

    public CourseEdit PlaceIntervalAt(double startKm, string? distanceLabel, Guid courseId) =>
        Commit(() => CourseEdits.PlaceIntervalAt(Waypoints, startKm, distanceLabel, courseId));

    public CourseEdit InsertAlongRoute(WaypointType type, double elapsedKm) =>
        Commit(() => CourseEdits.InsertAlongRoute(Waypoints, type, elapsedKm, NextTick));

    public CourseEdit AddStandardFinish(int selectedIndex, double towardLatitude, double towardLongitude, bool metric) =>
        Commit(() => CourseEdits.InsertStandardFinish(Waypoints, selectedIndex, towardLatitude, towardLongitude, metric, NextTick));

    public CourseEdit InsertSummit(double latitude, double longitude, double elapsedKm, double altitudeKm) =>
        Commit(() => CourseEdits.InsertSummit(Waypoints, latitude, longitude, elapsedKm, altitudeKm, NextTick));

    public int SpliceRoute(long fromTicks, long toTicks, RouteResponseDto? route) =>
        CourseEdits.Splice(Waypoints, fromTicks, toTicks, route, NextTick);

    private void AddRoute(RouteResponseDto route, WaypointType requestedType, double latitude, double longitude)
    {
        var previous = LastRoutePoint();
        if (previous is not null
            && route.OriginAltitudeKm is double origin
            && previous.AltitudeKm == Altitude.Unknown.Kilometers)
            previous.AltitudeKm = origin;

        var start = Waypoints.FirstOrDefault(w => w.WaypointType == WaypointType.Start) ?? Waypoints.FirstOrDefault();
        var points = start is null
            ? route.Points
            : RoutePath.WithoutImplicitReturn(start.Latitude, start.Longitude, latitude, longitude, route.Points);
        if (points.Count == 0)
        {
            Notice = "That route returned to the start. Right-click the start and choose FINISH to close the loop.";
            return;
        }

        var added = new List<long>(points.Count);
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            var type = Enum.TryParse<WaypointType>(point.WaypointType, ignoreCase: true, out var parsed)
                ? parsed
                : WaypointType.Continue;
            if (i == points.Count - 1 && requestedType != WaypointType.Continue)
                type = requestedType;
            var tick = NextTick();
            Waypoints.Add(new Waypoint
            {
                Latitude = point.Latitude,
                Longitude = point.Longitude,
                WaypointType = type,
                TextDescription = Trim(point.Text),
                AltitudeKm = point.AltitudeKm,
                TimestampTicks = tick
            });
            added.Add(tick);
        }
        _undo.Push(new UndoStep { RemoveTicks = added.ToArray() });
    }

    private void AddOne(double latitude, double longitude, WaypointType type, double elapsedKm)
    {
        var tick = NextTick();
        Waypoints.Add(new Waypoint
        {
            Latitude = latitude,
            Longitude = longitude,
            WaypointType = type,
            ElapsedDistanceKm = elapsedKm,
            AltitudeKm = Altitude.Unknown.Kilometers,
            TimestampTicks = tick
        });
        _undo.Push(new UndoStep { RemoveTicks = [tick] });
    }

    private CourseEdit Commit(Func<CourseEdit> edit)
    {
        var snapshot = Waypoints.Select(Clone).ToList();
        var result = edit();
        if (!result.Ok)
        {
            Waypoints.Clear();
            Waypoints.AddRange(snapshot);
            Notice = result.Message;
            return result;
        }

        _undo.Push(new UndoStep { Restore = snapshot });
        Notice = null;
        return result;
    }

    private static Waypoint Clone(Waypoint waypoint) => new()
    {
        CourseId = waypoint.CourseId,
        TimestampTicks = waypoint.TimestampTicks,
        Latitude = waypoint.Latitude,
        Longitude = waypoint.Longitude,
        HeadingDegrees = waypoint.HeadingDegrees,
        AltitudeKm = waypoint.AltitudeKm,
        ElapsedDistanceKm = waypoint.ElapsedDistanceKm,
        WaypointType = waypoint.WaypointType,
        TextDescription = waypoint.TextDescription
    };

    private sealed class UndoStep
    {
        public long[]? RemoveTicks { get; init; }
        public List<Waypoint>? Restore { get; init; }
    }

    private long NextTick()
    {
        var floor = Waypoints.Count == 0 ? 0 : Waypoints.Max(w => w.TimestampTicks);
        if (_ticks <= floor)
            _ticks = floor;
        return ++_ticks;
    }

    private static string? Trim(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var trimmed = text.Trim();
        return trimmed.Length <= RoutingLimits.MaxInstructionLength
            ? trimmed
            : trimmed[..RoutingLimits.MaxInstructionLength];
    }
}
