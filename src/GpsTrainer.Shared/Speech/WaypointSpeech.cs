namespace GpsTrainer;

public enum SpeechAtomKind
{
    Words,
    Audio
}

public readonly record struct SpeechAtom(SpeechAtomKind Kind, string Value)
{
    public static SpeechAtom Say(string words) => new(SpeechAtomKind.Words, words);
    public static SpeechAtom Play(AudioCue cue) => new(SpeechAtomKind.Audio, cue.ToString());
}

/// <summary>
/// Port of <c>TextToSpeech.Say(Waypoint)</c> plus the approached-event audio from
/// <c>WaypointTracker</c>. Phrase strings are the English values from <c>strings.xml</c>.
/// </summary>
public static class WaypointSpeech
{
    public const string TurnDetectorFlag = "<TurnDetector/>";

    public static IReadOnlyList<SpeechAtom> Say(WaypointType type, Distance distanceToWaypoint, string? description, bool debug)
    {
        var list = new List<SpeechAtom>();
        var hasDistance = distanceToWaypoint.GreaterThan(Distance.Empty);
        var distanceText = hasDistance ? distanceToWaypoint.Format(null) : "";

        switch (type)
        {
            case WaypointType.Finish:
                if (hasDistance)
                    list.Add(SpeechAtom.Say(distanceText));
                break;
            case WaypointType.TurnRight:
            case WaypointType.TurnLeft:
                if (hasDistance)
                {
                    list.Add(SpeechAtom.Say("In"));
                    list.Add(SpeechAtom.Say(distanceText));
                    list.Add(SpeechAtom.Say(UseDescription(description) ? description! : type == WaypointType.TurnLeft ? "Turn Left" : "Turn Right"));
                }
                else
                {
                    list.Add(SpeechAtom.Play(AudioCue.Beep));
                }
                break;
            case WaypointType.TurnAround:
                if (hasDistance)
                {
                    list.Add(SpeechAtom.Say("Turn Around"));
                    list.Add(SpeechAtom.Say(distanceText));
                }
                else if (debug)
                {
                    list.Add(SpeechAtom.Say("Turn Around"));
                }
                else
                {
                    list.Add(SpeechAtom.Play(AudioCue.Beep));
                }
                break;
            case WaypointType.Summit:
                list.Add(SpeechAtom.Say("Summit"));
                if (hasDistance)
                    list.Add(SpeechAtom.Say(distanceText));
                break;
            case WaypointType.FeedZone:
                list.Add(SpeechAtom.Say("Feed Zone"));
                if (hasDistance)
                    list.Add(SpeechAtom.Say(distanceText));
                break;
            case WaypointType.Announcement:
                if (!string.IsNullOrEmpty(description))
                    list.Add(SpeechAtom.Say(description));
                break;
            case WaypointType.StartInterval:
                if (hasDistance)
                {
                    list.Add(SpeechAtom.Say("Interval"));
                    list.Add(SpeechAtom.Say(distanceText));
                }
                else
                {
                    list.Add(SpeechAtom.Play(AudioCue.StartBeep));
                }
                break;
        }

        return list;
    }

    /// <summary>
    /// <c>WaypointTracker</c> plays <c>start54321</c> on approach only for an interval marker
    /// whose distance-to-waypoint is empty.
    /// </summary>
    public static bool PlayApproachCountdown(WaypointType type, Distance distanceToWaypoint) =>
        (type is WaypointType.StartInterval or WaypointType.FinishInterval)
        && distanceToWaypoint.ApproximatelyEquals(Distance.Empty);

    private static bool UseDescription(string? description) =>
        !string.IsNullOrEmpty(description) && description != TurnDetectorFlag;
}
