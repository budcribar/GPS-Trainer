namespace GpsTrainer;

/// <summary>
/// Whether a finish-triggered sync may start. The upload itself is the existing
/// synchronize. A false result is a quiet skip.
/// </summary>
public static class BackgroundSync
{
    public static bool IsSignedIn(string? login) => !string.IsNullOrWhiteSpace(login);

    public static bool LicenseAllows(bool isActive, DateTime expirationUtc, DateTime nowUtc) =>
        isActive && expirationUtc >= nowUtc;

    public static bool ShouldRun(bool recording, bool alreadyRunning, bool signedIn, bool online) =>
        !recording && !alreadyRunning && signedIn && online;
}
