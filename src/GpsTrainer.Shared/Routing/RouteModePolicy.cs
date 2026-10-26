namespace GpsTrainer;

/// <summary>
/// Same activity rule as the 2013 editor's <c>UserInterface.UsingRouteMode</c>:
/// trail, mountain, and cross-country courses start in freehand. Everything else follows roads.
/// </summary>
public static class RouteModePolicy
{
    public static bool FollowsRoads(string? activityName)
    {
        if (string.IsNullOrWhiteSpace(activityName))
            return true;
        if (activityName.Contains("Trail", StringComparison.OrdinalIgnoreCase))
            return false;
        if (activityName.Contains("Mountain", StringComparison.OrdinalIgnoreCase))
            return false;
        if (activityName.Contains("XCountry", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }
}
