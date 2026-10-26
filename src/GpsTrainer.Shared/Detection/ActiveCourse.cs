namespace GpsTrainer;

/// <summary>
/// The one course the running screen is coaching against.
/// Today that is the course the rider opened. Automatic course detection,
/// which is not built yet, implements the same shape and replaces the object
/// once a circle around the rider has eliminated every other candidate.
/// <see cref="WaypointApproach"/> keeps watching a single waypoint list.
/// </summary>
public interface IActiveCourse
{
    Guid? CourseId { get; }

    IReadOnlyList<Waypoint> Waypoints { get; }
}

/// <summary>The course the rider picked before the run started.</summary>
public sealed class ChosenCourse : IActiveCourse
{
    public ChosenCourse(Guid? courseId, IReadOnlyList<Waypoint> waypoints)
    {
        CourseId = courseId;
        Waypoints = waypoints;
    }

    public Guid? CourseId { get; }

    public IReadOnlyList<Waypoint> Waypoints { get; }
}
