using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace GpsTrainer;

/// <summary>
/// GPX 1.0 and 1.1 import, plus a GPX 1.1 export whose gpsTrainer data lives in a namespaced extension.
/// The extension namespace is an identifier. It does not need to resolve.
/// </summary>
public static class GpxCourse
{
    public const string GpxNamespace = "http://www.topografix.com/GPX/1/1";
    public const string Gpx10Namespace = "http://www.topografix.com/GPX/1/0";
    public const string ExtensionSuffix = "/xmlschemas/gpx/1";
    public const string LegacyExtensionNamespace = "https://gps-trainer.com/xmlschemas/intervals/v1";
    public const int MaxCharacters = 1_000_000;
    public const double DefaultToleranceMeters = 10;
    public const double MinToleranceMeters = 5;
    public const double MaxToleranceMeters = 15;
    public const double HeadingKeepDegrees = 20;
    public const double AltitudeHysteresisKm = 3.048 / 1000.0;

    public static string IntervalNamespace => ExtensionNamespace(null);

    public static string ExtensionNamespace(string? publicBaseUrl)
    {
        var baseUrl = string.IsNullOrWhiteSpace(publicBaseUrl) ? SiteIdentity.PublicUrl : publicBaseUrl.Trim().TrimEnd('/');
        return baseUrl + ExtensionSuffix;
    }

    public static double ClampTolerance(double meters)
    {
        if (double.IsNaN(meters) || meters <= 0)
            return DefaultToleranceMeters;
        return Math.Clamp(meters, MinToleranceMeters, MaxToleranceMeters);
    }

    // Elapsed distance is the route order. A point added after that distance was
    // stamped can still be stored with elapsed 0, and sorting on elapsed alone
    // pulls it back to the start. Those points stay in timestamp order instead.
    private static List<Waypoint> OrderForExport(IReadOnlyList<Waypoint> waypoints)
    {
        var latestProgressTick = waypoints.Where(w => w.ElapsedDistanceKm > 0).Select(w => (long?)w.TimestampTicks).Max();
        return waypoints
            .OrderBy(w => ExportPosition(w, latestProgressTick))
            .ThenBy(w => w.TimestampTicks)
            .ToList();
    }

    private static double ExportPosition(Waypoint waypoint, long? latestProgressTick)
    {
        if (waypoint.ElapsedDistanceKm > 0)
            return waypoint.ElapsedDistanceKm;
        if (waypoint.WaypointType is WaypointType.StartInterval or WaypointType.FinishInterval)
            return waypoint.ElapsedDistanceKm;
        if (latestProgressTick is long progressed && waypoint.TimestampTicks > progressed)
            return double.MaxValue;
        return 0;
    }

    public static WaypointType ClassifyTurn(double signedDegrees)
    {
        var abs = Math.Abs(signedDegrees);
        if (abs < HeadingKeepDegrees)
            return WaypointType.Continue;
        if (abs >= 150)
            return WaypointType.TurnAround;
        var left = signedDegrees < 0;
        if (abs < 60)
            return left ? WaypointType.BearLeft : WaypointType.BearRight;
        return left ? WaypointType.TurnLeft : WaypointType.TurnRight;
    }

    public static string Export(string courseName, IReadOnlyList<Waypoint> waypoints, IReadOnlyList<CourseIntervalDefinition> intervals, string? publicBaseUrl = null)
    {
        var gpx = XNamespace.Get(GpxNamespace);
        var gpst = XNamespace.Get(ExtensionNamespace(publicBaseUrl));
        var ordered = OrderForExport(waypoints);
        var totals = Measure(ordered);
        var track = new XElement(gpx + "trk",
            new XElement(gpx + "name", courseName),
            new XElement(gpx + "trkseg",
                ordered.Select(wp => new XElement(gpx + "trkpt",
                    new XAttribute("lat", wp.Latitude.ToString("0.0000000", CultureInfo.InvariantCulture)),
                    new XAttribute("lon", wp.Longitude.ToString("0.0000000", CultureInfo.InvariantCulture)),
                    new XElement(gpx + "ele", (wp.AltitudeKm * 1000.0).ToString("0.###", CultureInfo.InvariantCulture)),
                    new XElement(gpx + "extensions",
                        new XElement(gpst + "waypoint",
                            new XAttribute("type", wp.WaypointType.ToString()),
                            new XAttribute("ticks", wp.TimestampTicks.ToString(CultureInfo.InvariantCulture)),
                            new XAttribute("elapsedKm", wp.ElapsedDistanceKm.ToString("0.000000", CultureInfo.InvariantCulture)),
                            new XAttribute("heading", wp.HeadingDegrees.ToString("0.###", CultureInfo.InvariantCulture)),
                            new XAttribute("text", wp.TextDescription ?? "")))))));
        var courseMeta = new XElement(gpst + "course",
            new XAttribute("distanceKm", totals.DistanceKm.ToString("0.000000", CultureInfo.InvariantCulture)),
            new XAttribute("altitudeGainKm", totals.GainKm.ToString("0.000000", CultureInfo.InvariantCulture)),
            new XAttribute("altitudeLossKm", totals.LossKm.ToString("0.000000", CultureInfo.InvariantCulture)));
        var trackExtensions = new XElement(gpx + "extensions", courseMeta);
        if (intervals.Count > 0)
        {
            trackExtensions.Add(new XElement(gpst + "intervals",
                intervals.OrderBy(item => item.Ordinal).Select(item => new XElement(gpst + "interval",
                    new XAttribute("id", item.IntervalId.ToString("D")),
                    new XAttribute("ordinal", item.Ordinal.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("name", item.Name),
                    new XAttribute("startElapsedKm", item.StartElapsedKm.ToString("0.000000", CultureInfo.InvariantCulture)),
                    new XAttribute("finishElapsedKm", item.FinishElapsedKm.ToString("0.000000", CultureInfo.InvariantCulture)),
                    new XAttribute("startTicks", item.StartTimestampTicks.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("finishTicks", item.FinishTimestampTicks.ToString(CultureInfo.InvariantCulture))))));
        }
        track.Add(trackExtensions);

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(gpx + "gpx",
                new XAttribute("version", "1.1"),
                new XAttribute("creator", "gpsTrainer"),
                new XAttribute(XNamespace.Xmlns + "gpst", gpst.NamespaceName),
                new XElement(gpx + "metadata", new XElement(gpx + "name", courseName)),
                track));
        return document.Declaration + Environment.NewLine + document.ToString();
    }

    public static GpxCourseDocument Import(string xml, GpxImportOptions? options = null)
    {
        if (xml.Length > MaxCharacters)
            throw new InvalidOperationException("GPX file is too large.");
        options ??= new GpxImportOptions();
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 0
        };
        using var reader = XmlReader.Create(new StringReader(xml), settings);
        var document = XDocument.Load(reader, LoadOptions.None);
        var root = document.Root ?? throw new InvalidOperationException("GPX document is empty.");
        if (!string.Equals(root.Name.LocalName, "gpx", StringComparison.Ordinal))
            throw new InvalidOperationException("The file is not a GPX document.");
        var version = root.Attribute("version")?.Value;
        if (version is not null && version is not ("1.0" or "1.1"))
            throw new InvalidOperationException("Only GPX 1.0 and 1.1 files can be imported.");

        var trackPoints = LocalDescendants(root, "trkpt").ToList();
        if (trackPoints.Count == 0)
            trackPoints = LocalDescendants(root, "rtept").ToList();
        var namedPoints = root.Elements().Where(element => element.Name.LocalName == "wpt").ToList();
        var geometryIsWaypoints = false;
        if (trackPoints.Count == 0)
        {
            trackPoints = namedPoints;
            geometryIsWaypoints = true;
        }

        var raw = trackPoints.Select(ReadRaw).ToList();
        if (raw.Count == 0)
            throw new InvalidOperationException("GPX file has no points.");

        var trainerPoints = trackPoints.Select(TrainerChild).ToList();
        var intervalElements = root.Descendants().Where(element => element.Name.LocalName == "interval" && IsTrainerNamespace(element.Name.NamespaceName)).ToList();
        var exact = trainerPoints.Any(element => element is not null) || intervalElements.Count > 0;
        var name = ChildValue(root, "metadata", "name")
            ?? LocalDescendants(root, "trk").Select(track => ChildValue(track, "name")).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? LocalDescendants(root, "rte").Select(route => ChildValue(route, "name")).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? "";
        var creator = root.Attribute("creator")?.Value;
        var fileName = CleanFileName(options.FileName);

        List<Waypoint> waypoints;
        if (exact)
            waypoints = ExactWaypoints(raw, trainerPoints);
        else
        {
            var kept = Thin(raw, ClampTolerance(options.ToleranceMeters));
            waypoints = BuildWaypoints(kept);
            if (geometryIsWaypoints)
                MarkNamedPoints(waypoints);
            else if (namedPoints.Count > 0)
                waypoints = InsertAnnouncements(waypoints, namedPoints.Select(ReadRaw).ToList());
        }

        var intervals = intervalElements.Select(ReadInterval).ToList();
        foreach (var interval in intervals)
        {
            var start = waypoints.FirstOrDefault(point => point.TimestampTicks == interval.StartTimestampTicks);
            var finish = waypoints.FirstOrDefault(point => point.TimestampTicks == interval.FinishTimestampTicks);
            if (start is not null)
            {
                interval.StartLatitude = start.Latitude;
                interval.StartLongitude = start.Longitude;
                interval.StartAltitudeKm = start.AltitudeKm;
                interval.StartHeadingDegrees = start.HeadingDegrees;
            }
            if (finish is not null)
            {
                interval.FinishLatitude = finish.Latitude;
                interval.FinishLongitude = finish.Longitude;
                interval.FinishAltitudeKm = finish.AltitudeKm;
                interval.FinishHeadingDegrees = finish.HeadingDegrees;
            }
        }

        var totals = exact ? ReadStoredTotals(root) ?? Measure(waypoints) : Measure(waypoints);
        return new GpxCourseDocument
        {
            Name = name,
            Waypoints = waypoints,
            Intervals = intervals,
            HasGpsTrainerExtensions = exact,
            ElapsedDistanceKm = totals.DistanceKm,
            AltitudeGainKm = totals.GainKm,
            AltitudeLossKm = totals.LossKm,
            Publish = false,
            Creator = creator,
            FileName = fileName,
            Provenance = Provenance(fileName, creator)
        };
    }

    public static (double DistanceKm, double GainKm, double LossKm) Measure(IReadOnlyList<Waypoint> waypoints)
    {
        var distance = 0.0;
        if (waypoints.Count > 0 && waypoints.Any(point => point.ElapsedDistanceKm > 0))
            distance = waypoints.Max(point => point.ElapsedDistanceKm);
        else
        {
            for (var i = 1; i < waypoints.Count; i++)
                distance += GeoMath.DistanceMeters(waypoints[i - 1].Latitude, waypoints[i - 1].Longitude, waypoints[i].Latitude, waypoints[i].Longitude) / 1000.0;
        }

        var gain = 0.0;
        var loss = 0.0;
        double? previous = null;
        foreach (var point in waypoints)
        {
            if (point.AltitudeKm == Altitude.Unknown.Kilometers)
                continue;
            if (previous is null)
            {
                previous = point.AltitudeKm;
                continue;
            }
            var delta = point.AltitudeKm - previous.Value;
            if (Math.Abs(delta) <= AltitudeHysteresisKm)
                continue;
            if (delta > 0)
                gain += delta;
            else
                loss -= delta;
            previous = point.AltitudeKm;
        }
        return (distance, gain, loss);
    }

    public static void Validate(string xml)
    {
        var extensionNamespace = IntervalNamespace;
        const string marker = "xmlschemas/gpx/1";
        var index = xml.IndexOf(marker, StringComparison.Ordinal);
        if (index > 0)
        {
            var start = xml.LastIndexOf('"', index);
            var end = xml.IndexOf('"', index);
            if (start >= 0 && end > start)
                extensionNamespace = xml[(start + 1)..end];
        }
        var schemas = new XmlSchemaSet();
        schemas.Add(GpxNamespace, XmlReader.Create(new StringReader(GpxSchema)));
        schemas.Add(extensionNamespace, XmlReader.Create(new StringReader(ExtensionSchema(extensionNamespace))));
        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = schemas,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };
        var errors = new List<string>();
        settings.ValidationEventHandler += (_, args) => errors.Add(args.Message);
        using var reader = XmlReader.Create(new StringReader(xml), settings);
        while (reader.Read())
        {
        }
        if (errors.Count > 0)
            throw new XmlSchemaValidationException(string.Join("; ", errors));
    }

    private static List<Waypoint> ExactWaypoints(List<RawPoint> raw, List<XElement?> extensions)
    {
        var waypoints = new List<Waypoint>(raw.Count);
        for (var i = 0; i < raw.Count; i++)
        {
            var point = raw[i];
            var extension = extensions[i];
            var type = WaypointType.Continue;
            var ticks = (i + 1L) * 1000;
            var elapsed = point.ElapsedKm;
            var heading = point.Heading;
            string? text = point.Text;
            if (extension is not null)
            {
                if (Enum.TryParse<WaypointType>(Attribute(extension, "type"), out var parsed))
                    type = parsed;
                if (long.TryParse(Attribute(extension, "ticks"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var storedTicks))
                    ticks = storedTicks;
                if (double.TryParse(Attribute(extension, "elapsedKm"), NumberStyles.Float, CultureInfo.InvariantCulture, out var storedElapsed))
                    elapsed = storedElapsed;
                if (double.TryParse(Attribute(extension, "heading"), NumberStyles.Float, CultureInfo.InvariantCulture, out var storedHeading))
                    heading = storedHeading;
                text = Attribute(extension, "text");
            }
            waypoints.Add(new Waypoint
            {
                Latitude = point.Latitude,
                Longitude = point.Longitude,
                AltitudeKm = point.AltitudeKm,
                WaypointType = type,
                TimestampTicks = ticks,
                ElapsedDistanceKm = elapsed,
                HeadingDegrees = heading,
                TextDescription = text
            });
        }
        return waypoints;
    }

    private static List<RawPoint> Thin(List<RawPoint> points, double toleranceMeters)
    {
        if (points.Count <= 2)
            return points;
        var forced = new SortedSet<int> { 0, points.Count - 1 };
        for (var i = 1; i < points.Count - 1; i++)
        {
            var incoming = GeoMath.InitialBearingDegrees(points[i - 1].Latitude, points[i - 1].Longitude, points[i].Latitude, points[i].Longitude);
            var outgoing = GeoMath.InitialBearingDegrees(points[i].Latitude, points[i].Longitude, points[i + 1].Latitude, points[i + 1].Longitude);
            if (Math.Abs(SignedDelta(outgoing, incoming)) >= HeadingKeepDegrees)
                forced.Add(i);
        }
        var keep = new SortedSet<int>();
        var keys = forced.ToList();
        for (var segment = 0; segment < keys.Count - 1; segment++)
            DouglasPeucker(points, keys[segment], keys[segment + 1], toleranceMeters, keep);
        keep.UnionWith(forced);
        return keep.Select(index => points[index]).ToList();
    }

    private static void DouglasPeucker(List<RawPoint> points, int start, int end, double toleranceMeters, ISet<int> keep)
    {
        var stack = new Stack<(int Start, int End)>();
        stack.Push((start, end));
        while (stack.Count > 0)
        {
            var (from, to) = stack.Pop();
            keep.Add(from);
            keep.Add(to);
            if (to <= from + 1)
                continue;
            var farthest = -1;
            var farthestMeters = 0.0;
            for (var i = from + 1; i < to; i++)
            {
                var meters = PerpendicularMeters(points[from], points[to], points[i]);
                if (meters > farthestMeters)
                {
                    farthestMeters = meters;
                    farthest = i;
                }
            }
            if (farthest >= 0 && farthestMeters > toleranceMeters)
            {
                stack.Push((from, farthest));
                stack.Push((farthest, to));
            }
        }
    }

    private static List<Waypoint> BuildWaypoints(List<RawPoint> points)
    {
        var smoothed = Smooth(points.Select(point => point.HasAltitude ? point.AltitudeMeters : double.NaN).ToArray());
        var waypoints = new List<Waypoint>(points.Count);
        var elapsed = 0.0;
        for (var i = 0; i < points.Count; i++)
        {
            if (i > 0)
                elapsed += GeoMath.DistanceMeters(points[i - 1].Latitude, points[i - 1].Longitude, points[i].Latitude, points[i].Longitude) / 1000.0;
            var heading = i < points.Count - 1
                ? GeoMath.InitialBearingDegrees(points[i].Latitude, points[i].Longitude, points[i + 1].Latitude, points[i + 1].Longitude)
                : i > 0
                    ? GeoMath.InitialBearingDegrees(points[i - 1].Latitude, points[i - 1].Longitude, points[i].Latitude, points[i].Longitude)
                    : 0;
            var type = WaypointType.Continue;
            if (i > 0 && i < points.Count - 1)
            {
                var incoming = GeoMath.InitialBearingDegrees(points[i - 1].Latitude, points[i - 1].Longitude, points[i].Latitude, points[i].Longitude);
                type = ClassifyTurn(SignedDelta(heading, incoming));
            }
            waypoints.Add(new Waypoint
            {
                Latitude = points[i].Latitude,
                Longitude = points[i].Longitude,
                AltitudeKm = double.IsNaN(smoothed[i]) ? Altitude.Unknown.Kilometers : smoothed[i] / 1000.0,
                ElapsedDistanceKm = elapsed,
                HeadingDegrees = heading,
                WaypointType = type,
                TimestampTicks = (i + 1L) * 1000,
                TextDescription = points[i].Text
            });
        }
        if (waypoints.Count > 0)
        {
            waypoints[0].WaypointType = WaypointType.Start;
            waypoints[^1].WaypointType = WaypointType.Finish;
        }
        return waypoints;
    }

    private static void MarkNamedPoints(List<Waypoint> waypoints)
    {
        foreach (var waypoint in waypoints)
        {
            if (waypoint.WaypointType is WaypointType.Start or WaypointType.Finish)
                continue;
            if (!string.IsNullOrWhiteSpace(waypoint.TextDescription))
                waypoint.WaypointType = WaypointType.Announcement;
        }
    }

    private static List<Waypoint> InsertAnnouncements(List<Waypoint> route, List<RawPoint> announcements)
    {
        var result = route.ToList();
        foreach (var announcement in announcements)
        {
            var nearest = 0;
            var nearestMeters = double.MaxValue;
            for (var i = 0; i < result.Count; i++)
            {
                var meters = GeoMath.DistanceMeters(result[i].Latitude, result[i].Longitude, announcement.Latitude, announcement.Longitude);
                if (meters < nearestMeters)
                {
                    nearestMeters = meters;
                    nearest = i;
                }
            }
            if (nearestMeters <= MaxToleranceMeters)
            {
                if (result[nearest].WaypointType is WaypointType.Start or WaypointType.Finish)
                    result[nearest].TextDescription = Combine(result[nearest].TextDescription, announcement.Text);
                else
                {
                    result[nearest].WaypointType = WaypointType.Announcement;
                    result[nearest].TextDescription = announcement.Text;
                }
                continue;
            }

            var elapsed = ProjectElapsed(result, announcement.Latitude, announcement.Longitude);
            var insertAt = result.FindIndex(point => point.ElapsedDistanceKm > elapsed);
            if (insertAt < 0)
                insertAt = result.Count;
            var before = insertAt > 0 ? result[insertAt - 1] : null;
            var after = insertAt < result.Count ? result[insertAt] : null;
            var ticks = before is null ? 500 : after is null ? before.TimestampTicks + 500 : before.TimestampTicks + Math.Max(1, (after.TimestampTicks - before.TimestampTicks) / 2);
            var heading = after is not null
                ? GeoMath.InitialBearingDegrees(announcement.Latitude, announcement.Longitude, after.Latitude, after.Longitude)
                : before?.HeadingDegrees ?? 0;
            result.Insert(insertAt, new Waypoint
            {
                Latitude = announcement.Latitude,
                Longitude = announcement.Longitude,
                AltitudeKm = announcement.AltitudeKm,
                ElapsedDistanceKm = elapsed,
                HeadingDegrees = heading,
                WaypointType = WaypointType.Announcement,
                TimestampTicks = ticks,
                TextDescription = announcement.Text
            });
        }
        return result;
    }

    private static double ProjectElapsed(List<Waypoint> route, double latitude, double longitude)
    {
        if (route.Count == 0)
            return 0;
        if (route.Count == 1)
            return route[0].ElapsedDistanceKm;
        var best = route[0].ElapsedDistanceKm;
        var bestMeters = double.MaxValue;
        for (var i = 1; i < route.Count; i++)
        {
            var start = route[i - 1];
            var end = route[i];
            var fraction = SegmentFraction(start, end, latitude, longitude);
            var along = start.ElapsedDistanceKm + (end.ElapsedDistanceKm - start.ElapsedDistanceKm) * fraction;
            var lat = start.Latitude + (end.Latitude - start.Latitude) * fraction;
            var lon = start.Longitude + (end.Longitude - start.Longitude) * fraction;
            var meters = GeoMath.DistanceMeters(lat, lon, latitude, longitude);
            if (meters < bestMeters)
            {
                bestMeters = meters;
                best = along;
            }
        }
        return best;
    }

    private static double SegmentFraction(Waypoint start, Waypoint end, double latitude, double longitude)
    {
        var metersPerLat = 111_320.0;
        var metersPerLon = 111_320.0 * Math.Cos(start.Latitude * Math.PI / 180.0);
        var abx = (end.Longitude - start.Longitude) * metersPerLon;
        var aby = (end.Latitude - start.Latitude) * metersPerLat;
        var apx = (longitude - start.Longitude) * metersPerLon;
        var apy = (latitude - start.Latitude) * metersPerLat;
        var length = abx * abx + aby * aby;
        if (length < 1e-6)
            return 0;
        return Math.Clamp((apx * abx + apy * aby) / length, 0, 1);
    }

    private static double[] Smooth(double[] meters)
    {
        if (meters.Length < 3)
            return meters;
        var smoothed = (double[])meters.Clone();
        for (var i = 1; i < meters.Length - 1; i++)
        {
            if (double.IsNaN(meters[i]))
                continue;
            double sum = 0;
            var count = 0;
            for (var j = i - 1; j <= i + 1; j++)
            {
                if (double.IsNaN(meters[j]))
                    continue;
                sum += meters[j];
                count++;
            }
            if (count > 0)
                smoothed[i] = sum / count;
        }
        return smoothed;
    }

    private static double PerpendicularMeters(RawPoint start, RawPoint end, RawPoint point)
    {
        var metersPerLat = 111_320.0;
        var metersPerLon = 111_320.0 * Math.Cos(start.Latitude * Math.PI / 180.0);
        var abx = (end.Longitude - start.Longitude) * metersPerLon;
        var aby = (end.Latitude - start.Latitude) * metersPerLat;
        var apx = (point.Longitude - start.Longitude) * metersPerLon;
        var apy = (point.Latitude - start.Latitude) * metersPerLat;
        var length = abx * abx + aby * aby;
        if (length < 1e-6)
            return GeoMath.DistanceMeters(start.Latitude, start.Longitude, point.Latitude, point.Longitude);
        var t = Math.Clamp((apx * abx + apy * aby) / length, 0, 1);
        var dx = apx - abx * t;
        var dy = apy - aby * t;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double SignedDelta(double outgoing, double incoming)
    {
        var delta = outgoing - incoming;
        while (delta > 180)
            delta -= 360;
        while (delta < -180)
            delta += 360;
        return delta;
    }

    private static RawPoint ReadRaw(XElement point)
    {
        var latitude = double.Parse(Required(point, "lat"), CultureInfo.InvariantCulture);
        var longitude = double.Parse(Required(point, "lon"), CultureInfo.InvariantCulture);
        var altitude = 0.0;
        var hasAltitude = false;
        var ele = point.Elements().FirstOrDefault(element => element.Name.LocalName == "ele");
        if (ele is not null && double.TryParse(ele.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var meters))
        {
            altitude = meters;
            hasAltitude = true;
        }
        return new RawPoint(latitude, longitude, altitude, PointText(point), hasAltitude);
    }

    private static CourseIntervalDefinition ReadInterval(XElement item) => new()
    {
        IntervalId = Guid.TryParse(Attribute(item, "id"), out var id) ? id : Guid.NewGuid(),
        Ordinal = int.Parse(Attribute(item, "ordinal") ?? "0", CultureInfo.InvariantCulture),
        Name = Attribute(item, "name") ?? "",
        StartElapsedKm = double.Parse(Attribute(item, "startElapsedKm") ?? "0", CultureInfo.InvariantCulture),
        FinishElapsedKm = double.Parse(Attribute(item, "finishElapsedKm") ?? "0", CultureInfo.InvariantCulture),
        StartTimestampTicks = long.Parse(Attribute(item, "startTicks") ?? "0", CultureInfo.InvariantCulture),
        FinishTimestampTicks = long.Parse(Attribute(item, "finishTicks") ?? "0", CultureInfo.InvariantCulture)
    };

    private static (double DistanceKm, double GainKm, double LossKm)? ReadStoredTotals(XElement root)
    {
        var course = root.Descendants().FirstOrDefault(element => element.Name.LocalName == "course" && IsTrainerNamespace(element.Name.NamespaceName));
        if (course is null)
            return null;
        double.TryParse(Attribute(course, "distanceKm"), NumberStyles.Float, CultureInfo.InvariantCulture, out var distance);
        double.TryParse(Attribute(course, "altitudeGainKm"), NumberStyles.Float, CultureInfo.InvariantCulture, out var gain);
        double.TryParse(Attribute(course, "altitudeLossKm"), NumberStyles.Float, CultureInfo.InvariantCulture, out var loss);
        return (distance, gain, loss);
    }

    private static XElement? TrainerChild(XElement point)
    {
        var extensions = point.Elements().FirstOrDefault(element => element.Name.LocalName == "extensions");
        return extensions?.Elements().FirstOrDefault(element => element.Name.LocalName == "waypoint" && IsTrainerNamespace(element.Name.NamespaceName));
    }

    private static bool IsTrainerNamespace(string ns) =>
        ns.EndsWith(ExtensionSuffix, StringComparison.OrdinalIgnoreCase)
        || ns.Equals(LegacyExtensionNamespace, StringComparison.OrdinalIgnoreCase);

    private static string? PointText(XElement point)
    {
        var name = ChildValue(point, "name");
        var description = ChildValue(point, "desc");
        if (string.IsNullOrWhiteSpace(name))
            return string.IsNullOrWhiteSpace(description) ? null : description;
        if (string.IsNullOrWhiteSpace(description))
            return name;
        return name + ": " + description;
    }

    private static string? Combine(string? current, string? added)
    {
        if (string.IsNullOrWhiteSpace(added))
            return current;
        if (string.IsNullOrWhiteSpace(current))
            return added;
        return current + "; " + added;
    }

    private static string Provenance(string? fileName, string? creator)
    {
        var note = "Imported";
        if (!string.IsNullOrWhiteSpace(fileName))
            note += " from " + fileName;
        if (!string.IsNullOrWhiteSpace(creator))
            note += " (" + creator.Trim() + ")";
        if (note == "Imported")
            note = "Imported GPX";
        return note.Length <= 256 ? note : note[..256];
    }

    private static string? CleanFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        name = Path.GetFileName(name.Replace('\\', '/').Trim());
        if (name.Length > 80)
            name = name[..80];
        return name;
    }

    private static IEnumerable<XElement> LocalDescendants(XElement root, string localName) =>
        root.Descendants().Where(element => element.Name.LocalName == localName);

    private static string? ChildValue(XElement element, string localName) =>
        element.Elements().FirstOrDefault(child => child.Name.LocalName == localName)?.Value;

    private static string? ChildValue(XElement element, string childName, string grandchildName)
    {
        var child = element.Elements().FirstOrDefault(candidate => candidate.Name.LocalName == childName);
        return child is null ? null : ChildValue(child, grandchildName);
    }

    private static string Required(XElement element, string name) =>
        element.Attribute(name)?.Value ?? throw new InvalidOperationException("A point is missing " + name + ".");

    private static string? Attribute(XElement element, string name) => element.Attribute(name)?.Value;

    private readonly record struct RawPoint(double Latitude, double Longitude, double AltitudeMeters, string? Text, bool HasAltitude)
    {
        public double AltitudeKm => HasAltitude ? AltitudeMeters / 1000.0 : Altitude.Unknown.Kilometers;
        public double ElapsedKm => 0;
        public double Heading => 0;
    }

    private const string GpxSchema = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="http://www.topografix.com/GPX/1/1" xmlns="http://www.topografix.com/GPX/1/1" elementFormDefault="qualified">
          <xs:element name="gpx">
            <xs:complexType>
              <xs:sequence>
                <xs:element name="metadata" minOccurs="0">
                  <xs:complexType>
                    <xs:sequence>
                      <xs:element name="name" type="xs:string" minOccurs="0"/>
                    </xs:sequence>
                  </xs:complexType>
                </xs:element>
                <xs:element name="trk" maxOccurs="unbounded">
                  <xs:complexType>
                    <xs:sequence>
                      <xs:element name="name" type="xs:string" minOccurs="0"/>
                      <xs:element name="trkseg">
                        <xs:complexType>
                          <xs:sequence>
                            <xs:element name="trkpt" maxOccurs="unbounded">
                              <xs:complexType>
                                <xs:sequence>
                                  <xs:element name="ele" type="xs:decimal" minOccurs="0"/>
                                  <xs:element name="extensions" type="Extensions" minOccurs="0"/>
                                </xs:sequence>
                                <xs:attribute name="lat" type="xs:decimal" use="required"/>
                                <xs:attribute name="lon" type="xs:decimal" use="required"/>
                              </xs:complexType>
                            </xs:element>
                          </xs:sequence>
                        </xs:complexType>
                      </xs:element>
                      <xs:element name="extensions" type="Extensions" minOccurs="0"/>
                    </xs:sequence>
                  </xs:complexType>
                </xs:element>
              </xs:sequence>
              <xs:attribute name="version" type="xs:string" use="required"/>
              <xs:attribute name="creator" type="xs:string"/>
            </xs:complexType>
          </xs:element>
          <xs:complexType name="Extensions">
            <xs:sequence>
              <xs:any namespace="##other" processContents="lax" minOccurs="0" maxOccurs="unbounded"/>
            </xs:sequence>
          </xs:complexType>
        </xs:schema>
        """;

    private static string ExtensionSchema(string targetNamespace) =>
        $"""
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="{targetNamespace}" elementFormDefault="qualified">
          <xs:element name="waypoint">
            <xs:complexType>
              <xs:attribute name="type" type="xs:string" use="required"/>
              <xs:attribute name="ticks" type="xs:long" use="required"/>
              <xs:attribute name="elapsedKm" type="xs:decimal" use="required"/>
              <xs:attribute name="heading" type="xs:decimal" use="required"/>
              <xs:attribute name="text" type="xs:string" use="required"/>
            </xs:complexType>
          </xs:element>
          <xs:element name="course">
            <xs:complexType>
              <xs:attribute name="distanceKm" type="xs:decimal" use="required"/>
              <xs:attribute name="altitudeGainKm" type="xs:decimal" use="required"/>
              <xs:attribute name="altitudeLossKm" type="xs:decimal" use="required"/>
            </xs:complexType>
          </xs:element>
          <xs:element name="intervals">
            <xs:complexType>
              <xs:sequence>
                <xs:element name="interval" maxOccurs="unbounded">
                  <xs:complexType>
                    <xs:attribute name="id" type="xs:string" use="required"/>
                    <xs:attribute name="ordinal" type="xs:int" use="required"/>
                    <xs:attribute name="name" type="xs:string" use="required"/>
                    <xs:attribute name="startElapsedKm" type="xs:decimal" use="required"/>
                    <xs:attribute name="finishElapsedKm" type="xs:decimal" use="required"/>
                    <xs:attribute name="startTicks" type="xs:long" use="required"/>
                    <xs:attribute name="finishTicks" type="xs:long" use="required"/>
                  </xs:complexType>
                </xs:element>
              </xs:sequence>
            </xs:complexType>
          </xs:element>
          <xs:element name="interval">
            <xs:complexType>
              <xs:attribute name="id" type="xs:string" use="required"/>
              <xs:attribute name="ordinal" type="xs:int" use="required"/>
              <xs:attribute name="name" type="xs:string" use="required"/>
              <xs:attribute name="startElapsedKm" type="xs:decimal" use="required"/>
              <xs:attribute name="finishElapsedKm" type="xs:decimal" use="required"/>
              <xs:attribute name="startTicks" type="xs:long" use="required"/>
              <xs:attribute name="finishTicks" type="xs:long" use="required"/>
            </xs:complexType>
          </xs:element>
        </xs:schema>
        """;
}

public sealed class GpxImportOptions
{
    public double ToleranceMeters { get; init; } = GpxCourse.DefaultToleranceMeters;
    public string? FileName { get; init; }
}

public sealed class GpxCourseDocument
{
    public string Name { get; set; } = "";
    public List<Waypoint> Waypoints { get; set; } = [];
    public List<CourseIntervalDefinition> Intervals { get; set; } = [];
    public bool HasGpsTrainerExtensions { get; set; }
    public double ElapsedDistanceKm { get; set; }
    public double AltitudeGainKm { get; set; }
    public double AltitudeLossKm { get; set; }
    public bool Publish { get; set; }
    public string? Creator { get; set; }
    public string? FileName { get; set; }
    public string? Provenance { get; set; }
}

public sealed class GpxImportResult
{
    public Course Course { get; set; } = new();
    public List<Waypoint> Waypoints { get; set; } = [];
    public List<CourseIntervalDefinition> Intervals { get; set; } = [];
    public bool PreservedGpsTrainerExtensions { get; set; }
}
