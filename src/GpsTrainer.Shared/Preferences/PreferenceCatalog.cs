namespace GpsTrainer;

public enum PromptMode
{
    Off,
    Time,
    Dist,
    Every,
    For
}

public sealed record PreferenceDefault(string Key, string DefaultValue, string Summary);

/// <summary>
/// Preference keys recovered from <c>res/xml/preferences.xml</c> and <c>Settings.initialize</c>.
/// Distance preferences are stored as <c>{key}.Value</c> plus <c>{key}.Units</c>, matching the app.
/// </summary>
public static class PreferenceCatalog
{
    public static readonly string[] Sports =
    [
        "Other",
        "Running",
        "Trail Running",
        "Cycling",
        "Mountain Biking",
        "XCountry Skate",
        "Race Walking",
        "Rowing",
        "XCountry Classic"
    ];

    public static readonly string[] WaypointPromptKeys =
    [
        "Finish", "StartInterval", "Summit", "TurnAround", "TurnLeft", "TurnRight", "Start", "FeedZone"
    ];

    public static IEnumerable<PreferenceDefault> Core()
    {
        yield return new("Mute", "false", "Mute audio prompts");
        yield return new("Simulate", "false", "Developer Mode to simulate GPS");
        yield return new("Logging", "false", "Turn on Logging for GPS Locations");
        yield return new("Debug", "false", "Set Debug Mode");
        yield return new("Tuning", "false", "Set Performance Tuning Mode");
        yield return new("DevelopmentMode", "false", "Use the development web site and database");
        yield return new("MuteWithinFinishDistance", "50m", "Mute Within Finish Distance");
        yield return new("KeepAudioHeadphonesAlive", "false", "Play silence to keep the BT Audio Headphones alive");
        yield return new("IgnoreRestStops", "true", "Do not include rest stops in the results");
        yield return new("DefaultSport", "Cycling", "Choose default sport for new courses");
        yield return new("Verbose", "true", "Long form of the audio prompts");
        yield return new("HighResolution", "false", "High Resolution audio prompts");
        yield return new("StartDelayTime", "00:00:30", "Start Delay Time");
        yield return new("BackupOnExit", "true", "Backup the gpsTrainer database upon exit");
        yield return new("SavePassword", "true", "Remember password locally");
        yield return new("PositionReceivedTimeout", "20", "Seconds");
        yield return new("EnableTimerResetTimeout", "8", "Seconds");
    }

    public static IEnumerable<PreferenceDefault> AudioPrompts()
    {
        foreach (var sport in Sports)
        {
            foreach (var kind in new[] { "CheckPoint", "AltGain", "Pace", "Time", "Distance" })
            {
                var key = sport + kind;
                var (mode, dist) = DefaultAudio(sport, kind);
                yield return new(key + "Mode", mode, key);
                yield return new(key + "Distance", dist, key);
                yield return new(key + "Period", kind == "CheckPoint" ? "00:00:30" : "00:10:00", key);
            }
        }
    }

    public static IEnumerable<PreferenceDefault> WaypointPrompts()
    {
        foreach (var key in WaypointPromptKeys)
        {
            var (every, forDist) = key switch
            {
                "Start" or "FeedZone" => ("0.1 mi", "1 mi"),
                "TurnAround" or "TurnLeft" or "TurnRight" => ("100 m", "200 m"),
                _ => ("100 m", "500 m")
            };
            yield return new(key + "Mode", "Every", key);
            yield return new(key + "Every", every, key);
            yield return new(key + "For", forDist, key);
        }
    }

    private static (string Mode, string Distance) DefaultAudio(string sport, string kind)
    {
        if (kind == "CheckPoint")
            return ("Time", "1.0 mi");
        if (kind == "Distance" && sport is "Running" or "Trail Running" or "Race Walking")
            return ("Dist", "1.0 mi");
        if (kind == "Distance" && sport is "XCountry Skate" or "XCountry Classic")
            return ("Dist", "1000 m");
        if (kind == "Distance" && sport == "Rowing")
            return ("Dist", "100 m");
        if (kind == "Pace" && sport == "Cycling")
            return ("Dist", "1.0 mi");
        return ("Off", "1.0 mi");
    }
}
