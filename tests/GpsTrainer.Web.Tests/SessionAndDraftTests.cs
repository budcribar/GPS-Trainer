using GpsTrainer.Web.Services;

namespace GpsTrainer.Web.Tests;

public class SessionAndDraftTests
{
    [Fact]
    public void A_session_round_trips_without_a_password_and_a_blank_copy_is_ignored()
    {
        var session = new Session();
        session.Set(new LoginResponse
        {
            Token = "signed-token",
            User = new UserProfileDto { Login = "bud", FirstName = "Bud" }
        });

        var json = SessionSnapshot.Serialize(session.Capture());
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);

        var restored = new Session();
        restored.Apply(SessionSnapshot.Parse(json)!);
        Assert.Equal("bud", restored.Login);
        Assert.Equal("signed-token", restored.Token);
        Assert.Equal("Bud", restored.User!.FirstName);

        restored.Clear();
        Assert.Null(restored.Token);
        Assert.Null(restored.Login);
        Assert.Null(SessionSnapshot.Parse(null));
        Assert.Null(SessionSnapshot.Parse("{"));

        var blank = new Session();
        blank.Apply(new SessionSnapshot());
        Assert.Null(blank.Login);
    }

    [Fact]
    public void A_course_drawn_before_sign_in_is_restored_onto_an_empty_editor()
    {
        var course = new Course { Name = "Before sign-in", Comments = "ridge", ActivityCode = 3, Difficulty = 2, Publish = false };
        var waypoints = new List<Waypoint>
        {
            new() { Latitude = 40.5, Longitude = -105.1, WaypointType = WaypointType.Start, TimestampTicks = 1 },
            new() { Latitude = 40.52, Longitude = -105.08, WaypointType = WaypointType.Continue, TimestampTicks = 2 }
        };

        Assert.True(EditorDraft.TryParse(EditorDraft.Serialize(course, waypoints, "01:10:00"), out var draft));

        var restored = new Course();
        var sketch = new CourseSketch();
        var goal = "";
        draft.ApplyTo(restored, sketch, value => goal = value);

        Assert.Equal("Before sign-in", restored.Name);
        Assert.Equal("ridge", restored.Comments);
        Assert.Equal(3, restored.ActivityCode);
        Assert.Equal(2, restored.Difficulty);
        Assert.False(restored.Publish);
        Assert.Equal("01:10:00", goal);
        Assert.Equal(2, sketch.Waypoints.Count);
        Assert.Equal(WaypointType.Start, sketch.Waypoints[0].WaypointType);
        Assert.False(EditorDraft.TryParse("{}", out _));
        Assert.False(EditorDraft.TryParse(null, out _));
    }
}
