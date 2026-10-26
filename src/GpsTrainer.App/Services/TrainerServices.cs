using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace GpsTrainer.App;

public sealed class AppSession
{
    public UserAccount? User { get; set; }
    public Course? Course { get; set; }
    public Race? Race { get; set; }
    public string? Token { get; set; }
}

public sealed class TrainerRepository : IDisposable
{
    private readonly LocalDatabase _database;

    public TrainerRepository()
    {
        var path = Path.Combine(FileSystem.AppDataDirectory, SchemaSql.DatabaseName);
        _database = new LocalDatabase(path);
        _database.EnsureCreated();
        if (_database.ListActivityCodes().Count == 0)
        {
            var key = 0;
            _database.ReplaceActivityCodes(PreferenceCatalog.Sports.Select(name => new ActivityCode { ActKey = key++, ActValue = name }));
        }
    }

    public LocalDatabase Database => _database;

    public void Remember(UserAccount user) => _database.UpsertUser(user);

    public bool OfflineSignIn(string login, string password) => _database.SignIn(login, password);

    public UserAccount? FindUser(string login) => _database.FindUser(login);

    public void Dispose() => _database.Dispose();
}

public static partial class ApiAddress
{
    public const string PreferenceKey = "ApiBaseUrl";

    public static string Current
    {
        get
        {
            var saved = Preferences.Get(PreferenceKey, "");
            if (!string.IsNullOrWhiteSpace(saved))
                return saved;
            if (!string.IsNullOrWhiteSpace(BuiltIn))
                return BuiltIn;
#if ANDROID
            return "https://web-production-7e6fc.up.railway.app";
#else
            return "http://127.0.0.1:5097";
#endif
        }
    }
}

public sealed class TrainerApi
{
    private readonly AppSession _session;

    public TrainerApi(AppSession session) => _session = session;

    public async Task<LoginResponse?> LoginAsync(string login, string password)
    {
        using var http = Client();
        var response = await http.PostAsJsonAsync("api/auth/login", new LoginRequest { Login = login, Password = password });
        if (!response.IsSuccessStatusCode)
            return null;
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        if (body is not null)
            _session.Token = body.Token;
        return body;
    }

    public async Task<string> SignUpAsync(UserWriteDto user)
    {
        using var http = Client();
        var response = await http.PostAsJsonAsync("api/users", user);
        var body = await response.Content.ReadFromJsonAsync<InsertUserResponse>();
        return body?.Result ?? "error";
    }

    public async Task<UserStatus?> StatusAsync(string login)
    {
        using var http = Client();
        var response = await http.GetAsync($"api/users/{Uri.EscapeDataString(login)}/status");
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<UserStatus>();
    }

    public async Task<IReadOnlyList<Course>> DownloadedCoursesAsync(string login)
    {
        using var http = Client();
        Authorize(http);
        return await http.GetFromJsonAsync<List<Course>>($"api/courses?login={Uri.EscapeDataString(login)}") ?? [];
    }

    public async Task<IReadOnlyList<Waypoint>> WaypointsAsync(Guid courseId)
    {
        using var http = Client();
        Authorize(http);
        return await http.GetFromJsonAsync<List<Waypoint>>($"api/courses/{courseId}/waypoints") ?? [];
    }

    public async Task<bool> UploadCourseAsync(Course course, IReadOnlyList<Waypoint> waypoints)
    {
        using var http = Client();
        Authorize(http);
        var response = await http.PostAsJsonAsync("api/courses", course);
        if (!response.IsSuccessStatusCode)
            return false;
        var saved = await response.Content.ReadFromJsonAsync<Course>();
        if (saved is null)
            return false;
        var put = await http.PutAsJsonAsync($"api/courses/{saved.CourseId}/waypoints", waypoints);
        return put.IsSuccessStatusCode;
    }

    public async Task<GpxImportResult?> PreviewGpxAsync(string xml, string? fileName)
    {
        using var http = Client();
        Authorize(http);
        var address = "api/courses/import/gpx?fileName=" + Uri.EscapeDataString(fileName ?? "");
        var response = await http.PostAsync(address, new StringContent(xml, System.Text.Encoding.UTF8, "application/gpx+xml"));
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<GpxImportResult>();
    }

    public async Task<string?> ExportGpxAsync(Guid courseId)
    {
        using var http = Client();
        Authorize(http);
        var response = await http.GetAsync($"api/courses/{courseId}/export/gpx");
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<bool> UpdateCourseAsync(Course course)
    {
        using var http = Client();
        Authorize(http);
        var response = await http.PutAsJsonAsync($"api/courses/{course.CourseId}", course);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> UpdateWaypointsAsync(Guid courseId, IReadOnlyList<Waypoint> waypoints)
    {
        using var http = Client();
        Authorize(http);
        var response = await http.PutAsJsonAsync($"api/courses/{courseId}/waypoints", waypoints);
        return response.IsSuccessStatusCode;
    }

    public async Task<RouteResponseDto> RouteAsync(RouteRequestDto request)
    {
        using var http = Client();
        Authorize(http);
        var response = await http.PostAsJsonAsync("api/routing/directions", request);
        if (!response.IsSuccessStatusCode)
            return RouteResponseDto.Fail("Routing failed.");
        return await response.Content.ReadFromJsonAsync<RouteResponseDto>()
            ?? RouteResponseDto.Fail("Routing failed.");
    }

    public async Task<bool> DeleteCourseAsync(Guid id, string login)
    {
        using var http = Client();
        Authorize(http);
        var response = await http.DeleteAsync($"api/courses/{id}?login={Uri.EscapeDataString(login)}");
        return response.IsSuccessStatusCode;
    }

    public async Task InhibitCourseAsync(Guid id, string login)
    {
        using var http = Client();
        await http.PostAsync($"api/courses/{id}/inhibit-download?login={Uri.EscapeDataString(login)}", null);
    }

    public async Task<bool> UploadRaceAsync(Race race, IReadOnlyList<TrackPoint> points)
    {
        using var http = Client();
        Authorize(http);
        var response = await http.PostAsJsonAsync("api/races", race);
        if (!response.IsSuccessStatusCode)
            return false;
        var saved = await response.Content.ReadFromJsonAsync<Race>();
        if (saved is null)
            return false;
        var put = await http.PostAsJsonAsync($"api/races/{saved.RaceId}/positions", points);
        return put.IsSuccessStatusCode;
    }

    public async Task<bool> UploadPositionsAsync(Guid raceId, IReadOnlyList<TrackPoint> points)
    {
        using var http = Client();
        Authorize(http);
        var response = await http.PostAsJsonAsync($"api/races/{raceId}/positions", points);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> UpdateRaceAsync(Race race)
    {
        using var http = Client();
        Authorize(http);
        var response = await http.PutAsJsonAsync($"api/races/{race.RaceId}", race);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteRaceAsync(Guid id, string login)
    {
        using var http = Client();
        Authorize(http);
        var response = await http.DeleteAsync($"api/races/{id}?login={Uri.EscapeDataString(login)}");
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// DELETE /api/users/{login} with the signed-in token. A blank confirmation is not sent.
    /// Network failures and non-success statuses leave the device copy in place.
    /// </summary>
    public async Task<AccountDeletion.Result> DeleteAccountAsync(string login, string confirmation)
    {
        if (!AccountDeletion.ShouldCallServer(confirmation) || string.IsNullOrWhiteSpace(login))
            return AccountDeletion.Result.Rejected;
        using var http = Client();
        Authorize(http);
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/users/{Uri.EscapeDataString(login)}")
        {
            Content = JsonContent.Create(new AccountDeletionRequest { Confirmation = confirmation.Trim() })
        };
        try
        {
            using var response = await http.SendAsync(request);
            return AccountDeletion.Interpret((int)response.StatusCode);
        }
        catch (HttpRequestException)
        {
            return AccountDeletion.Result.Failed;
        }
        catch (TaskCanceledException)
        {
            return AccountDeletion.Result.Failed;
        }
    }

    public async Task InhibitRaceAsync(Guid id, string login)
    {
        using var http = Client();
        await http.PostAsync($"api/races/{id}/inhibit-download?login={Uri.EscapeDataString(login)}", null);
    }

    public async Task<IReadOnlyList<Race>> DownloadedRacesAsync(string login)
    {
        using var http = Client();
        Authorize(http);
        return await http.GetFromJsonAsync<List<Race>>($"api/races?login={Uri.EscapeDataString(login)}") ?? [];
    }

    public async Task<IReadOnlyList<TrackPoint>> PositionsAsync(Guid raceId)
    {
        using var http = Client();
        Authorize(http);
        return await http.GetFromJsonAsync<List<TrackPoint>>($"api/races/{raceId}/positions") ?? [];
    }

    public async Task RegisterPushAsync(string login, string token)
    {
        using var http = Client();
        Authorize(http);
        await http.PostAsJsonAsync($"api/users/{Uri.EscapeDataString(login)}/push", new PushRegistrationDto { Token = token });
    }

    private HttpClient Client() => new() { BaseAddress = new Uri(ApiAddress.Current.TrimEnd('/') + "/") };

    private void Authorize(HttpClient http)
    {
        if (!string.IsNullOrEmpty(_session.Token))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _session.Token);
    }
}

/// <summary>
/// After the server deletes the account: stop tracking, drop the local database copy,
/// cached files, preferences, and the in-memory session.
/// </summary>
public static class PhoneSignOut
{
    public static void Apply(AppSession session)
    {
        try
        {
            var services = IPlatformApplication.Current?.Services
                ?? Application.Current?.Handler?.MauiContext?.Services;
            if (services is not null)
            {
                try
                {
                    services.GetService<TrackingHost>()?.Stop();
                }
                catch (Exception)
                {
                }
                try
                {
                    services.GetService<RunSession>()?.Forget();
                }
                catch (Exception)
                {
                }
                services.GetService<TrainerRepository>()?.Database.ClearCachedData();
            }
        }
        catch (Exception)
        {
            // The server already removed the account. Preferences and the session still have to go.
        }

        ClearCacheFiles();
        foreach (var key in AccountDeletion.PreferenceKeys())
            Preferences.Remove(key);
        Preferences.Clear();
        session.User = null;
        session.Token = null;
        session.Course = null;
        session.Race = null;
    }

    private static void ClearCacheFiles()
    {
        try
        {
            var dir = FileSystem.CacheDirectory;
            if (!Directory.Exists(dir))
                return;
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (IOException)
        {
        }
    }
}

public sealed class SyncService
{
    private readonly TrainerRepository _store;
    private readonly TrainerApi _api;
    private readonly AppSession _session;

    public SyncService(TrainerRepository store, TrainerApi api, AppSession session)
    {
        _store = store;
        _api = api;
        _session = session;
    }

    public async Task<string> SynchronizeAsync()
    {
        var login = _session.User?.Login;
        if (login is null)
            return AppStrings.please_enter_your_login_name;
        UserStatus? status;
        try
        {
            status = await _api.StatusAsync(login);
        }
        catch (Exception ex)
        {
            return AppStrings.server_unavailable + " " + ex.Message;
        }
        if (status is null)
            return AppStrings.server_unavailable;
        if (!status.IsActive || status.ExpirationDate < DateTime.UtcNow)
            return AppStrings.license_expired;

        var notes = new List<string>();
        foreach (var action in _store.Database.ListActions().ToList())
        {
            var ok = await PlayAsync(action, login);
            notes.Add($"{action.Action}: {(ok ? "ok" : "failed")}");
            if (ok)
                _store.Database.DeleteAction(action.RowId);
        }

        var courses = await _api.DownloadedCoursesAsync(login);
        foreach (var course in courses)
        {
            _store.Database.UpsertCourse(course);
            var waypoints = await _api.WaypointsAsync(course.CourseId);
            _store.Database.ReplaceWaypoints(course.CourseId, waypoints);
        }
        var races = await _api.DownloadedRacesAsync(login);
        foreach (var race in races)
        {
            _store.Database.UpsertRace(race);
            foreach (var point in await _api.PositionsAsync(race.RaceId))
                _store.Database.InsertPosition(point);
        }
        _store.Database.SetUserTimestamp(status.UserTimestamp);
        notes.Add($"{courses.Count} courses, {races.Count} races");
        return string.Join("\n", notes);
    }

    private async Task<bool> PlayAsync(SyncActionRecord action, string login)
    {
        var calls = SyncPlan.CallsFor(action.Action);
        if (calls.Count == 0)
            return true;
        try
        {
            switch (action.Action)
            {
                case ActionType.InsertCourse:
                case ActionType.ModifyCourse:
                    var course = _store.Database.ListCourses().FirstOrDefault(c => c.CourseId == action.Id);
                    if (course is null)
                        return true;
                    var waypoints = _store.Database.ListWaypoints(course.CourseId);
                    if (action.Action == ActionType.InsertCourse)
                        return await _api.UploadCourseAsync(course, waypoints);
                    if (!await _api.UpdateCourseAsync(course))
                        return false;
                    return await _api.UpdateWaypointsAsync(course.CourseId, waypoints);
                case ActionType.DeleteCourse:
                    return await _api.DeleteCourseAsync(action.Id, login);
                case ActionType.DeleteDownloadCourse:
                    await _api.InhibitCourseAsync(action.Id, login);
                    return true;
                case ActionType.InsertRace:
                case ActionType.ModifyRace:
                    var race = _store.Database.ListRaces().FirstOrDefault(r => r.RaceId == action.Id);
                    if (race is null)
                        return true;
                    if (action.Action == ActionType.InsertRace)
                        return await _api.UploadRaceAsync(race, _store.Database.ListPositions(race.RaceId));
                    if (!await _api.UpdateRaceAsync(race))
                        return false;
                    return await _api.UploadPositionsAsync(race.RaceId, _store.Database.ListPositions(race.RaceId));
                case ActionType.DeleteRace:
                    return await _api.DeleteRaceAsync(action.Id, login);
                case ActionType.DeleteDownloadRace:
                    await _api.InhibitRaceAsync(action.Id, login);
                    return true;
                default:
                    return true;
            }
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }
}

/// <summary>
/// Coaching session used by the running screen. Rest exclusion, accuracy gate, and
/// waypoint speech reuse the shared ports of GpsDispatcher, WaypointDetector, and TextToSpeech.
/// </summary>
public sealed class RunSession
{
    private readonly RestStopTracker _rest = new() { IgnoreRestStops = Preferences.Get("IgnoreRestStops", true) };
    private readonly TurnDetector _turns = new();
    private readonly List<SpeechAtom> _speech = [];
    private DateTime? _started;
    private GeoPosition? _previous;
    private string _sport = "Cycling";
    private double _gainKm;
    private double? _lastAltitudeKm;
    private readonly Dictionary<string, AudioPrompts.Clock> _clocks = [];
    private readonly Dictionary<long, double> _waypointSpoken = [];
    private IReadOnlyList<TrackPoint> _ghost = [];
    private TimeSpan _goal = TimeSpan.Zero;
    private TimeSpan _targetElapsed = TimeSpan.Zero;
    private GhostSchedule _checkpoint = new(false, false, TimeSpan.FromSeconds(30), 0);
    private TimeSpan _lastCheckpointElapsed = TimeSpan.Zero;
    private double _lastCheckpointKm;
    private readonly RideRecorder _track = new();
    private GhostSplit? _split;
    private double? _speedMps;

    public RunStateKind State { get; private set; } = RunStateKind.WaitingForGps;
    public bool Learning { get; private set; }
    public double ElapsedKilometers { get; private set; }
    public TimeSpan Elapsed { get; private set; }
    public string PaceText { get; private set; } = "0.0 mph";
    public string SpeedText { get; private set; } = "0.0 mph";
    public string DistanceText { get; private set; } = "0.00 mi";
    public string TimeText { get; private set; } = "00:00:00";
    public string TimeUnit { get; private set; } = "sec";
    public string DistanceValue { get; private set; } = "0.00";
    public string DistanceUnit { get; private set; } = "mi";
    public string PaceValue { get; private set; } = "0.0";
    public string PaceUnit { get; private set; } = "mph";
    public string CheckpointText { get; private set; } = "00:00:00";
    public string CheckpointUnit { get; private set; } = "sec";
    public IReadOnlyList<TrackPoint> RecordedTrack => _track.Points;
    public string Message { get; private set; } = AppStrings.WaitingForGpsData;
    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }
    public double? AccuracyMeters { get; private set; }
    public IActiveCourse ActiveCourse { get; set; } = new ChosenCourse(null, []);

    public IReadOnlyList<Waypoint> Waypoints
    {
        get => ActiveCourse.Waypoints;
        set => ActiveCourse = new ChosenCourse(ActiveCourse.CourseId, value);
    }

    private long _announcedTicks = -1;

    /// <summary>
    /// The rider already chose this course. A later detector replaces <see cref="ActiveCourse"/>
    /// with its own <see cref="IActiveCourse"/> once several candidates have narrowed to one.
    /// </summary>
    public void UseCourse(Guid courseId, IReadOnlyList<Waypoint> waypoints) =>
        ActiveCourse = new ChosenCourse(courseId, waypoints);

    public void UseSport(string? sport) =>
        _sport = string.IsNullOrWhiteSpace(sport) ? "Cycling" : sport.Trim();

    /// <summary>
    /// The stored track of the race being chased. An empty track leaves racing silent.
    /// </summary>
    public void UseGhost(IReadOnlyList<TrackPoint> track, TimeSpan goal, TimeSpan targetElapsed, string? sport)
    {
        _ghost = track;
        _goal = goal;
        _targetElapsed = targetElapsed > TimeSpan.Zero ? targetElapsed : SpanOf(track);
        _checkpoint = GhostRace.Schedule(sport, key => Preferences.Get(key, ""));
        _lastCheckpointElapsed = TimeSpan.Zero;
        _lastCheckpointKm = 0;
    }

    public void Begin(bool learning)
    {
        Learning = learning;
        State = RunStateKind.WaitingForGps;
        ElapsedKilometers = 0;
        Elapsed = TimeSpan.Zero;
        _started = null;
        _previous = null;
        _gainKm = 0;
        _lastAltitudeKm = null;
        _clocks.Clear();
        _waypointSpoken.Clear();
        _announcedTicks = -1;
        _turns.Reset();
        _lastCheckpointElapsed = TimeSpan.Zero;
        _lastCheckpointKm = 0;
        _track.Clear();
        _split = null;
        _speedMps = null;
        PublishReadout();
        Message = AppStrings.WaitingForGpsData;
    }

    /// <summary>Drops the in-memory ride after the account is deleted.</summary>
    public void Forget()
    {
        Begin(false);
        _ghost = [];
        _goal = TimeSpan.Zero;
        _targetElapsed = TimeSpan.Zero;
        ActiveCourse = new ChosenCourse(null, []);
        Latitude = null;
        Longitude = null;
        AccuracyMeters = null;
    }

    /// <summary>Keeps the large time readout moving between GPS samples.</summary>
    public void RefreshClock()
    {
        if (_started is null || State is not (RunStateKind.Racing or RunStateKind.Learning))
            return;
        Elapsed = DateTime.UtcNow - _started.Value;
        PublishReadout();
    }

    public void Go()
    {
        if (State is RunStateKind.Finished or RunStateKind.Aborted)
            return;
        State = Learning ? RunStateKind.Learning : RunStateKind.Racing;
        _started = DateTime.UtcNow;
        _speech.Add(SpeechAtom.Play(AudioCue.StartBeep));
        Message = State.ToString();
    }

    public void Abort()
    {
        State = RunStateKind.Aborted;
        Message = Learning ? AppStrings.alert_dialog_abort_course_title : AppStrings.RaceAborted;
    }

    public void Finish()
    {
        State = RunStateKind.Finished;
        Message = AppStrings.SynchronizationComplete;
    }

    public Waypoint? OnSample(double latitude, double longitude, double? accuracy, double? speedMps, DateTime utc, double? bearingDegrees = null, double? altitudeMeters = null)
    {
        Latitude = latitude;
        Longitude = longitude;
        AccuracyMeters = accuracy;
        if (State is RunStateKind.Finished or RunStateKind.Aborted)
            return null;
        if (State == RunStateKind.WaitingForGps)
            State = RunStateKind.WaitingForData;

        var here = new GeoPosition(latitude, longitude, false);
        if (accuracy is >= RestStopTracker.PoorAccuracyMeters)
        {
            Message = AppStrings.WaitingForGpsData;
            return null;
        }
        if (State == RunStateKind.WaitingForData)
        {
            State = RunStateKind.WaitingForStart;
            Message = "Waiting for start";
        }

        var moving = State is RunStateKind.Learning or RunStateKind.Racing;
        var atRest = speedMps is null or <= 0.3;
        if (!_rest.Accept(moving, atRest, accuracy, utc, out var effective))
            return null;
        if (!moving)
            return null;

        _started ??= effective;
        Elapsed = effective - _started.Value;
        if (_previous is not null)
            ElapsedKilometers += _previous.DistanceTo(here).ToKilometers().Value;
        _previous = here;

        _speedMps = speedMps;
        var speed = speedMps is > 0 ? Speed.FromMetersPerSecond(speedMps.Value) : Speed.AtRest;
        _track.Record(latitude, longitude, effective);
        PublishReadout();
        Message = Learning ? "Learning" : "Racing";
        RememberAltitude(altitudeMeters);
        var learned = ConsiderTurn(bearingDegrees, speed);
        ConsiderWaypoint(here, speed);
        if (!Preferences.Get("Mute", false))
            ConsiderPrompts(here);
        ConsiderGhost(latitude, longitude);

        return learned;
    }

    private void RememberAltitude(double? altitudeMeters)
    {
        if (altitudeMeters is not double meters || meters is < -500 or > 9000)
            return;
        var kilometers = meters / 1000.0;
        if (_lastAltitudeKm is double previous && kilometers > previous)
            _gainKm += kilometers - previous;
        _lastAltitudeKm = kilometers;
    }

    private void ConsiderPrompts(GeoPosition here)
    {
        if (PeriodicMuted(here))
            return;
        foreach (var plan in AudioPrompts.ForSport(_sport, ReadPref))
        {
            if (plan.Kind == "CheckPoint")
                continue;
            var clock = _clocks.GetValueOrDefault(plan.Kind);
            var tick = AudioPrompts.Advance(plan, Elapsed, ElapsedKilometers, clock);
            _clocks[plan.Kind] = tick.Clock;
            if (!tick.Speak)
                continue;
            switch (plan.Kind)
            {
                case "Distance":
                    _speech.Add(SpeechAtom.Say(AppStrings.Distance));
                    _speech.Add(SpeechAtom.Say(DistanceText));
                    break;
                case "Pace":
                    _speech.Add(SpeechAtom.Say(AppStrings.Pace));
                    _speech.Add(SpeechAtom.Say(PaceText));
                    break;
                case "Time":
                    _speech.Add(SpeechAtom.Say(AppStrings.Time));
                    _speech.Add(SpeechAtom.Say(FormatElapsed(Elapsed)));
                    break;
                case "AltGain":
                    _speech.Add(SpeechAtom.Say(AppStrings.Altitude));
                    _speech.Add(SpeechAtom.Say(AppStrings.Gained));
                    _speech.Add(SpeechAtom.Say(Distance.FromKilometers(_gainKm).Format(null)));
                    break;
            }
        }
    }

    private bool PeriodicMuted(GeoPosition here)
    {
        if (State != RunStateKind.Racing)
            return false;
        var finish = ActiveCourse.Waypoints.LastOrDefault(point => point.WaypointType == WaypointType.Finish);
        if (finish is null)
            return false;
        var muteKm = EditorText.ParseDistanceKm(Preferences.Get("MuteWithinFinishDistance", "50m")) ?? 0.05;
        return here.DistanceTo(finish.Position).ToKilometers().Value < muteKm;
    }

    private static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
            : $"{elapsed.Minutes}:{elapsed.Seconds:00}";

    private Waypoint? ConsiderTurn(double? bearingDegrees, Speed speed)
    {
        if (State != RunStateKind.Learning || ActiveCourse.CourseId is not Guid courseId)
            return null;
        if (bearingDegrees is not double bearing || !double.IsFinite(bearing) || bearing < 0)
            return null;

        var sample = new Waypoint
        {
            CourseId = courseId,
            Latitude = Latitude ?? 0,
            Longitude = Longitude ?? 0,
            HeadingDegrees = bearing,
            AltitudeKm = Altitude.Unknown.Kilometers,
            ElapsedDistanceKm = ElapsedKilometers,
            WaypointType = WaypointType.Continue,
            TimestampTicks = DateTime.UtcNow.Ticks
        };
        var detected = _turns.Update(sample, speed);
        if (detected is null)
            return null;
        detected.CourseId = courseId;
        var list = ActiveCourse.Waypoints.ToList();
        list.Add(detected);
        UseCourse(courseId, list);
        _speech.AddRange(WaypointSpeech.Say(detected.WaypointType, Distance.Empty, detected.TextDescription, debug: true));
        if (detected.WaypointType == WaypointType.TurnLeft)
            _speech.Add(SpeechAtom.Say("Turn Left"));
        else if (detected.WaypointType == WaypointType.TurnRight)
            _speech.Add(SpeechAtom.Say("Turn Right"));
        if (!string.IsNullOrWhiteSpace(detected.TextDescription) && detected.TextDescription != WaypointSpeech.TurnDetectorFlag)
            _speech.Add(SpeechAtom.Say(detected.TextDescription));
        return detected;
    }

    public IReadOnlyList<SpeechAtom> TakeSpeech()
    {
        var copy = _speech.ToList();
        _speech.Clear();
        return copy;
    }

    private string? ReadPref(string key) => Preferences.Get(key, "");

    private void PublishReadout()
    {
        var readout = RaceReadout.Create(
            Elapsed,
            ElapsedKilometers,
            _speedMps,
            SportUnits.IsMetric(_sport, ReadPref),
            SportUnits.ShowSpeed(_sport, ReadPref),
            _split);
        TimeText = readout.Time;
        TimeUnit = readout.TimeUnit;
        DistanceValue = readout.DistanceValue;
        DistanceUnit = readout.DistanceUnit;
        DistanceText = readout.DistanceText;
        PaceValue = readout.Pace;
        PaceUnit = readout.PaceUnit;
        PaceText = readout.PaceText;
        SpeedText = readout.PaceText;
        CheckpointText = readout.Checkpoint;
        CheckpointUnit = readout.CheckpointUnit;
    }

    private void ConsiderGhost(double latitude, double longitude)
    {
        if (Learning || State != RunStateKind.Racing || _ghost.Count == 0)
            return;
        if (!_checkpoint.Due(Elapsed, ElapsedKilometers, _lastCheckpointElapsed, _lastCheckpointKm))
            return;
        if (GhostRace.Compare(_ghost, latitude, longitude, Elapsed, _goal, _targetElapsed) is not GhostSplit split)
            return;
        _lastCheckpointElapsed = Elapsed;
        _lastCheckpointKm = ElapsedKilometers;
        _split = split;
        PublishReadout();
        foreach (var phrase in GhostRace.Phrases(split))
            _speech.Add(SpeechAtom.Say(phrase));
        Message = string.Join(' ', GhostRace.Phrases(split));
    }

    private static TimeSpan SpanOf(IReadOnlyList<TrackPoint> track)
    {
        if (track.Count < 2)
            return TimeSpan.Zero;
        var ordered = track.OrderBy(point => point.TimestampTicks).ToList();
        return TimeSpan.FromTicks(Math.Max(0, ordered[^1].TimestampTicks - ordered[0].TimestampTicks));
    }

    private void ConsiderWaypoint(GeoPosition here, Speed speed)
    {
        if (WaypointApproach.ShouldIgnoreBecauseAtRest(speed))
            return;
        var next = NextSpokenWaypoint(here);
        if (next is null)
            return;
        var prompt = AudioPrompts.ForWaypoint(next.WaypointType.ToString(), ReadPref);
        var gapKm = here.DistanceTo(next.Position).ToKilometers().Value;
        var remaining = next.ElapsedDistanceKm > 0
            ? Math.Max(0, next.ElapsedDistanceKm - ElapsedKilometers)
            : gapKm;
        if (next.ElapsedDistanceKm > 0 && gapKm > prompt.ForKm + 0.05 && remaining <= prompt.ForKm)
            return;
        var always = next.WaypointType == WaypointType.Finish;
        var last = _waypointSpoken.TryGetValue(next.TimestampTicks, out var spoken) ? spoken : (double?)null;
        var arrival = gapKm < 0.04;
        if (arrival)
        {
            var state = new WaypointCountdownState { CurToTargetKilometers = gapKm, PrevToTargetKilometers = gapKm + 0.01 };
            WaypointCountdown.Shutdown(state);
            _speech.AddRange(WaypointSpeech.Say(next.WaypointType, Distance.FromKilometers(gapKm), next.TextDescription, false));
            _announcedTicks = next.TimestampTicks;
            return;
        }
        if (AudioPrompts.ApproachDue(prompt, always, remaining, last))
        {
            _speech.AddRange(WaypointSpeech.Say(next.WaypointType, Distance.FromKilometers(remaining), next.TextDescription, false));
            _waypointSpoken[next.TimestampTicks] = remaining;
            return;
        }
        var along = Math.Max(0, next.ElapsedDistanceKm - ElapsedKilometers);
        var eta = WaypointApproach.UpdatedEta(gapKm, along, speed.ToKilometersPerSecond().Value, TimeSpan.Zero);
        if (WaypointApproach.ShouldStartCountdown(eta, false) && eta <= WaypointCountdown.InitialCountDown
            && WaypointSpeech.PlayApproachCountdown(next.WaypointType, Distance.Empty))
        {
            _speech.Add(SpeechAtom.Play(AudioCue.Start54321));
        }
    }

    private Waypoint? NextSpokenWaypoint(GeoPosition here)
    {
        var ordered = ActiveCourse.Waypoints.OrderBy(point => point.ElapsedDistanceKm).ThenBy(point => point.TimestampTicks).ToList();
        var stamped = ordered.Any(point => point.ElapsedDistanceKm > 0);
        Waypoint? nearest = null;
        var nearestGap = double.MaxValue;
        foreach (var waypoint in ordered)
        {
            if (waypoint.TimestampTicks == _announcedTicks)
                continue;
            if (stamped && waypoint.ElapsedDistanceKm + 0.05 < ElapsedKilometers)
                continue;
            var prompt = AudioPrompts.ForWaypoint(waypoint.WaypointType.ToString(), ReadPref);
            if (waypoint.WaypointType != WaypointType.Finish && prompt.Mode.Equals("Off", StringComparison.OrdinalIgnoreCase))
                continue;
            if (stamped)
                return waypoint;
            var gap = here.DistanceTo(waypoint.Position).ToKilometers().Value;
            if (gap < nearestGap)
            {
                nearest = waypoint;
                nearestGap = gap;
            }
        }
        return nearest;
    }
}

public interface IAudioCuePlayer
{
    Task PlayAsync(AudioCue cue);
}

public interface IBluetoothDevices
{
    bool IsAudioDevicePaired();
    IReadOnlyList<string> PairedAudioDeviceNames();
}

public interface IPushNotifications
{
    Task<string?> RegisterAsync();
}

public sealed class SpeechFeedback
{
    private readonly IAudioCuePlayer _audio;

    public SpeechFeedback(IAudioCuePlayer audio) => _audio = audio;

    public async Task SpeakAsync(IEnumerable<SpeechAtom> atoms)
    {
        if (Preferences.Get("Mute", false))
            return;
        foreach (var atom in atoms)
        {
            if (atom.Kind == SpeechAtomKind.Words)
                await TextToSpeech.Default.SpeakAsync(atom.Value);
            else if (Enum.TryParse<AudioCue>(atom.Value, out var cue))
                await _audio.PlayAsync(cue);
        }
    }
}

public sealed partial class PackageAudioPlayer : IAudioCuePlayer
{
    public async Task PlayAsync(AudioCue cue)
    {
        var file = cue switch
        {
            AudioCue.Beep => "beep.wav",
            AudioCue.StartBeep => "start_beep.wav",
            AudioCue.Start54321 => "start54321.wav",
            AudioCue.Silence => "silence.mp3",
            _ => "beep.wav"
        };
        await using var stream = await FileSystem.OpenAppPackageFileAsync(file);
        var cache = Path.Combine(FileSystem.CacheDirectory, file);
        await using (var output = File.Create(cache))
            await stream.CopyToAsync(output);
        await PlayFileAsync(cache, cue == AudioCue.Silence && Preferences.Get("KeepAudioHeadphonesAlive", false));
    }

    private static partial Task PlayFileAsync(string path, bool loop);
}

public sealed class UnavailableBluetooth : IBluetoothDevices
{
    public bool IsAudioDevicePaired() => false;
    public IReadOnlyList<string> PairedAudioDeviceNames() => [];
}

/// <summary>
/// GCM is gone. Registration waits until an FCM sender id or APNs key is configured.
/// The historical sender id from the 1.44 client is not used and is not called.
/// </summary>
public sealed class PlaceholderPush : IPushNotifications
{
    public Task<string?> RegisterAsync()
    {
        var sender = Preferences.Get("FcmSenderId", "");
        return Task.FromResult(string.IsNullOrWhiteSpace(sender) ? null : (string?)"");
    }
}

public partial class TrackingHost
{
    public void Start() => StartPlatform();
    public void Stop() => StopPlatform();

    partial void StartPlatform();
    partial void StopPlatform();
}
