using GpsTrainer;

namespace GpsTrainer.Shared.Tests;

public class AudioPromptTests
{
    [Fact]
    public void Cycling_defaults_speak_pace_by_distance_and_keep_distance_off()
    {
        var plans = AudioPrompts.ForSport("Cycling", _ => null);
        Assert.Equal("Dist", plans.Single(plan => plan.Kind == "Pace").Mode);
        Assert.InRange(plans.Single(plan => plan.Kind == "Pace").DistanceKm, 1.6, 1.62);
        Assert.Equal("Off", plans.Single(plan => plan.Kind == "Distance").Mode);
        Assert.Equal("Time", plans.Single(plan => plan.Kind == "CheckPoint").Mode);
        Assert.Equal(TimeSpan.FromSeconds(30), plans.Single(plan => plan.Kind == "CheckPoint").Period);
    }

    [Fact]
    public void Running_defaults_speak_distance_every_mile()
    {
        var plans = AudioPrompts.ForSport("Running", _ => "");
        var distance = plans.Single(plan => plan.Kind == "Distance");
        Assert.Equal("Dist", distance.Mode);
        Assert.InRange(distance.DistanceKm, 1.6, 1.62);
        Assert.Equal(TimeSpan.FromMinutes(10), plans.Single(plan => plan.Kind == "Time").Period);
    }

    [Fact]
    public void A_stored_mode_overrides_the_catalog()
    {
        var plans = AudioPrompts.ForSport("Cycling", key => key == "CyclingPaceMode" ? "Off" : null);
        Assert.Equal("Off", plans.Single(plan => plan.Kind == "Pace").Mode);
    }

    [Fact]
    public void Time_mode_waits_for_the_period()
    {
        var plan = new AudioPrompts.Plan("Time", "Time", 1, TimeSpan.FromSeconds(10));
        var first = AudioPrompts.Advance(plan, TimeSpan.Zero, 0, default);
        Assert.False(first.Speak);
        var early = AudioPrompts.Advance(plan, TimeSpan.FromSeconds(9.4), 0, first.Clock);
        Assert.False(early.Speak);
        var due = AudioPrompts.Advance(plan, TimeSpan.FromSeconds(9.6), 0, first.Clock);
        Assert.True(due.Speak);
    }

    [Fact]
    public void Distance_mode_waits_for_the_interval()
    {
        var mile = EditorText.MilesToKilos(1);
        var plan = new AudioPrompts.Plan("Distance", "Dist", mile, TimeSpan.FromMinutes(10));
        var armed = AudioPrompts.Advance(plan, TimeSpan.Zero, 0, default);
        Assert.False(armed.Speak);
        var shortOf = AudioPrompts.Advance(plan, TimeSpan.FromMinutes(5), mile - 0.01, armed.Clock);
        Assert.False(shortOf.Speak);
        var due = AudioPrompts.Advance(plan, TimeSpan.FromMinutes(6), mile, armed.Clock);
        Assert.True(due.Speak);
    }

    [Fact]
    public void A_turn_is_spoken_every_step_inside_the_for_window()
    {
        var prompt = AudioPrompts.ForWaypoint("TurnLeft", _ => null);
        Assert.Equal("Every", prompt.Mode);
        Assert.False(AudioPrompts.ApproachDue(prompt, false, 0.5, null));
        Assert.True(AudioPrompts.ApproachDue(prompt, false, 0.19, null));
        Assert.False(AudioPrompts.ApproachDue(prompt, false, 0.15, 0.19));
        Assert.True(AudioPrompts.ApproachDue(prompt, false, 0.09, 0.19));
    }

    [Fact]
    public void Off_skips_a_turn_and_still_speaks_the_finish()
    {
        var off = new AudioPrompts.Approach("Off", 0.1, 0.2);
        Assert.False(AudioPrompts.ApproachDue(off, false, 0.1, null));
        Assert.True(AudioPrompts.ApproachDue(off, true, 0.1, null));
    }

    [Fact]
    public void Staging_arms_then_speaks_as_the_start_gets_closer()
    {
        var prompt = AudioPrompts.ForWaypoint("Start", _ => null);
        Assert.False(AudioPrompts.StagingDue(prompt, 1.0, null, out var armed));
        Assert.Equal(1.0, armed);
        Assert.True(AudioPrompts.StagingDue(prompt, 0.8, armed, out var spoken));
        Assert.Equal(0.8, spoken);
        Assert.False(AudioPrompts.StagingDue(prompt, 0.04, spoken, out _));
    }

    [Fact]
    public void Distance_to_start_waits_for_a_fix()
    {
        Assert.Equal("Waiting for GPS", AudioPrompts.DistanceToStart(null, null, 40, -105));
        var text = AudioPrompts.DistanceToStart(40.001, -105, 40, -105);
        Assert.NotEqual("Waiting for GPS", text);
        Assert.Contains("0", text);
    }
}
