namespace GpsTrainer.Shared.Tests;

public class GpsInfoTextTests
{
    private static readonly GpsInfoSample Fix = new(
        Latitude: 40.015,
        Longitude: -105.27,
        AltitudeMeters: 1609.344,
        SpeedMetersPerSecond: 4.4704,
        AccuracyMeters: 5,
        SatellitesInView: 12,
        SatellitesUsed: 8,
        Approximate: false,
        Simulated: false);

    [Fact]
    public void A_fix_lists_position_altitude_speed_accuracy_and_satellites()
    {
        var view = GpsInfoText.Present(GpsInfoBlocker.None, Fix, metric: false);

        Assert.Equal("", view.Message);
        Assert.False(view.ShowSettings);
        Assert.Equal(
            ["Latitude", "Longitude", "Altitude", "Speed", "Accuracy", "Satellites", "Status"],
            view.Rows.Select(row => row.Label).ToArray());
        Assert.Equal("40.01500", Row(view, "Latitude"));
        Assert.Equal("-105.27000", Row(view, "Longitude"));
        Assert.Equal("5280 ft", Row(view, "Altitude"));
        Assert.Equal("10.0 mph", Row(view, "Speed"));
        Assert.Equal("±5 m", Row(view, "Accuracy"));
        Assert.Equal("8 used, 12 in view", Row(view, "Satellites"));
        Assert.Equal("Fix", Row(view, "Status"));
    }

    [Fact]
    public void Metric_uses_meters_and_kph()
    {
        var view = GpsInfoText.Present(GpsInfoBlocker.None, Fix, metric: true);

        Assert.Equal("1609 m", Row(view, "Altitude"));
        Assert.Equal("16.1 kph", Row(view, "Speed"));
    }

    [Fact]
    public void Permission_denied_explains_and_offers_settings()
    {
        var view = GpsInfoText.Present(GpsInfoBlocker.PermissionDenied, Fix, metric: false);

        Assert.Equal(GpsInfoText.PermissionDenied, view.Message);
        Assert.True(view.ShowSettings);
        Assert.Empty(view.Rows);
    }

    [Fact]
    public void Location_off_explains_and_offers_settings()
    {
        var view = GpsInfoText.Present(GpsInfoBlocker.LocationOff, null, metric: true);

        Assert.Equal(GpsInfoText.LocationOff, view.Message);
        Assert.True(view.ShowSettings);
        Assert.Empty(view.Rows);
    }

    [Fact]
    public void Waiting_has_no_settings_button()
    {
        var view = GpsInfoText.Present(GpsInfoBlocker.None, null, metric: false);

        Assert.Equal(GpsInfoText.Waiting, view.Message);
        Assert.False(view.ShowSettings);
        Assert.Empty(view.Rows);
    }

    [Fact]
    public void Approximate_and_simulated_fixes_say_so()
    {
        var approximate = Fix with { Approximate = true, SatellitesInView = 0, SatellitesUsed = 0 };
        var simulated = Fix with { Simulated = true };

        var approxView = GpsInfoText.Present(GpsInfoBlocker.None, approximate, metric: true);
        var simView = GpsInfoText.Present(GpsInfoBlocker.None, simulated, metric: true);

        Assert.Equal("Approximate location. Precise location is off.", approxView.Message);
        Assert.True(approxView.ShowSettings);
        Assert.Equal("Approximate", Row(approxView, "Status"));
        Assert.Equal("—", Row(approxView, "Satellites"));
        Assert.Equal("Simulated position.", simView.Message);
        Assert.Equal("Simulated fix", Row(simView, "Status"));
    }

    [Fact]
    public void Missing_altitude_and_speed_stay_blank()
    {
        var sample = Fix with { AltitudeMeters = null, SpeedMetersPerSecond = null, AccuracyMeters = null };
        var view = GpsInfoText.Present(GpsInfoBlocker.None, sample, metric: true);

        Assert.Equal("—", Row(view, "Altitude"));
        Assert.Equal("—", Row(view, "Speed"));
        Assert.Equal("—", Row(view, "Accuracy"));
    }

    private static string Row(GpsInfoView view, string label) =>
        view.Rows.Single(row => row.Label == label).Value;
}
