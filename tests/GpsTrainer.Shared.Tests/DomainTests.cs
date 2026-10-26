using GpsTrainer;

namespace GpsTrainer.Shared.Tests;

public class DistanceFormatTests
{
    [Fact]
    public void General_format_uses_group_separator_and_unit()
    {
        var distance = Distance.FromKilometers(1234.5);
        Assert.Equal("1,234.50 km", distance.Format(null));
        Assert.Equal("1,234.50 km", distance.Format(""));
        Assert.Equal("1,234.50 km", distance.Format("g"));
    }

    [Fact]
    public void Explicit_pattern_and_unit_tokens()
    {
        Assert.Equal("1.00 meter", Distance.FromMeters(1).Format("0.00 UUU"));
        Assert.Equal("2.00 meters", Distance.FromMeters(2).Format("0.00 UUU"));
        Assert.Equal("1.00 '", Distance.FromFeet(1).Format("0.00 U"));
        Assert.Equal("1.50 ft", Distance.FromFeet(1.5).Format("0.00 UU"));
        Assert.Equal("1.50 UU", Distance.FromNauticalMiles(1.5).Format("0.00 UU"));
    }

    [Fact]
    public void V_placeholder_becomes_zero_before_formatting()
    {
        Assert.Equal("1.50 km", Distance.FromKilometers(1.5).Format("V.VV UU"));
    }

    [Fact]
    public void Invalid_pattern_returns_the_exception_message()
    {
        Assert.Equal("Invalid distance format", Distance.FromMeters(1).Format("0.0.0"));
    }

    [Fact]
    public void Conversions_use_the_original_meter_factors()
    {
        Assert.Equal(1609.344, Distance.FromStatuteMiles(1).ToMeters(), 6);
        Assert.Equal(1000, Distance.FromKilometers(1).ToMeters(), 6);
        Assert.Equal(0.3048, Distance.FromFeet(1).ToMeters(), 6);
        Assert.Equal(1852, Distance.FromNauticalMiles(1).ToMeters(), 6);
        Assert.Equal(1, Distance.FromMeters(1609.344).ToStatuteMiles().Value, 6);
    }

    [Fact]
    public void Empty_is_zero_centimeters_after_imperial_fallback()
    {
        Assert.Equal(DistanceUnit.Centimeters, Distance.Empty.Unit);
        Assert.Equal(0, Distance.Empty.Value);
        Assert.True(Distance.Empty.IsEmpty);
    }
}

public class SpeedAndPaceTests
{
    [Fact]
    public void Meters_per_second_factors_match_the_decompile()
    {
        Assert.Equal(0.447, Speed.FromStatuteMilesPerHour(1).ToMetersPerSecond(), 6);
        Assert.Equal(3.6, Speed.FromMetersPerSecond(1).ToKilometersPerHour().Value, 6);
        Assert.Equal(1.852, new Speed(1, SpeedUnit.Knots).ToKilometersPerHour().Value, 6);
    }

    [Fact]
    public void At_rest_equality_is_the_singleton()
    {
        Assert.True(Speed.AtRest.IsAtRestSingleton);
        Assert.False(new Speed(0, SpeedUnit.MetersPerSecond).IsAtRestSingleton);
        Assert.True(WaypointApproach.ShouldIgnoreBecauseAtRest(Speed.AtRest));
        Assert.False(WaypointApproach.ShouldIgnoreBecauseAtRest(new Speed(0, SpeedUnit.MetersPerSecond)));
    }

    [Fact]
    public void Pace_labels()
    {
        Assert.Equal("06:00mpm", new Pace(Speed.FromStatuteMilesPerHour(10)).ToString());
        Assert.Equal("05:00mpk", new Pace(Speed.FromKilometersPerHour(12)).ToString());
        Assert.Equal("59:59mpk", new Pace(Speed.AtRest).ToString());
    }
}

public class WaypointCountdownTests
{
    [Fact]
    public void Timings_match_the_smali_initializers()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(100), WaypointCountdown.PollingInterval);
        Assert.Equal(TimeSpan.FromSeconds(300), WaypointCountdown.PollingWindow);
        Assert.Equal(TimeSpan.FromSeconds(3), WaypointCountdown.InitialCountDown);
    }

    [Fact]
    public void Moving_loop_subtracts_100ms_and_fires_approach_while_inside_the_countdown()
    {
        var state = new WaypointCountdownState
        {
            TimeToArrival = TimeSpan.FromSeconds(3),
            CountDown = TimeSpan.FromSeconds(3),
            SpeedIsAtRestSingleton = false
        };

        Assert.True(WaypointCountdown.TryPoll(state));
        Assert.Equal(TimeSpan.FromMilliseconds(2900), state.TimeToArrival);
        Assert.Equal(TimeSpan.FromSeconds(2), state.CountDown);
        Assert.Equal(1, state.ApproachedCount);

        var polls = 1;
        while (WaypointCountdown.TryPoll(state))
            polls++;

        Assert.True(state.TimeToArrival <= TimeSpan.Zero);
        Assert.True(state.ApproachedCount >= 3);
        Assert.Equal(29, polls);
    }

    [Fact]
    public void At_rest_singleton_does_not_reduce_eta_but_still_speaks_the_countdown()
    {
        var state = new WaypointCountdownState
        {
            TimeToArrival = TimeSpan.FromSeconds(3),
            CountDown = TimeSpan.FromSeconds(3),
            SpeedIsAtRestSingleton = true
        };

        Assert.True(WaypointCountdown.TryPoll(state));
        Assert.Equal(TimeSpan.FromSeconds(3), state.TimeToArrival);
        Assert.Equal(1, state.ApproachedCount);
        Assert.Equal(TimeSpan.FromSeconds(2), state.CountDown);
    }

    [Fact]
    public void Shutdown_sets_negative_detour_when_closing_on_the_target()
    {
        var state = new WaypointCountdownState
        {
            TimeToArrival = TimeSpan.Zero,
            CurToTargetKilometers = 0.2,
            PrevToTargetKilometers = 0.5
        };
        WaypointCountdown.Shutdown(state);
        Assert.True(state.ResyncedElapsedDistance);
        Assert.True(state.ArrivalFired);
        Assert.NotNull(state.Detour);
        Assert.True(state.Detour!.ToMeters() < 0);
    }

    [Fact]
    public void Shutdown_keeps_current_gap_when_the_target_is_getting_farther()
    {
        var state = new WaypointCountdownState
        {
            CurToTargetKilometers = 0.4,
            PrevToTargetKilometers = 0.1,
            Triggered = true
        };
        WaypointCountdown.Shutdown(state);
        Assert.False(state.ArrivalFired);
        Assert.Equal(0.4, state.Detour!.ToKilometers().Value, 6);
    }

    [Fact]
    public void Eta_refresh_rules()
    {
        var eta = WaypointApproach.UpdatedEta(1.0, 0.5, speedKilometersPerSecond: 0.01, TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.FromSeconds(98), eta);

        Assert.True(WaypointApproach.ShouldRefreshEta(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20)));
        Assert.False(WaypointApproach.ShouldRefreshEta(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(3)));
        Assert.True(WaypointApproach.ShouldRefreshEta(TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(4)));
        Assert.True(WaypointApproach.ShouldStartCountdown(TimeSpan.FromSeconds(300), countdownRunning: false));
        Assert.False(WaypointApproach.ShouldStartCountdown(TimeSpan.FromSeconds(301), countdownRunning: false));
        Assert.False(WaypointApproach.ShouldStartCountdown(TimeSpan.FromSeconds(10), countdownRunning: true));
    }
}

public class SpeechAndRestTests
{
    [Fact]
    public void Turn_speech_matches_TextToSpeech_Say()
    {
        var ahead = WaypointSpeech.Say(WaypointType.TurnRight, Distance.FromMeters(50), null, debug: false);
        Assert.Equal(["In", "50.00 m", "Turn Right"], ahead.Select(a => a.Value).ToArray());

        var beep = WaypointSpeech.Say(WaypointType.TurnLeft, Distance.Empty, "onto Main", debug: false);
        Assert.Equal(SpeechAtomKind.Audio, beep[0].Kind);
        Assert.Equal(nameof(AudioCue.Beep), beep[0].Value);

        var interval = WaypointSpeech.Say(WaypointType.StartInterval, Distance.Empty, null, debug: false);
        Assert.Equal(nameof(AudioCue.StartBeep), interval[0].Value);

        Assert.True(WaypointSpeech.PlayApproachCountdown(WaypointType.FinishInterval, Distance.Empty));
        Assert.False(WaypointSpeech.PlayApproachCountdown(WaypointType.TurnLeft, Distance.Empty));
    }

    [Fact]
    public void Rest_stops_are_excluded_and_shift_the_clock()
    {
        var tracker = new RestStopTracker { IgnoreRestStops = true };
        var t0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.False(tracker.Accept(true, atRest: true, accuracyMeters: 5, t0, out _));
        Assert.True(tracker.IsResting);
        Assert.True(tracker.Accept(true, atRest: false, accuracyMeters: 5, t0.AddSeconds(10), out var effective));
        Assert.Equal(TimeSpan.FromSeconds(10), tracker.TotalRest);
        Assert.Equal(t0, effective);
        Assert.False(tracker.Accept(true, atRest: false, accuracyMeters: 100, t0.AddSeconds(12), out _));
    }
}

public class ContractTests
{
    [Theory]
    [InlineData(ActionType.DeleteCourse, "DeleteCourse")]
    [InlineData(ActionType.InsertCourse, "InsertCourse")]
    [InlineData(ActionType.ModifyCourse, "ClientUpdateCourse")]
    [InlineData(ActionType.DeleteDownloadRace, "InhibitRaceDownload")]
    public void Sync_plan_names_the_soap_call(ActionType action, string expected)
    {
        Assert.Contains(expected, SyncPlan.CallsFor(action));
    }

    [Fact]
    public void Download_insert_actions_do_nothing()
    {
        Assert.Empty(SyncPlan.CallsFor(ActionType.InsertDownloadCourse));
        Assert.Empty(SyncPlan.CallsFor(ActionType.InsertDownloadRace));
        Assert.Equal(6, (int)ActionType.InsertCourse);
        Assert.Equal(11, (int)WaypointType.Start);
    }

    [Fact]
    public void Validators_use_the_original_patterns()
    {
        Assert.True(EmailValidator.Validate("bud@example.com"));
        Assert.False(EmailValidator.Validate("not-an-email"));
        Assert.True(PhoneNumberValidator.Validate("303-555-1212"));
        Assert.True(PhoneNumberValidator.Validate("(303) 555-1212"));
        Assert.False(PhoneNumberValidator.Validate("3035551212"));
    }

    [Fact]
    public void Soap_datetime_round_trips_and_accepts_the_java_offset_form()
    {
        var utc = new DateTime(2013, 10, 13, 15, 4, 5, 123, DateTimeKind.Utc);
        var text = SoapDateTime.ToSoapString(utc);
        Assert.Equal(utc, SoapDateTime.Parse(text));
        Assert.Equal(utc, SoapDateTime.Parse("2013-10-13T15:04:05.123+0000"));
        Assert.Equal(utc.AddMilliseconds(-123), SoapDateTime.Parse("2013-10-13T15:04:05Z"));
    }

    [Fact]
    public void Unix_ticks_are_epoch_milliseconds_times_10000()
    {
        var utc = DateTime.UnixEpoch.AddMilliseconds(1500);
        Assert.Equal(15_000_000, SoapDateTime.ToUnixTicks(utc));
        Assert.Equal(utc, SoapDateTime.FromUnixTicks(15_000_000));
    }

    [Fact]
    public void Directions_xml_parser_keeps_maneuver_steps()
    {
        const string xml = """
            <DirectionsResponse>
              <status>OK</status>
              <route><leg><step>
                <maneuver>turn-left</maneuver>
                <html_instructions>Turn &lt;b&gt;left&lt;/b&gt; onto Main</html_instructions>
                <start_location><lat>40.0</lat><lng>-105.0</lng></start_location>
              </step></leg></route>
            </DirectionsResponse>
            """;
        var steps = GoogleXmlDirections.Parse(xml);
        Assert.Single(steps);
        Assert.Equal("turn-left", steps[0].Maneuver);
        Assert.Equal("Turn left onto Main", steps[0].Instructions);
        Assert.Contains("sensor=true", GoogleXmlDirections.BuildUrl(1, 2, 3, 4, walking: true, apiKey: null));
        Assert.Contains("mode=walking", GoogleXmlDirections.BuildUrl(1, 2, 3, 4, walking: true, apiKey: null));
        Assert.DoesNotContain("key=", GoogleXmlDirections.BuildUrl(1, 2, 3, 4, false, null));
    }

    [Fact]
    public async Task Local_schema_creates_the_ten_tables_and_round_trips_a_course()
    {
        var path = Path.Combine(Path.GetTempPath(), "gpstrainer-tests-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using (var db = new LocalDatabase(path))
            {
                Assert.StartsWith("CREATE TABLE ActivityCodes", SchemaSql.ActivityCodes);
                Assert.DoesNotContain("IF NOT EXISTS", SchemaSql.ActivityCodes, StringComparison.OrdinalIgnoreCase);
                db.EnsureCreated();
                db.EnsureCreated();
                Assert.Equal(SchemaSql.TableNames.OrderBy(n => n), db.TableNames());

                var course = new Course
                {
                    Name = "Boulder Creek",
                    Login = "bud",
                    ActivityCode = 1,
                    ElapsedDistanceKm = 12.5,
                    Timestamp = new DateTime(2013, 10, 13, 0, 0, 0, DateTimeKind.Utc)
                };
                db.UpsertCourse(course);
                db.InsertWaypoint(new Waypoint
                {
                    CourseId = course.CourseId,
                    Latitude = 40.01,
                    Longitude = -105.27,
                    WaypointType = WaypointType.Start,
                    TimestampTicks = 1
                });
                db.RecordAction(ActionType.InsertCourse, course.CourseId);
                var loaded = Assert.Single(db.ListCourses("bud"));
                Assert.Equal("Boulder Creek", loaded.Name);
                Assert.Equal(WaypointType.Start, Assert.Single(db.ListWaypoints(course.CourseId)).WaypointType);
                Assert.Equal(ActionType.InsertCourse, Assert.Single(db.ListActions()).Action);
                db.EnsureCreated();
                Assert.Equal("Boulder Creek", Assert.Single(db.ListCourses("bud")).Name);
                Assert.Equal(SchemaSql.TableNames.OrderBy(n => n), db.TableNames());
            }
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        await Task.CompletedTask;
    }

    [Fact]
    public void Preference_defaults_keep_ignore_rest_stops_and_the_cycling_pace_prompt()
    {
        Assert.Equal("true", PreferenceCatalog.Core().Single(p => p.Key == "IgnoreRestStops").DefaultValue);
        Assert.Contains(PreferenceCatalog.AudioPrompts(), p => p.Key == "CyclingPaceMode" && p.DefaultValue == "Dist");
        Assert.Contains(PreferenceCatalog.WaypointPrompts(), p => p.Key == "TurnLeftMode" && p.DefaultValue == "Every");
    }

    [Fact]
    public void Interval_clock_matches_the_website_time_string()
    {
        Assert.Equal("00:01:05", IntervalClock.Format(TimeSpan.FromSeconds(65)));
        Assert.Equal("01:01:01", IntervalClock.Format(TimeSpan.FromSeconds(3661)));
        Assert.Equal(SiteIdentity.Domain, "gps-trainer.com");
    }

    [Fact]
    public void Course_rules_reject_an_unmatched_interval_and_keep_essential_continues()
    {
        var shortCourse = new List<Waypoint>
        {
            new() { WaypointType = WaypointType.Start, TimestampTicks = 1, ElapsedDistanceKm = 0, Latitude = 40, Longitude = -105 },
            new() { WaypointType = WaypointType.Finish, TimestampTicks = 2, ElapsedDistanceKm = 0.01, Latitude = 40.0001, Longitude = -105 }
        };
        Assert.Equal("Course is too short", CourseRules.Validate(shortCourse));

        var open = new List<Waypoint>
        {
            new() { WaypointType = WaypointType.Start, TimestampTicks = 1, ElapsedDistanceKm = 0, Latitude = 40, Longitude = -105 },
            new() { WaypointType = WaypointType.StartInterval, TimestampTicks = 2, ElapsedDistanceKm = 1, Latitude = 40.01, Longitude = -105 },
            new() { WaypointType = WaypointType.Finish, TimestampTicks = 3, ElapsedDistanceKm = 2, Latitude = 40.02, Longitude = -105 }
        };
        Assert.Equal("Must have a matching Finish Interval for each Start Interval", CourseRules.Validate(open));

        var continues = new List<Waypoint>
        {
            new() { WaypointType = WaypointType.Start, HeadingDegrees = 0, ElapsedDistanceKm = 0 },
            new() { WaypointType = WaypointType.Continue, HeadingDegrees = 1, ElapsedDistanceKm = 0.1 },
            new() { WaypointType = WaypointType.Continue, HeadingDegrees = 1, ElapsedDistanceKm = 0.4 },
            new() { WaypointType = WaypointType.TurnLeft, HeadingDegrees = 90, ElapsedDistanceKm = 0.5 }
        };
        var essential = CourseRules.Essential(continues);
        Assert.Equal(3, essential.Count);
        Assert.Equal(WaypointType.TurnLeft, essential[^1].WaypointType);
    }

    [Fact]
    public void Interval_timing_walks_the_track_between_the_markers()
    {
        var start = new Waypoint { Latitude = 40.01, Longitude = -105, WaypointType = WaypointType.StartInterval, ElapsedDistanceKm = 1.1 };
        var finish = new Waypoint { Latitude = 40.02, Longitude = -105, WaypointType = WaypointType.FinishInterval, ElapsedDistanceKm = 2.2 };
        var t0 = new DateTime(2013, 10, 13, 12, 0, 0, DateTimeKind.Utc).Ticks;
        var track = new List<TrackPoint>();
        for (var i = 0; i <= 6; i++)
        {
            track.Add(new TrackPoint
            {
                Latitude = 40.0 + i * 0.005,
                Longitude = -105,
                TimestampTicks = t0 + TimeSpan.FromSeconds(i * 10).Ticks
            });
        }

        var split = CourseIntervals.Measure(start, finish, track);
        Assert.NotNull(split);
        Assert.True(split!.Time > TimeSpan.Zero);
        Assert.StartsWith("00:", split.TimeString);
        Assert.Single(CourseIntervals.Pair([start, finish]));
    }
}
