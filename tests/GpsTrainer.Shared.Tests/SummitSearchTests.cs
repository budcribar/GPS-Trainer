using GpsTrainer;

namespace GpsTrainer.Shared.Tests;

public class SummitSearchTests
{
    [Fact]
    public async Task A_peak_outside_the_initial_window_is_climbed_in_batched_50_foot_steps()
    {
        const double click = 0.1;
        const double summit = 0.85;
        var (hit, calls) = await Search(click, 2.5, elapsed => Ridge(elapsed, summit));

        Assert.NotNull(hit);
        Assert.InRange(hit!.ElapsedKm, summit - 0.01, summit + 0.01);
        Assert.True(hit.ElapsedKm > click + SummitSearch.InitialWindowKm + 0.05);
        Assert.InRange(hit.AltitudeKm, Ridge(summit, summit) - 0.02, Ridge(summit, summit) + 0.01);
        Assert.InRange(calls.Count, 2, 3);
        Assert.Contains(calls, StepBatch);
        Assert.DoesNotContain(calls, call => call.Count <= 1);
    }

    [Fact]
    public async Task A_noisy_bump_does_not_hide_the_higher_summit_past_the_window()
    {
        const double click = 0.12;
        const double bump = 0.12;
        const double summit = 0.8;
        var (hit, calls) = await Search(click, 2.5, elapsed =>
        {
            var altitude = Ridge(elapsed, summit);
            if (Math.Abs(elapsed - bump) <= 0.002)
                altitude += 0.008;
            return altitude;
        });

        Assert.NotNull(hit);
        Assert.InRange(hit!.ElapsedKm, summit - 0.01, summit + 0.01);
        Assert.True(Math.Abs(hit.ElapsedKm - bump) > 0.05);
        Assert.True(hit.ElapsedKm > click + SummitSearch.InitialWindowKm);
        Assert.InRange(calls.Count, 2, 3);
        Assert.Contains(calls, StepBatch);
    }

    [Fact]
    public async Task A_plateau_lands_in_the_middle()
    {
        var (hit, calls) = await Search(0.5, 1.2, elapsed => elapsed is >= 0.2 and <= 0.8 ? 2.0 : 1.0);

        Assert.NotNull(hit);
        Assert.InRange(hit!.ElapsedKm, 0.49, 0.51);
        Assert.Equal(2.0, hit.AltitudeKm, 5);
        Assert.True(hit.Plateau);
        Assert.True(calls.Count <= 2);
        Assert.DoesNotContain(calls, call => call.Count <= 1);
    }

    [Fact]
    public async Task Unknown_altitudes_are_skipped_and_a_known_peak_wins()
    {
        var (hit, _) = await Search(0.30, 1.0, elapsed =>
        {
            if (Math.Abs(elapsed - 0.32) <= 0.002)
                return 1.8;
            if (Math.Abs(elapsed - 0.15) <= 0.002)
                return 1.2;
            if (Math.Abs(elapsed - 0.40) <= 0.002)
                return Altitude.Unknown.Kilometers;
            return null;
        });

        Assert.NotNull(hit);
        Assert.InRange(hit!.ElapsedKm, 0.31, 0.33);
        Assert.Equal(1.8, hit.AltitudeKm, 5);
        Assert.NotEqual(Altitude.Unknown.Kilometers, hit.AltitudeKm);

        var missing = await SummitSearch.FindAsync(0.2, 1.0, stations =>
            Task.FromResult<IReadOnlyList<double?>>(stations.Select(_ => (double?)Altitude.Unknown.Kilometers).ToList()));
        Assert.Null(missing);
    }

    [Fact]
    public async Task A_smooth_crest_is_pinpointed_between_the_dense_samples()
    {
        var (hit, _) = await Search(0.203, 1.0, elapsed => 2.0 - 10000.0 * (elapsed - 0.203) * (elapsed - 0.203));

        Assert.NotNull(hit);
        Assert.InRange(hit!.ElapsedKm, 0.201, 0.205);
        Assert.True(Math.Abs(hit.ElapsedKm - 0.203) < 0.002);
        Assert.False(hit.Plateau);
    }

    [Fact]
    public void PointAt_interpolates_only_when_both_ends_are_known()
    {
        var end = GeoMath.Translate(40, -105, 0, 1);
        var line = new List<Waypoint>
        {
            new() { Latitude = 40, Longitude = -105, AltitudeKm = 1, WaypointType = WaypointType.Start },
            new() { Latitude = end.Latitude, Longitude = end.Longitude, AltitudeKm = 3, WaypointType = WaypointType.Finish }
        };
        var middle = SummitSearch.PointAt(line, 0.5);
        Assert.InRange(middle.Latitude, 40, end.Latitude);
        Assert.Equal(-105, middle.Longitude, 4);
        Assert.Equal(2, middle.AltitudeKm, 2);

        line[0].AltitudeKm = Altitude.Unknown.Kilometers;
        var unknown = SummitSearch.PointAt(line, 0.5);
        Assert.Equal(Altitude.Unknown.Kilometers, unknown.AltitudeKm);
        Assert.False(SummitSearch.IsKnown(unknown.AltitudeKm));
    }

    [Fact]
    public void InsertSummit_keeps_travel_order_and_updates_a_nearby_summit()
    {
        var end = GeoMath.Translate(40, -105, 0, 1);
        var sketch = new CourseSketch();
        sketch.Replace(
        [
            new Waypoint { Latitude = 40, Longitude = -105, AltitudeKm = 1, WaypointType = WaypointType.Start, TimestampTicks = 1 },
            new Waypoint { Latitude = end.Latitude, Longitude = end.Longitude, AltitudeKm = 1.2, WaypointType = WaypointType.Finish, TimestampTicks = 2 }
        ]);
        var point = SummitSearch.PointAt(sketch.Waypoints, 0.4);
        var edit = sketch.InsertSummit(point.Latitude, point.Longitude, 0.4, 1.5);
        Assert.True(edit.Ok);
        Assert.Empty(edit.Gaps);
        Assert.Equal(WaypointType.Summit, sketch.Waypoints[1].WaypointType);
        Assert.Equal(1.5, sketch.Waypoints[1].AltitudeKm, 5);
        Assert.InRange(sketch.Waypoints[1].ElapsedDistanceKm, 0.35, 0.45);
        Assert.Equal(WaypointType.Finish, sketch.Waypoints[2].WaypointType);

        Assert.True(sketch.Undo());
        Assert.Equal(2, sketch.Waypoints.Count);
        Assert.DoesNotContain(sketch.Waypoints, item => item.WaypointType == WaypointType.Summit);

        sketch.InsertSummit(point.Latitude, point.Longitude, 0.4, 1.5);
        var nudged = GeoMath.Translate(point.Latitude, point.Longitude, 90, 0.003);
        var updated = sketch.InsertSummit(nudged.Latitude, nudged.Longitude, 0.41, 1.7);
        Assert.True(updated.Ok);
        Assert.Equal(3, sketch.Waypoints.Count);
        Assert.Equal(1, sketch.Waypoints.Count(item => item.WaypointType == WaypointType.Summit));
        Assert.Equal(1.7, sketch.Waypoints.Single(item => item.WaypointType == WaypointType.Summit).AltitudeKm, 5);

        var parked = GeoMath.Translate(40, -105, 0, 0.8);
        var parkedSummit = sketch.Waypoints.Single(item => item.WaypointType == WaypointType.Summit);
        parkedSummit.Latitude = parked.Latitude;
        parkedSummit.Longitude = parked.Longitude;
        var start = sketch.Waypoints[0];
        var added = sketch.InsertSummit(start.Latitude, start.Longitude, 0, 1.8);
        Assert.True(added.Ok);
        Assert.Equal(WaypointType.Start, sketch.Waypoints[0].WaypointType);
        Assert.Equal(2, sketch.Waypoints.Count(item => item.WaypointType == WaypointType.Summit));
    }

    [Fact]
    public void Profile_distance_maps_back_to_kilometers()
    {
        Assert.Equal(0, SummitSearch.ProfileElapsedKm(68, 0, 10), 5);
        Assert.Equal(10, SummitSearch.ProfileElapsedKm(742, 0, 10), 5);
        Assert.Equal(5, SummitSearch.ProfileElapsedKm(405, 0, 10), 5);

        var end = GeoMath.Translate(40, -105, 0, 1);
        var plot = ElevationProfile.Plot(
        [
            new Waypoint { Latitude = 40, Longitude = -105, AltitudeKm = 1.5, WaypointType = WaypointType.Start },
            new Waypoint { Latitude = end.Latitude, Longitude = end.Longitude, AltitudeKm = 1.6, WaypointType = WaypointType.Finish }
        ], true, "Ridge");
        Assert.InRange(plot.MinDistanceKm, -0.001, 0.001);
        Assert.InRange(plot.MaxDistanceKm, 0.99, 1.01);
    }

    private static async Task<(SummitHit? Hit, List<IReadOnlyList<double>> Calls)> Search(
        double clickKm, double lengthKm, Func<double, double?> profile)
    {
        var calls = new List<IReadOnlyList<double>>();
        var hit = await SummitSearch.FindAsync(clickKm, lengthKm, stations =>
        {
            calls.Add(stations.ToList());
            IReadOnlyList<double?> measured = stations.Select(profile).ToList();
            return Task.FromResult(measured);
        });
        return (hit, calls);
    }

    private static double Ridge(double elapsedKm, double summitKm) =>
        1.0 + 0.4 * (summitKm - Math.Abs(elapsedKm - summitKm));

    private static bool StepBatch(IReadOnlyList<double> call)
    {
        if (call.Count < 20)
            return false;
        var gaps = new List<double>();
        for (var i = 1; i < call.Count; i++)
            gaps.Add(Math.Abs(call[i] - call[i - 1]));
        gaps.Sort();
        return Math.Abs(gaps[gaps.Count / 2] - SummitSearch.StepKm) < 0.0002;
    }
}
