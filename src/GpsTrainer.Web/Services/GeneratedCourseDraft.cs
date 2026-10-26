namespace GpsTrainer.Web.Services;

/// <summary>
/// Holds one unsaved generated course until the editor opens it.
/// The editor does not save unless the user chooses Save.
/// </summary>
public static class GeneratedCourseDraft
{
    private static readonly object Gate = new();
    private static Course? _course;
    private static List<Waypoint>? _waypoints;

    public static void Store(Course course, IReadOnlyList<Waypoint> waypoints)
    {
        lock (Gate)
        {
            _course = course;
            _waypoints = waypoints.ToList();
        }
    }

    public static bool TryTake(out Course course, out List<Waypoint> waypoints)
    {
        lock (Gate)
        {
            if (_course is null || _waypoints is null)
            {
                course = new Course();
                waypoints = [];
                return false;
            }

            course = _course;
            waypoints = _waypoints;
            _course = null;
            _waypoints = null;
            return true;
        }
    }
}
