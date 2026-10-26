using System.Globalization;

namespace GpsTrainer;

/// <summary>One road leg the editor should ask the router to rebuild.</summary>
public readonly record struct RouteGap(long FromTicks, long ToTicks);

/// <summary>Result of a course-menu edit. Gaps are empty when the line does not need a new route.</summary>
public sealed class CourseEdit
{
    public bool Ok { get; init; }
    public string? Message { get; init; }
    public IReadOnlyList<RouteGap> Gaps { get; init; } = [];

    public static CourseEdit Done(params RouteGap[] gaps) => new() { Ok = true, Gaps = gaps };
    public static CourseEdit Fail(string message) => new() { Message = message };
}

/// <summary>
/// The rest of the 2013 pushpin and map menus, applied to the waypoint list.
/// Connect does not retype the clicked marker. It inserts a new waypoint of the
/// chosen type at that marker, immediately after the selected waypoint, and a
/// selected Finish becomes Continue. The original menu showed Connect only when
/// the selected waypoint was the last one. A middle delete leaves a gap between
/// the neighboring route points; the first and last deletes only promote Start
/// or Finish. Interval markers move as a pair and do not open a road gap.
/// </summary>
public static class CourseEdits
{
    public static readonly string[] IntervalDistances = ["100m", "200m", "400m", "500m", "800m", "1000m", "1mi"];

    public static readonly WaypointType[] ConnectTypes =
    [
        WaypointType.Continue,
        WaypointType.TurnRight,
        WaypointType.TurnLeft,
        WaypointType.TurnAround,
        WaypointType.Finish
    ];

    public static bool IsInterval(Waypoint waypoint) =>
        waypoint.WaypointType is WaypointType.StartInterval or WaypointType.FinishInterval;

    public static double LengthKm(IReadOnlyList<Waypoint> waypoints)
    {
        double distance = 0;
        Waypoint? previous = null;
        foreach (var point in waypoints)
        {
            if (IsInterval(point))
                continue;
            if (previous is not null)
                distance += GeoMath.DistanceMeters(previous.Latitude, previous.Longitude, point.Latitude, point.Longitude) / 1000.0;
            previous = point;
        }
        return distance;
    }

    /// <summary>
    /// floor(length + 1) plus " km" or " mi", matching <c>Course.StandardCourseLength</c>.
    /// A metric length in (42, 42.195) is "42.2 km". An English length in (26, 26.22) is "26.2 mi".
    /// </summary>
    public static string StandardCourseLength(double lengthKm, bool metric)
    {
        var length = metric ? lengthKm : EditorText.KilosToMiles(lengthKm);
        if (metric && length > 42 && length < 42.195)
            return "42.2 km";
        if (!metric && length > 26 && length < 26.22)
            return "26.2 mi";
        return Math.Floor(length + 1).ToString(CultureInfo.InvariantCulture) + (metric ? " km" : " mi");
    }

    public static string StandardFinishMenuLabel(double lengthKm, bool metric) =>
        StandardCourseLength(lengthKm, metric) + " FINISH";

    public static CourseEdit Delete(List<Waypoint> waypoints, int index)
    {
        if ((uint)index >= (uint)waypoints.Count)
            return CourseEdit.Fail("That waypoint is not on the course.");
        var target = waypoints[index];
        if (IsInterval(target))
        {
            var pair = CourseIntervals.Pair(waypoints).FirstOrDefault(item =>
                item.Start.TimestampTicks == target.TimestampTicks || item.Finish.TimestampTicks == target.TimestampTicks);
            if (pair.Start is null)
                waypoints.RemoveAt(index);
            else
            {
                var start = pair.Start.TimestampTicks;
                var finish = pair.Finish.TimestampTicks;
                waypoints.RemoveAll(point => point.TimestampTicks == start || point.TimestampTicks == finish);
            }
            return CourseEdit.Done();
        }

        var route = RouteIndexes(waypoints);
        var position = route.IndexOf(index);
        if (position < 0)
            return CourseEdit.Fail("That waypoint is not on the course.");
        if (route.Count == 1)
        {
            waypoints.RemoveAt(index);
            return CourseEdit.Done();
        }
        if (position == 0)
        {
            waypoints.RemoveAt(index);
            var next = waypoints[RouteIndexes(waypoints)[0]];
            next.WaypointType = WaypointType.Start;
            next.ElapsedDistanceKm = 0;
            StampRoute(waypoints);
            return CourseEdit.Done();
        }
        if (position == route.Count - 1)
        {
            var previous = waypoints[route[position - 1]];
            waypoints.RemoveAt(index);
            if (previous.WaypointType != WaypointType.Start)
                previous.WaypointType = WaypointType.Finish;
            previous.HeadingDegrees = -1;
            StampRoute(waypoints);
            return CourseEdit.Done();
        }

        var before = waypoints[route[position - 1]];
        var after = waypoints[route[position + 1]];
        waypoints.RemoveAt(index);
        StampRoute(waypoints);
        return CourseEdit.Done(new RouteGap(before.TimestampTicks, after.TimestampTicks));
    }

    public static CourseEdit Connect(List<Waypoint> waypoints, int selectedIndex, int targetIndex, WaypointType type, Func<long> nextTick)
    {
        if ((uint)selectedIndex >= (uint)waypoints.Count || (uint)targetIndex >= (uint)waypoints.Count)
            return CourseEdit.Fail("No waypoint selected");
        if (Array.IndexOf(ConnectTypes, type) < 0)
            return CourseEdit.Fail("That connection is not on the menu.");
        var selected = waypoints[selectedIndex];
        var target = waypoints[targetIndex];
        if (IsInterval(selected) || IsInterval(target))
            return CourseEdit.Fail("No waypoint selected");
        if (GeoMath.DistanceMeters(selected.Latitude, selected.Longitude, target.Latitude, target.Longitude) < RoutingLimits.MinSegmentMeters)
            return CourseEdit.Fail("Cannot connect waypoint to itself");

        if (selected.WaypointType == WaypointType.Finish)
            selected.WaypointType = WaypointType.Continue;
        var created = new Waypoint
        {
            CourseId = selected.CourseId,
            Latitude = target.Latitude,
            Longitude = target.Longitude,
            AltitudeKm = target.AltitudeKm,
            HeadingDegrees = -1,
            WaypointType = type,
            TimestampTicks = nextTick()
        };
        var insertAt = selectedIndex + 1;
        waypoints.Insert(insertAt, created);
        StampRoute(waypoints);
        var gaps = new List<RouteGap> { new(selected.TimestampTicks, created.TimestampTicks) };
        var next = waypoints.Skip(insertAt + 1).FirstOrDefault(point => !IsInterval(point));
        if (next is not null)
            gaps.Add(new RouteGap(created.TimestampTicks, next.TimestampTicks));
        return CourseEdit.Done(gaps.ToArray());
    }

    public static CourseEdit Move(List<Waypoint> waypoints, int index, double latitude, double longitude, Guid courseId)
    {
        if ((uint)index >= (uint)waypoints.Count)
            return CourseEdit.Fail("That waypoint is not on the course.");
        if (!RoutingLimits.IsPosition(latitude, longitude))
            return CourseEdit.Fail("Those coordinates are not on the map.");
        var target = waypoints[index];
        if (IsInterval(target))
            return MoveInterval(waypoints, target, latitude, longitude, courseId);

        target.Latitude = latitude;
        target.Longitude = longitude;
        var route = RouteIndexes(waypoints);
        var position = route.IndexOf(index);
        var gaps = new List<RouteGap>();
        if (position > 0)
        {
            var previous = waypoints[route[position - 1]];
            previous.HeadingDegrees = GeoMath.InitialBearingDegrees(previous.Latitude, previous.Longitude, latitude, longitude);
            gaps.Add(new RouteGap(previous.TimestampTicks, target.TimestampTicks));
        }
        if (position >= 0 && position < route.Count - 1)
        {
            var next = waypoints[route[position + 1]];
            target.HeadingDegrees = GeoMath.InitialBearingDegrees(latitude, longitude, next.Latitude, next.Longitude);
            gaps.Add(new RouteGap(target.TimestampTicks, next.TimestampTicks));
        }
        else if (position > 0)
        {
            var previous = waypoints[route[position - 1]];
            target.HeadingDegrees = GeoMath.InitialBearingDegrees(previous.Latitude, previous.Longitude, latitude, longitude);
        }
        StampRoute(waypoints);
        return CourseEdit.Done(gaps.ToArray());
    }

    public static CourseEdit PlaceInterval(List<Waypoint> waypoints, int index, string? distanceLabel, Guid courseId)
    {
        if ((uint)index >= (uint)waypoints.Count)
            return CourseEdit.Fail("That waypoint is not on the course.");
        var kilometers = EditorText.ParseDistanceKm(distanceLabel ?? "");
        if (kilometers is null or <= 0)
            return CourseEdit.Fail("That interval distance was not recognized.");
        StampRoute(waypoints);
        var route = waypoints.Where(point => !IsInterval(point)).ToList();
        if (route.Count < 2 || route[^1].ElapsedDistanceKm <= 0)
            return CourseEdit.Fail("Course is too short to add an Interval");
        var pin = waypoints[index];
        var hit = IntervalCourse.Nearest(route, pin.Latitude, pin.Longitude);
        var finishKm = Math.Min(route[^1].ElapsedDistanceKm, hit.ElapsedKm + kilometers.Value);
        if (finishKm <= hit.ElapsedKm)
            return CourseEdit.Fail("Course is too short to add an Interval");
        try
        {
            var placed = IntervalCourse.Place(waypoints, courseId, distanceLabel ?? "", hit.ElapsedKm, finishKm).Waypoints;
            waypoints.Clear();
            waypoints.AddRange(placed);
        }
        catch (ArgumentException ex)
        {
            return CourseEdit.Fail(ex.Message);
        }
        return CourseEdit.Done();
    }

    /// <summary>
    /// Places an interval that starts at an elapsed distance on the route, as the 2013
    /// profile chart did from the click's percent along the plot.
    /// </summary>
    public static CourseEdit PlaceIntervalAt(List<Waypoint> waypoints, double startKm, string? distanceLabel, Guid courseId)
    {
        var kilometers = EditorText.ParseDistanceKm(distanceLabel ?? "");
        if (kilometers is null or <= 0)
            return CourseEdit.Fail("That interval distance was not recognized.");
        StampRoute(waypoints);
        var route = waypoints.Where(point => !IsInterval(point)).ToList();
        if (route.Count < 2 || route[^1].ElapsedDistanceKm <= 0)
            return CourseEdit.Fail("Course is too short to add an Interval");
        var start = Math.Clamp(startKm, 0, route[^1].ElapsedDistanceKm);
        var finish = Math.Min(route[^1].ElapsedDistanceKm, start + kilometers.Value);
        if (finish <= start)
        {
            start = Math.Max(0, route[^1].ElapsedDistanceKm - kilometers.Value);
            finish = route[^1].ElapsedDistanceKm;
        }
        if (finish <= start)
            return CourseEdit.Fail("Course is too short to add an Interval");
        try
        {
            var placed = IntervalCourse.Place(waypoints, courseId, distanceLabel ?? "", start, finish).Waypoints;
            waypoints.Clear();
            waypoints.AddRange(placed);
        }
        catch (ArgumentException ex)
        {
            return CourseEdit.Fail(ex.Message);
        }
        return CourseEdit.Done();
    }

    /// <summary>
    /// Inserts a Turn Around, Feed Zone, or Announcement snapped to the route at an elapsed distance.
    /// </summary>
    public static CourseEdit InsertAlongRoute(List<Waypoint> waypoints, WaypointType type, double elapsedKm, Func<long> nextTick)
    {
        if (type is not (WaypointType.TurnAround or WaypointType.FeedZone or WaypointType.Announcement))
            return CourseEdit.Fail("That profile marker is not supported.");
        StampRoute(waypoints);
        var route = waypoints.Where(point => !IsInterval(point)).ToList();
        if (route.Count < 2 || route[^1].ElapsedDistanceKm <= 0)
            return CourseEdit.Fail("Course is too short to add Waypoints");
        var at = Math.Clamp(elapsedKm, 0, route[^1].ElapsedDistanceKm);
        var point = SummitSearch.PointAt(route, at);
        var insertAt = waypoints.Count;
        for (var i = 0; i < waypoints.Count; i++)
        {
            if (IsInterval(waypoints[i]))
                continue;
            if (waypoints[i].ElapsedDistanceKm > at + 1e-9)
            {
                insertAt = i;
                break;
            }
        }
        waypoints.Insert(insertAt, new Waypoint
        {
            CourseId = waypoints[0].CourseId,
            Latitude = point.Latitude,
            Longitude = point.Longitude,
            AltitudeKm = point.AltitudeKm,
            HeadingDegrees = -1,
            WaypointType = type,
            ElapsedDistanceKm = at,
            TimestampTicks = nextTick()
        });
        StampRoute(waypoints);
        return CourseEdit.Done();
    }

    /// <summary>
    /// Places a Finish beyond the selected waypoint by the amount the standard-distance
    /// label adds. The click is the direction. The same point has no bearing, so the
    /// incoming leg is used instead.
    /// </summary>
    public static CourseEdit InsertStandardFinish(
        List<Waypoint> waypoints, int selectedIndex, double towardLatitude, double towardLongitude, bool metric, Func<long> nextTick)
    {
        var selectedAt = ResolveRouteIndex(waypoints, selectedIndex);
        if (selectedAt < 0)
            return CourseEdit.Fail("Add a start before adding a finish.");
        var selected = waypoints[selectedAt];
        var current = LengthKm(waypoints);
        var target = EditorText.ParseDistanceKm(StandardCourseLength(current, metric));
        if (target is null || target.Value <= current)
            return CourseEdit.Fail("That finish is not longer than the course.");
        var extra = target.Value - current;
        double bearing;
        if (GeoMath.DistanceMeters(selected.Latitude, selected.Longitude, towardLatitude, towardLongitude) < RoutingLimits.MinSegmentMeters)
        {
            var route = RouteIndexes(waypoints);
            var position = route.IndexOf(selectedAt);
            if (position > 0)
            {
                var previous = waypoints[route[position - 1]];
                bearing = GeoMath.InitialBearingDegrees(previous.Latitude, previous.Longitude, selected.Latitude, selected.Longitude);
            }
            else
                bearing = 0;
        }
        else
            bearing = GeoMath.InitialBearingDegrees(selected.Latitude, selected.Longitude, towardLatitude, towardLongitude);

        var point = GeoMath.Translate(selected.Latitude, selected.Longitude, bearing, extra);
        if (selected.WaypointType == WaypointType.Finish)
            selected.WaypointType = WaypointType.Continue;
        var created = new Waypoint
        {
            CourseId = selected.CourseId,
            Latitude = point.Latitude,
            Longitude = point.Longitude,
            AltitudeKm = Altitude.Unknown.Kilometers,
            HeadingDegrees = bearing,
            WaypointType = WaypointType.Finish,
            TimestampTicks = nextTick()
        };
        waypoints.Insert(selectedAt + 1, created);
        StampRoute(waypoints);
        var gaps = new List<RouteGap> { new(selected.TimestampTicks, created.TimestampTicks) };
        var next = waypoints.Skip(selectedAt + 2).FirstOrDefault(item => !IsInterval(item));
        if (next is not null)
            gaps.Add(new RouteGap(created.TimestampTicks, next.TimestampTicks));
        return CourseEdit.Done(gaps.ToArray());
    }

    /// <summary>
    /// Inserts a Summit on the existing line. A Summit already within 8 m is moved
    /// onto the high point. A nearby turn is left as a turn.
    /// </summary>
    public static CourseEdit InsertSummit(
        List<Waypoint> waypoints, double latitude, double longitude, double elapsedKm, double altitudeKm, Func<long> nextTick)
    {
        if (!RoutingLimits.IsPosition(latitude, longitude))
            return CourseEdit.Fail("Those coordinates are not on the map.");
        if (!SummitSearch.IsKnown(altitudeKm) || !double.IsFinite(elapsedKm))
            return CourseEdit.Fail("No elevation was found for a summit on this course.");

        StampRoute(waypoints);
        foreach (var existing in waypoints)
        {
            if (existing.WaypointType != WaypointType.Summit)
                continue;
            if (GeoMath.DistanceMeters(existing.Latitude, existing.Longitude, latitude, longitude) >= RoutingLimits.MinSegmentMeters)
                continue;
            existing.Latitude = latitude;
            existing.Longitude = longitude;
            existing.AltitudeKm = altitudeKm;
            existing.ElapsedDistanceKm = elapsedKm;
            StampRoute(waypoints);
            return CourseEdit.Done();
        }

        var insertAt = waypoints.Count;
        for (var i = 0; i < waypoints.Count; i++)
        {
            if (IsInterval(waypoints[i]))
                continue;
            if (waypoints[i].ElapsedDistanceKm > elapsedKm + 1e-9)
            {
                insertAt = i;
                break;
            }
        }
        waypoints.Insert(insertAt, new Waypoint
        {
            CourseId = waypoints.Count == 0 ? Guid.Empty : waypoints[0].CourseId,
            Latitude = latitude,
            Longitude = longitude,
            AltitudeKm = altitudeKm,
            HeadingDegrees = -1,
            WaypointType = WaypointType.Summit,
            ElapsedDistanceKm = elapsedKm,
            TimestampTicks = nextTick()
        });
        StampRoute(waypoints);
        return CourseEdit.Done();
    }

    public static int Splice(List<Waypoint> waypoints, long fromTicks, long toTicks, RouteResponseDto? route, Func<long> nextTick)
    {
        if (route is not { Routed: true, Points.Count: > 0 })
            return 0;
        var from = waypoints.FindIndex(point => point.TimestampTicks == fromTicks);
        var to = waypoints.FindIndex(point => point.TimestampTicks == toTicks);
        if (from < 0 || to < 0 || to <= from)
            return 0;
        var origin = waypoints[from];
        var destination = waypoints[to];
        if (route.OriginAltitudeKm is double originAltitude && originAltitude != Altitude.Unknown.Kilometers)
            origin.AltitudeKm = originAltitude;
        var arrived = route.Points.LastOrDefault(point =>
            point.AltitudeKm != Altitude.Unknown.Kilometers
            && GeoMath.DistanceMeters(point.Latitude, point.Longitude, destination.Latitude, destination.Longitude) < RoutePath.SamePointMeters);
        if (arrived is not null)
            destination.AltitudeKm = arrived.AltitudeKm;

        var start = waypoints.FirstOrDefault(point => point.WaypointType == WaypointType.Start) ?? waypoints[0];
        var kept = RoutePath.WithoutImplicitReturn(
            start.Latitude, start.Longitude, destination.Latitude, destination.Longitude, route.Points);
        var insertAt = to;
        var added = 0;
        foreach (var point in kept)
        {
            if (GeoMath.DistanceMeters(point.Latitude, point.Longitude, origin.Latitude, origin.Longitude) < RoutePath.SamePointMeters)
                continue;
            if (GeoMath.DistanceMeters(point.Latitude, point.Longitude, destination.Latitude, destination.Longitude) < RoutePath.SamePointMeters)
                continue;
            var type = Enum.TryParse<WaypointType>(point.WaypointType, ignoreCase: true, out var parsed)
                ? parsed
                : WaypointType.Continue;
            if (IsInterval(new Waypoint { WaypointType = type }))
                type = WaypointType.Continue;
            waypoints.Insert(insertAt, new Waypoint
            {
                CourseId = destination.CourseId,
                Latitude = point.Latitude,
                Longitude = point.Longitude,
                WaypointType = type,
                TextDescription = Trim(point.Text),
                AltitudeKm = point.AltitudeKm,
                TimestampTicks = nextTick()
            });
            insertAt++;
            added++;
        }
        StampRoute(waypoints);
        return added;
    }

    public static void StampRoute(IList<Waypoint> waypoints)
    {
        Waypoint? previous = null;
        var distance = 0.0;
        foreach (var point in waypoints)
        {
            if (IsInterval(point))
                continue;
            if (previous is not null)
                distance += GeoMath.DistanceMeters(previous.Latitude, previous.Longitude, point.Latitude, point.Longitude) / 1000.0;
            point.ElapsedDistanceKm = distance;
            previous = point;
        }
    }

    private static CourseEdit MoveInterval(List<Waypoint> waypoints, Waypoint target, double latitude, double longitude, Guid courseId)
    {
        StampRoute(waypoints);
        var route = waypoints.Where(point => !IsInterval(point)).ToList();
        if (route.Count < 2)
            return CourseEdit.Fail("Course is too short to move an interval.");
        var interval = IntervalCourse.FromMarkers(courseId, waypoints).FirstOrDefault(item =>
            item.StartTimestampTicks == target.TimestampTicks || item.FinishTimestampTicks == target.TimestampTicks);
        if (interval is null)
            return CourseEdit.Fail("That interval is not on the course.");
        var startNow = IntervalCourse.Nearest(route, interval.StartLatitude, interval.StartLongitude).ElapsedKm;
        var finishNow = IntervalCourse.Nearest(route, interval.FinishLatitude, interval.FinishLongitude).ElapsedKm;
        var length = finishNow - startNow;
        if (length <= 0)
            length = interval.LengthKm;
        var courseLength = route[^1].ElapsedDistanceKm;
        if (length <= 0 || courseLength <= 0)
            return CourseEdit.Fail("Finish must be after the start.");
        if (length > courseLength)
            length = courseLength;
        var dragged = IntervalCourse.Nearest(route, target.Latitude, target.Longitude).ElapsedKm;
        var drop = IntervalCourse.Nearest(route, latitude, longitude).ElapsedKm;
        var newStart = startNow + (drop - dragged);
        if (newStart < 0)
            newStart = 0;
        if (newStart + length > courseLength)
            newStart = Math.Max(0, courseLength - length);
        if (newStart + length <= newStart)
            return CourseEdit.Fail("Course is too short to move an interval.");
        try
        {
            var moved = IntervalCourse.Move(waypoints, interval, newStart, newStart + length);
            waypoints.Clear();
            waypoints.AddRange(moved);
        }
        catch (ArgumentException ex)
        {
            return CourseEdit.Fail(ex.Message);
        }
        return CourseEdit.Done();
    }

    private static int ResolveRouteIndex(IReadOnlyList<Waypoint> waypoints, int index)
    {
        var route = RouteIndexes(waypoints);
        if (route.Count == 0)
            return -1;
        if ((uint)index < (uint)waypoints.Count && !IsInterval(waypoints[index]))
            return index;
        return route[^1];
    }

    private static List<int> RouteIndexes(IReadOnlyList<Waypoint> waypoints)
    {
        var indexes = new List<int>();
        for (var i = 0; i < waypoints.Count; i++)
        {
            if (!IsInterval(waypoints[i]))
                indexes.Add(i);
        }
        return indexes;
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
