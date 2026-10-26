namespace GpsTrainer.Server.Routing;

public static class OrsProfile
{
    public const string FootWalking = "foot-walking";
    public const string FootHiking = "foot-hiking";
    public const string CyclingRoad = "cycling-road";
    public const string CyclingMountain = "cycling-mountain";

    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "driving-car", "cycling-regular", CyclingRoad, CyclingMountain, "cycling-electric",
        FootWalking, FootHiking, "wheelchair"
    };

    public static string ForActivity(string? activity)
    {
        if (string.IsNullOrWhiteSpace(activity))
            return FootWalking;
        if (activity.Contains("Mountain", StringComparison.OrdinalIgnoreCase)
            && activity.Contains("Bik", StringComparison.OrdinalIgnoreCase))
            return CyclingMountain;
        if (activity.Contains("Cycling", StringComparison.OrdinalIgnoreCase)
            || activity.Contains("Bik", StringComparison.OrdinalIgnoreCase))
            return CyclingRoad;
        if (activity.Contains("Trail", StringComparison.OrdinalIgnoreCase))
            return FootHiking;
        return FootWalking;
    }

    public static bool IsAllowed(string profile) => Allowed.Contains(profile);
}
