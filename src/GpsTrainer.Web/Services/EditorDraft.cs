using System.Text.Json;
using GpsTrainer;

namespace GpsTrainer.Web.Services;

/// <summary>
/// A course drawn before sign-in. The editor page is discarded on the way to the
/// login form, so the draft is stored and put back when the editor opens again.
/// </summary>
public sealed class EditorDraft
{
    public const string StorageKey = "gpstrainer.draftCourse";

    public string Name { get; set; } = "";
    public string Comments { get; set; } = "";
    public int ActivityCode { get; set; }
    public int Difficulty { get; set; }
    public bool Publish { get; set; }
    public string GoalTime { get; set; } = "00:00:35";
    public List<Waypoint> Waypoints { get; set; } = [];

    public static EditorDraft Capture(Course course, IReadOnlyList<Waypoint> waypoints, string goalTime) => new()
    {
        Name = course.Name,
        Comments = course.Comments,
        ActivityCode = course.ActivityCode,
        Difficulty = course.Difficulty,
        Publish = course.Publish,
        GoalTime = string.IsNullOrWhiteSpace(goalTime) ? "00:00:35" : goalTime,
        Waypoints = waypoints.Select(Clone).ToList()
    };

    public void ApplyTo(Course course, CourseSketch sketch, Action<string> setGoalTime)
    {
        course.Name = Name ?? "";
        course.Comments = Comments ?? "";
        course.ActivityCode = ActivityCode;
        course.Difficulty = Difficulty is < 0 or > 3 ? 0 : Difficulty;
        course.Publish = Publish;
        setGoalTime(string.IsNullOrWhiteSpace(GoalTime) ? "00:00:35" : GoalTime);
        sketch.Replace(Waypoints.Select(Clone).ToList());
    }

    public static string Serialize(Course course, IReadOnlyList<Waypoint> waypoints, string goalTime) =>
        JsonSerializer.Serialize(Capture(course, waypoints, goalTime), SessionSnapshot.Json);

    public static bool TryParse(string? json, out EditorDraft draft)
    {
        draft = new EditorDraft();
        if (string.IsNullOrWhiteSpace(json))
            return false;
        try
        {
            var parsed = JsonSerializer.Deserialize<EditorDraft>(json, SessionSnapshot.Json);
            if (parsed is null || parsed.Waypoints.Count == 0)
                return false;
            draft = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static Waypoint Clone(Waypoint waypoint) => new()
    {
        CourseId = waypoint.CourseId,
        TimestampTicks = waypoint.TimestampTicks,
        Latitude = waypoint.Latitude,
        Longitude = waypoint.Longitude,
        HeadingDegrees = waypoint.HeadingDegrees,
        AltitudeKm = waypoint.AltitudeKm,
        ElapsedDistanceKm = waypoint.ElapsedDistanceKm,
        WaypointType = waypoint.WaypointType,
        TextDescription = waypoint.TextDescription
    };
}
