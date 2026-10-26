using GpsTrainer;

namespace GpsTrainer.Shared.Tests;

public class MapContextMenuTests
{
    [Fact]
    public void The_last_point_menu_matches_the_2013_order()
    {
        var rows = MapContextMenu.For(true, "10 mi FINISH");
        Assert.Equal(
            ["CONTINUE", "ROUTE", "TURN RIGHT", "TURN LEFT", "FINISH", "—", "10 mi FINISH", "—", "ZOOM", "MOVE"],
            rows.Select(row => row.Label).ToArray());
        Assert.Equal("route", rows[1].Action);
        Assert.Equal("TurnLeft", rows.Single(row => row.Label == "TURN LEFT").Type);
        Assert.DoesNotContain(rows, row => row.Type is "BearLeft" or "BearRight");
    }

    [Fact]
    public void An_earlier_point_omits_route_finish_and_the_standard_finish()
    {
        var rows = MapContextMenu.For(false, "5 km FINISH");
        Assert.Equal(
            ["CONTINUE", "TURN RIGHT", "TURN LEFT", "ZOOM", "MOVE"],
            rows.Select(row => row.Label).ToArray());
    }
}
