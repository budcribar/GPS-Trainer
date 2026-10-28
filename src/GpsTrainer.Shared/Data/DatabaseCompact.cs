namespace GpsTrainer;

/// <summary>When Compact may run, and when creating a course has to wait.</summary>
public static class DatabaseCompact
{
    public const string PleaseWait = "Compacting database, please wait";

    public static bool ShouldStart(bool recording, bool alreadyCompacting) =>
        !recording && !alreadyCompacting;

    public static bool BlocksNewCourse(bool compacting) => compacting;
}

/// <summary>One compact at a time, visible to the course list.</summary>
public sealed class DatabaseMaintenance
{
    private int _compacting;

    public bool IsCompacting => Volatile.Read(ref _compacting) != 0;

    public bool TryBegin() => Interlocked.CompareExchange(ref _compacting, 1, 0) == 0;

    public void End() => Interlocked.Exchange(ref _compacting, 0);
}
