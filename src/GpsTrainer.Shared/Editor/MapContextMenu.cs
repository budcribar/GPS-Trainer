namespace GpsTrainer;

/// <summary>
/// The 2013 map right-click rows. Insert rows add a point. Bear left and bear right stay off the menu.
/// A rule is a separator. The last-point menu adds ROUTE, FINISH, and the standard-distance finish.
/// </summary>
public static class MapContextMenu
{
    public sealed record Row(string Label, string Action, string? Type = null);

    public static IReadOnlyList<Row> For(bool lastPointSelected, string standardFinish)
    {
        if (lastPointSelected)
        {
            return
            [
                new("CONTINUE", "insert", "Continue"),
                new("ROUTE", "route", "Continue"),
                new("TURN RIGHT", "insert", "TurnRight"),
                new("TURN LEFT", "insert", "TurnLeft"),
                new("FINISH", "insert", "Finish"),
                new("—", "rule"),
                new(standardFinish, "finish"),
                new("—", "rule"),
                new("ZOOM", "zoom"),
                new("MOVE", "move")
            ];
        }

        return
        [
            new("CONTINUE", "insert", "Continue"),
            new("TURN RIGHT", "insert", "TurnRight"),
            new("TURN LEFT", "insert", "TurnLeft"),
            new("ZOOM", "zoom"),
            new("MOVE", "move")
        ];
    }
}
