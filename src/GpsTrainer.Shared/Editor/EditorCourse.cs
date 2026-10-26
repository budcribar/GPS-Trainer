namespace GpsTrainer;

public sealed class EditorPin
{
    public const long NullTicks = -1;
    public const double UnknownAltitudeKm = -10;

    public long TimestampTicks { get; set; } = NullTicks;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double AltitudeKm { get; set; } = UnknownAltitudeKm;
    public double Heading { get; set; } = -1;
    public WaypointType WaypointType { get; set; } = WaypointType.Continue;
    public string TextDescription { get; set; } = "";
    public double ElapsedDistanceKm { get; set; }

    public double DistanceTo(EditorPin other) =>
        GeoMath.DistanceMeters(Latitude, Longitude, other.Latitude, other.Longitude) / 1000.0;

    public double BearingTo(EditorPin other) =>
        GeoMath.InitialBearingDegrees(Latitude, Longitude, other.Latitude, other.Longitude);

    public EditorPin TranslateTo(double bearingDegrees, double distanceKm)
    {
        var point = GeoMath.Translate(Latitude, Longitude, bearingDegrees, distanceKm);
        return new EditorPin { Latitude = point.Latitude, Longitude = point.Longitude };
    }
}

/// <summary>
/// Linked-list course from <c>Typescript/CourseEditor/CourseEditor/Course.ts</c>.
/// Pushpins and Bing/Google map calls are not part of this class.
/// </summary>
public sealed class EditorCourse
{
    private sealed class Node
    {
        public EditorPin Pin { get; }
        public Node? Prev { get; set; }
        public Node? Next { get; set; }
        public int Index { get; set; }
        public Node(EditorPin pin, Node? prev, Node? next, int index)
        {
            Pin = pin;
            Prev = prev;
            Next = next;
            Index = index;
        }
    }

    private readonly Dictionary<long, Node> _pins = new();
    private long _first = EditorPin.NullTicks;
    private long _last = EditorPin.NullTicks;
    private long _selected = EditorPin.NullTicks;

    public string? Name { get; set; }
    public double ElapsedDistanceKm { get; private set; }

    public bool NoWaypointsEntered => _first == EditorPin.NullTicks;

    public EditorPin? Start => _first == EditorPin.NullTicks ? null : _pins[_first].Pin;
    public EditorPin? Finish => _last == EditorPin.NullTicks ? null : _pins[_last].Pin;

    public EditorPin? Next(EditorPin pin) => _pins.TryGetValue(pin.TimestampTicks, out var node) ? node.Next?.Pin : null;
    public EditorPin? Previous(EditorPin pin) => _pins.TryGetValue(pin.TimestampTicks, out var node) ? node.Prev?.Pin : null;

    public int? RouteIndex(EditorPin pin) => _pins.TryGetValue(pin.TimestampTicks, out var node) ? node.Index : null;

    public EditorPin? Selected => _selected == EditorPin.NullTicks || !_pins.ContainsKey(_selected) ? null : _pins[_selected].Pin;

    public int? CurrentRouteSegment() => Selected is null ? null : _pins[_selected].Index;

    public void Select(EditorPin? pin) => _selected = pin?.TimestampTicks ?? EditorPin.NullTicks;

    public EditorPin? Find(long ticks) => _pins.TryGetValue(ticks, out var node) ? node.Pin : null;

    public IReadOnlyList<EditorPin> Waypoints()
    {
        var list = new List<EditorPin>();
        var pin = Start;
        while (pin is not null)
        {
            list.Add(pin);
            pin = Next(pin);
        }
        return list;
    }

    public double AltitudeGainKm() => Altitude(gain: true);
    public double AltitudeLossKm() => Altitude(gain: false);

    public double Insert(EditorPin? previous, EditorPin target)
    {
        var next = previous is null ? Start : Next(previous);
        return InsertCore(previous, next, target);
    }

    public void Append(EditorPin pin)
    {
        if (_first == EditorPin.NullTicks)
            _first = pin.TimestampTicks;
        Node? prev = _last == EditorPin.NullTicks ? null : _pins[_last];
        var index = prev is null ? 0 : prev.Index + 1;
        var node = new Node(pin, prev, null, index);
        if (prev is not null)
        {
            ElapsedDistanceKm += prev.Pin.DistanceTo(pin);
            pin.ElapsedDistanceKm = ElapsedDistanceKm;
            prev.Next = node;
        }
        _pins[pin.TimestampTicks] = node;
        _last = pin.TimestampTicks;
    }

    public void Delete(EditorPin target)
    {
        var prev = Previous(target);
        var next = Next(target);
        if (prev is null)
        {
            if (next is null)
            {
                Clear();
                return;
            }
            var nextNode = _pins[next.TimestampTicks];
            nextNode.Prev = null;
            _first = next.TimestampTicks;
            var delta = -next.ElapsedDistanceKm;
            AddElapsed(next, delta);
            ElapsedDistanceKm += delta;
            next.WaypointType = WaypointType.Start;
            while (nextNode is not null)
            {
                nextNode.Index--;
                nextNode = nextNode.Next;
            }
        }
        else if (next is null)
        {
            var prevNode = _pins[prev.TimestampTicks];
            prevNode.Next = null;
            _last = prev.TimestampTicks;
            ElapsedDistanceKm = prev.ElapsedDistanceKm;
            prev.Heading = -1;
            if (prev.WaypointType != WaypointType.Start)
                prev.WaypointType = WaypointType.Finish;
        }
        else
        {
            var prevNode = _pins[prev.TimestampTicks];
            var nextNode = _pins[next.TimestampTicks];
            prevNode.Next = nextNode;
            nextNode.Prev = prevNode;
            prev.Heading = prev.BearingTo(next);
            var delta = prev.ElapsedDistanceKm + prev.DistanceTo(next) - next.ElapsedDistanceKm;
            AddElapsed(next, delta);
            ElapsedDistanceKm += delta;
            while (nextNode is not null)
            {
                nextNode.Index--;
                nextNode = nextNode.Next;
            }
        }
        _pins.Remove(target.TimestampTicks);
    }

    public string Validate()
    {
        if (string.IsNullOrEmpty(Name))
            return "Course Name is missing";
        if (ElapsedDistanceKm <= 50.0 / 1000.0)
            return "Course is too short";
        var waypoints = Waypoints();
        if (waypoints.Count < 2)
            return "A start and finish waypoint must be specified";
        if (waypoints[0].WaypointType != WaypointType.Start)
            return "First waypoint must be start";
        if (waypoints[0].ElapsedDistanceKm != 0.0)
            return "ElapsedDistanceKM should be 0 on start waypoint";
        if (waypoints[^1].WaypointType != WaypointType.Finish)
            return "Last waypoint must be finish";
        if (waypoints.Count(w => w.WaypointType == WaypointType.Start) != 1)
            return "There must be only one start waypoint";
        if (waypoints.Count(w => w.WaypointType == WaypointType.Finish) != 1)
            return "There must be only one finish waypoint";
        var open = false;
        EditorPin? previous = null;
        foreach (var wp in waypoints)
        {
            if (previous is not null && wp.ElapsedDistanceKm < previous.ElapsedDistanceKm)
                return EditorText.Format("Elapsed Distance is not sequential {0}:{1} {2}:{3}", previous.TimestampTicks, previous.ElapsedDistanceKm, wp.TimestampTicks, wp.ElapsedDistanceKm);
            previous = wp;
            if (wp.TimestampTicks < 0)
                return "Negative TimestampTicks found";
            if (wp.WaypointType == WaypointType.StartInterval)
            {
                if (open)
                    return "Overlapping Start Interval Found";
                open = true;
            }
            if (wp.WaypointType == WaypointType.FinishInterval)
            {
                if (!open)
                    return "Finish Interval Found Before Start Interval";
                open = false;
            }
        }
        return open ? "Must have a matching Finish Interval for each Start Interval" : "";
    }

    public EditorPin? FindClosest(double longitude, double latitude)
    {
        var min = 100000.0;
        EditorPin? found = null;
        var node = _first == EditorPin.NullTicks ? null : _pins[_first];
        var target = new EditorPin { Latitude = latitude, Longitude = longitude };
        while (node is not null)
        {
            if (node.Next is not null)
            {
                var along = DistanceToSegment(longitude, latitude, node.Pin, node.Next.Pin);
                if (along < min)
                {
                    min = along;
                    found = node.Pin;
                }
            }
            var dist = target.DistanceTo(node.Pin);
            if (dist <= min)
            {
                min = dist;
                found = node.Pin;
            }
            node = node.Next;
        }
        return found;
    }

    public EditorPin? MoveAlongRoute(EditorPin pin, double distanceMovedKm)
    {
        var targetKm = pin.ElapsedDistanceKm + distanceMovedKm;
        if (targetKm < 0)
            targetKm = 0;
        if (targetKm > ElapsedDistanceKm)
            targetKm = ElapsedDistanceKm;
        var node = _pins[pin.TimestampTicks];
        if (distanceMovedKm > 0)
        {
            while (node is not null)
            {
                if (node.Pin.ElapsedDistanceKm == targetKm)
                    return node.Pin;
                if (node.Pin.ElapsedDistanceKm > targetKm)
                {
                    if (node.Prev is null)
                        return null;
                    var prev = node.Prev.Pin;
                    if (pin.WaypointType == WaypointType.FinishInterval && prev.WaypointType == WaypointType.StartInterval)
                        return null;
                    return prev.TranslateTo(prev.Heading, targetKm - prev.ElapsedDistanceKm);
                }
                node = node.Next;
            }
        }
        else
        {
            while (node is not null)
            {
                if (node.Pin.ElapsedDistanceKm <= targetKm)
                    return node.Pin.TranslateTo(node.Pin.Heading, targetKm - node.Pin.ElapsedDistanceKm);
                node = node.Prev;
            }
        }
        return null;
    }

    public EditorPin? FindStartInterval(EditorPin finish)
    {
        var node = _pins[finish.TimestampTicks];
        while (node is not null)
        {
            if (node.Pin.WaypointType == WaypointType.StartInterval)
                return node.Pin;
            node = node.Prev;
        }
        return null;
    }

    public EditorPin? FindFinishInterval(EditorPin start)
    {
        var node = _pins[start.TimestampTicks];
        while (node is not null)
        {
            if (node.Pin.WaypointType == WaypointType.FinishInterval)
                return node.Pin;
            node = node.Next;
        }
        return null;
    }

    public static int CountType(IEnumerable<EditorPin> pins, WaypointType type) => pins.Count(p => p.WaypointType == type);

    private double InsertCore(EditorPin? previous, EditorPin? next, EditorPin target)
    {
        var delta = 0.0;
        if (next is null)
        {
            if (previous is null)
            {
                target.ElapsedDistanceKm = 0;
                if (target.TimestampTicks == EditorPin.NullTicks)
                    target.TimestampTicks = 0;
            }
            else
            {
                var leg = previous.DistanceTo(target);
                target.ElapsedDistanceKm = previous.ElapsedDistanceKm + leg;
                ElapsedDistanceKm = target.ElapsedDistanceKm;
                if (target.TimestampTicks == EditorPin.NullTicks)
                {
                    var deltaT = (long)Math.Round(leg * 60 * 60 * 1000 / 10);
                    target.TimestampTicks = previous.TimestampTicks + deltaT;
                    PreventDuplicates(previous, null, target);
                }
                previous.Heading = previous.BearingTo(target);
                if (previous.WaypointType == WaypointType.Finish)
                    previous.WaypointType = WaypointType.Continue;
            }
        }
        else if (previous is null)
        {
            if (next.TimestampTicks == _last)
                next.WaypointType = WaypointType.Finish;
            else
                next.WaypointType = WaypointType.Continue;
            delta = target.DistanceTo(next);
            AddElapsed(next, delta);
            ElapsedDistanceKm += delta;
        }
        else
        {
            var span = next.TimestampTicks - previous.TimestampTicks;
            var d = previous.DistanceTo(target);
            var total = d + target.DistanceTo(next);
            if (target.TimestampTicks == EditorPin.NullTicks)
            {
                target.TimestampTicks = previous.TimestampTicks + (long)Math.Round(span * d / total);
                PreventDuplicates(previous, next, target);
            }
            if (target.Heading < 0)
                target.Heading = target.BearingTo(next);
            previous.Heading = previous.BearingTo(target);
            if (previous.WaypointType == WaypointType.Finish)
                previous.WaypointType = WaypointType.Continue;
            target.ElapsedDistanceKm = previous.ElapsedDistanceKm + previous.DistanceTo(target);
            delta = target.ElapsedDistanceKm + target.DistanceTo(next) - next.ElapsedDistanceKm;
            AddElapsed(next, delta);
            ElapsedDistanceKm += delta;
        }

        var prevNode = previous is null ? null : _pins[previous.TimestampTicks];
        var nextNode = next is null ? null : _pins[next.TimestampTicks];
        var index = prevNode is null ? 0 : prevNode.Index + 1;
        var node = new Node(target, prevNode, nextNode, index);
        if (prevNode is not null)
            prevNode.Next = node;
        if (nextNode is not null)
            nextNode.Prev = node;
        _pins[target.TimestampTicks] = node;
        var walk = nextNode;
        while (walk is not null)
        {
            walk.Index++;
            walk = walk.Next;
        }
        if (_first == EditorPin.NullTicks || previous is null)
            _first = target.TimestampTicks;
        if (_last == EditorPin.NullTicks || next is null)
            _last = target.TimestampTicks;
        return delta;
    }

    private void AddElapsed(EditorPin pin, double delta)
    {
        while (true)
        {
            pin.ElapsedDistanceKm += delta;
            var next = Next(pin);
            if (next is null)
                return;
            pin = next;
        }
    }

    private static void PreventDuplicates(EditorPin previous, EditorPin? next, EditorPin target)
    {
        if (previous.TimestampTicks == target.TimestampTicks)
            target.TimestampTicks += 5;
        if (next is not null && next.TimestampTicks == target.TimestampTicks)
            target.TimestampTicks -= 5;
    }

    private double Altitude(bool gain)
    {
        var gained = 0.0;
        var lost = 0.0;
        var prev = -10.0;
        var pin = Start;
        const double hysteresis = 3.048 / 1000.0;
        while (pin is not null)
        {
            var current = pin.AltitudeKm;
            if (current != EditorPin.UnknownAltitudeKm)
            {
                if (prev == -10)
                    prev = current;
                else if (Math.Abs(current - prev) > hysteresis)
                {
                    if (current > prev)
                        gained += current - prev;
                    else
                        lost += prev - current;
                    prev = current;
                }
            }
            pin = Next(pin);
        }
        return gain ? gained : lost;
    }

    private static double DistanceToSegment(double longitude, double latitude, EditorPin a, EditorPin b)
    {
        var target = new EditorPin { Latitude = latitude, Longitude = longitude };
        return Math.Min(target.DistanceTo(a), target.DistanceTo(b));
    }

    private void Clear()
    {
        _pins.Clear();
        _first = EditorPin.NullTicks;
        _last = EditorPin.NullTicks;
        _selected = EditorPin.NullTicks;
        ElapsedDistanceKm = 0;
    }
}
