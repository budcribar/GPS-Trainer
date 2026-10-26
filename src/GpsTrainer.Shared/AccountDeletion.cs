namespace GpsTrainer;

/// <summary>
/// Shared rules for DELETE /api/users/{login}. The server accepts the exact word DELETE
/// or the account password. The phone removes its copy only after that call succeeds.
/// </summary>
public static class AccountDeletion
{
    public static bool ShouldCallServer(string? confirmation) =>
        !string.IsNullOrWhiteSpace(confirmation);

    public enum Result
    {
        Deleted,
        Rejected,
        Unauthorized,
        Failed
    }

    public static Result Interpret(int statusCode) => statusCode switch
    {
        204 => Result.Deleted,
        400 => Result.Rejected,
        401 or 403 => Result.Unauthorized,
        _ => Result.Failed
    };

    public static bool ClearsLocalData(Result result) => result == Result.Deleted;

    public static string Message(Result result) => result switch
    {
        Result.Deleted => "The account was deleted.",
        Result.Rejected => "Type your password or DELETE.",
        Result.Unauthorized => "The account was not deleted. Sign in again and try again.",
        _ => "The account was not deleted. Check the connection and try again."
    };

    /// <summary>
    /// Preferences that identify the signed-in rider. Sign-out removes every preference, including these.
    /// </summary>
    public static IReadOnlyList<string> PreferenceKeys()
    {
        var keys = new List<string>
        {
            "SignIn",
            "Password",
            "SavePassword",
            "ApiBaseUrl",
            "BackgroundLocationChoice",
            "Debug",
            "Simulate",
            "Mute",
            "FcmSenderId",
            "KeepAudioHeadphonesAlive"
        };
        foreach (var sport in PreferenceCatalog.Sports)
            keys.Add(sport + "Units");
        foreach (var pref in PreferenceCatalog.Core())
            keys.Add(pref.Key);
        foreach (var pref in PreferenceCatalog.AudioPrompts())
            keys.Add(pref.Key);
        foreach (var pref in PreferenceCatalog.WaypointPrompts())
            keys.Add(pref.Key);
        return keys.Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>Local rows that belong to the signed-in rider. Lookup tables stay.</summary>
    public static readonly string[] CachedTables =
    [
        "Waypoint", "Position", "DefaultRace", "SyncAction", "Course", "Race", "Users"
    ];
}
