namespace GpsTrainer.App;

static class Services
{
    public static IServiceProvider Current =>
        IPlatformApplication.Current?.Services
        ?? Application.Current?.Handler?.MauiContext?.Services
        ?? throw new InvalidOperationException("Application services are not available.");
}

static class Ui
{
    public static VerticalStackLayout Column(params View[] views)
    {
        var layout = new VerticalStackLayout { Padding = 16, Spacing = 10 };
        foreach (var view in views)
            layout.Add(view);
        return layout;
    }

    public static Label Heading(string text) => new()
    {
        Text = text,
        FontSize = 26,
        FontAttributes = FontAttributes.Bold,
        LineBreakMode = LineBreakMode.WordWrap
    };

    public static View Labeled(string caption, View field)
    {
        field.HorizontalOptions = LayoutOptions.Fill;
        if (field.MinimumHeightRequest < 48)
            field.MinimumHeightRequest = 48;
        return new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                new Label { Text = caption, LineBreakMode = LineBreakMode.WordWrap },
                field
            }
        };
    }

    /// <summary>Label on the left, switch on the right, one row, at least 48dp tall.</summary>
    public static View SwitchRow(string title, string? description, Switch toggle)
    {
        toggle.HorizontalOptions = LayoutOptions.End;
        toggle.VerticalOptions = LayoutOptions.Center;
        var text = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill
        };
        text.Add(new Label
        {
            Text = title,
            LineBreakMode = LineBreakMode.WordWrap,
            VerticalOptions = LayoutOptions.Center
        });
        if (!string.IsNullOrWhiteSpace(description))
        {
            text.Add(new Label
            {
                Text = description,
                FontSize = 13,
                LineBreakMode = LineBreakMode.WordWrap,
                TextColor = Application.Current?.RequestedTheme == AppTheme.Dark
                    ? Color.FromArgb("#C8C8C8")
                    : Color.FromArgb("#404040")
            });
        }
        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12,
            Padding = new Thickness(0, 4),
            MinimumHeightRequest = 48
        };
        row.Add(text, 0, 0);
        row.Add(toggle, 1, 0);
        return row;
    }

    public static View FactRow(string title, string value)
    {
        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(108)),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 12,
            MinimumHeightRequest = 48
        };
        row.Add(new Label
        {
            Text = title,
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.WordWrap
        }, 0, 0);
        row.Add(new Label
        {
            Text = value,
            HorizontalTextAlignment = TextAlignment.End,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.WordWrap
        }, 1, 0);
        return row;
    }

    public static VerticalStackLayout IntervalList(IReadOnlyList<Waypoint> waypoints)
    {
        var lines = IntervalCourse.Describe(waypoints);
        var layout = new VerticalStackLayout { Spacing = 4 };
        layout.Add(new Label { Text = "Intervals", FontAttributes = FontAttributes.Bold });
        if (lines.Count == 0)
            layout.Add(new Label { Text = "No intervals on this course." });
        foreach (var line in lines)
            layout.Add(new Label { Text = line });
        return layout;
    }

    public static Entry Box(string placeholder, bool password = false) => new()
    {
        Placeholder = placeholder,
        IsPassword = password
    };

    public static Button Command(string text, Func<Task> action)
    {
        var button = new Button { Text = text };
        button.Clicked += async (_, _) =>
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                if (Application.Current?.Windows.Count > 0)
                    await Application.Current.Windows[0].Page!.DisplayAlertAsync(AppStrings.warning_title, ex.Message, AppStrings.confirm);
            }
        };
        return button;
    }

    public static Task Push(Page page) =>
        (Application.Current?.Windows[0].Page as NavigationPage)?.PushAsync(page) ?? Task.CompletedTask;
}

public class LoginPage : ContentPage
{
    private readonly Entry _login = Ui.Box(AppStrings.UserName);
    private readonly Entry _password = Ui.Box(AppStrings.Password, password: true);
    private readonly Label _message = new();

    public LoginPage()
    {
        Title = AppStrings.app_name;
        _login.Text = Preferences.Get("SignIn", "");
        if (Preferences.Get("SavePassword", true))
            _password.Text = Preferences.Get("Password", "");
        Content = new ScrollView
        {
            Content = Ui.Column(
                new Image { Source = "gpstrainer.png", HeightRequest = 72, WidthRequest = 72, HorizontalOptions = LayoutOptions.Center },
                Ui.Heading(AppStrings.app_name),
                Ui.Labeled(AppStrings.UserName, _login),
                Ui.Labeled(AppStrings.Password, _password),
                Ui.Command(AppStrings.login_login_button, SignIn),
                Ui.Command(AppStrings.signup_button, () => Ui.Push(new SignupPage())),
                _message)
        };
    }

    private async Task SignIn()
    {
        var session = Services.Current.GetRequiredService<AppSession>();
        var api = Services.Current.GetRequiredService<TrainerApi>();
        var store = Services.Current.GetRequiredService<TrainerRepository>();
        var login = _login.Text?.Trim() ?? "";
        var password = _password.Text ?? "";
        if (login.Length == 0)
        {
            _message.Text = AppStrings.please_enter_your_login_name;
            return;
        }
        try
        {
            var remote = await api.LoginAsync(login, password);
            if (remote is not null)
            {
                var local = store.FindUser(login) ?? new UserAccount { Login = login };
                local.Password = password;
                local.FirstName = remote.User.FirstName;
                local.LastName = remote.User.LastName;
                local.Email = remote.User.Email;
                local.IsActive = remote.User.IsActive;
                local.ExpirationDate = remote.User.ExpirationDate;
                store.Remember(local);
                session.User = local;
                session.Token = remote.Token;
            }
        }
        catch (Exception ex)
        {
            _message.Text = AppStrings.no_internet_connection + " " + ex.Message;
        }

        if (session.User is null && store.OfflineSignIn(login, password))
            session.User = store.FindUser(login);

        if (session.User is null)
        {
            _message.Text = AppStrings.signin_failed;
            return;
        }
        if (Preferences.Get("SavePassword", true))
        {
            Preferences.Set("SignIn", login);
            Preferences.Set("Password", password);
        }
        var push = Services.Current.GetRequiredService<IPushNotifications>();
        var token = await push.RegisterAsync();
        if (!string.IsNullOrEmpty(token))
            await api.RegisterPushAsync(login, token);
        await Ui.Push(new CourseListPage());
    }
}

public class SignupPage : ContentPage
{
    private readonly Entry _login = Ui.Box(AppStrings.UserName);
    private readonly Entry _password = Ui.Box(AppStrings.Password, password: true);
    private readonly Entry _email = Ui.Box(AppStrings.email);
    private readonly Entry _first = Ui.Box(AppStrings.firstName);
    private readonly Entry _last = Ui.Box(AppStrings.LastName);
    private readonly Entry _phone = Ui.Box(AppStrings.phone);
    private readonly Label _message = new();

    public SignupPage()
    {
        Title = AppStrings.signup_button;
        Content = new ScrollView
        {
            Content = Ui.Column(
                Ui.Heading(AppStrings.signup_button),
                Ui.Labeled(AppStrings.UserName, _login),
                Ui.Labeled(AppStrings.Password, _password),
                Ui.Labeled(AppStrings.email, _email),
                Ui.Labeled(AppStrings.firstName, _first),
                Ui.Labeled(AppStrings.LastName, _last),
                Ui.Labeled(AppStrings.phone, _phone),
                Ui.Command(AppStrings.signup_continue_button, Submit),
                _message)
        };
    }

    private async Task Submit()
    {
        if (!EmailValidator.Validate(_email.Text))
        {
            _message.Text = AppStrings.email;
            return;
        }
        var dto = new UserWriteDto
        {
            Login = _login.Text?.Trim() ?? "",
            Password = _password.Text ?? "",
            Email = _email.Text?.Trim() ?? "",
            FirstName = _first.Text ?? "",
            LastName = _last.Text ?? "",
            PhoneNumber = _phone.Text ?? "",
            Country = "US",
            IsMale = true,
            WantsEmails = true
        };
        var result = await Services.Current.GetRequiredService<TrainerApi>().SignUpAsync(dto);
        _message.Text = result switch
        {
            "ok" => "Account created.",
            "DuplicateUserName" => "That user name is already registered.",
            "DuplicateEmail" => result,
            _ => result
        };
        if (result == "ok")
        {
            var account = new UserAccount
            {
                Login = dto.Login,
                Password = dto.Password,
                Email = dto.Email,
                FirstName = dto.FirstName,
                LastName = dto.LastName,
                PhoneNumber = dto.PhoneNumber,
                ExpirationDate = DateTime.UtcNow.AddYears(10)
            };
            Services.Current.GetRequiredService<TrainerRepository>().Remember(account);
            await Navigation.PopAsync();
        }
    }
}

public class CourseListPage : ContentPage
{
    private readonly VerticalStackLayout _list = new() { Spacing = 8 };

    public CourseListPage()
    {
        Title = AppStrings.app_name;
        ToolbarItems.Add(new ToolbarItem(AppStrings.menu_options, null, () => Ui.Push(new PreferencesPage())));
        ToolbarItems.Add(new ToolbarItem(AppStrings.menu_data, null, () => Ui.Push(new DataPage())));
        ToolbarItems.Add(new ToolbarItem(AppStrings.menu_gps_info, null, () => Ui.Push(new GpsInfoPage())));
        ToolbarItems.Add(new ToolbarItem(AppStrings.menu_about, null, () => Ui.Push(new AboutPage())));
        Content = new ScrollView
        {
            Content = Ui.Column(
                Ui.Heading("Courses"),
                _list,
                Ui.Command(AppStrings.menu_insert_course, () => Ui.Push(new CourseEditPage(null))),
                Ui.Command("Import GPX", ImportGpx))
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _list.Clear();
        var store = Services.Current.GetRequiredService<TrainerRepository>();
        var login = Services.Current.GetRequiredService<AppSession>().User?.Login;
        var courses = store.Database.ListCourses(login);
        if (courses.Count == 0)
        {
            _list.Add(new Label { Text = AppStrings.empty_course_list });
            return;
        }
        foreach (var course in courses)
        {
            var captured = course;
            _list.Add(new Button
            {
                Text = $"{captured.Name}  {Distance.FromKilometers(captured.ElapsedDistanceKm).Format(null)}",
                Command = new Command(async () =>
                {
                    Services.Current.GetRequiredService<AppSession>().Course = captured;
                    await Ui.Push(new CourseDetailsPage(captured));
                })
            });
        }
    }

    private static async Task ImportGpx()
    {
        var session = Services.Current.GetRequiredService<AppSession>();
        if (string.IsNullOrEmpty(session.Token))
            throw new InvalidOperationException("Sign in before importing a GPX file.");
        var picked = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "GPX course",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.Android, ["application/gpx+xml", "text/xml", "application/xml", "application/octet-stream"] },
                { DevicePlatform.iOS, ["com.topografix.gpx", "public.xml"] },
                { DevicePlatform.MacCatalyst, ["gpx", "xml"] },
                { DevicePlatform.WinUI, [".gpx", ".xml"] }
            })
        });
        if (picked is null)
            return;
        await using var stream = await picked.OpenReadAsync();
        using var reader = new StreamReader(stream);
        var xml = await reader.ReadToEndAsync();
        if (xml.Length > GpxCourse.MaxCharacters)
            throw new InvalidOperationException("GPX file is too large.");
        var preview = await Services.Current.GetRequiredService<TrainerApi>().PreviewGpxAsync(xml, picked.FileName);
        if (preview is null)
            throw new InvalidOperationException("GPX was not imported.");
        preview.Course.Login = session.User?.Login ?? preview.Course.Login;
        preview.Course.Publish = false;
        await Ui.Push(new CourseEditPage(preview.Course, preview.Waypoints));
    }
}

public class CourseEditPage : ContentPage
{
    private readonly Course _course;
    private readonly IReadOnlyList<Waypoint>? _imported;
    private readonly Entry _name = Ui.Box(AppStrings.course_name);
    private readonly Entry _comments = Ui.Box(AppStrings.course_comments);
    private readonly Picker _activity = new() { Title = AppStrings.course_activity };

    public CourseEditPage(Course? existing, IReadOnlyList<Waypoint>? imported = null)
    {
        _course = existing ?? new Course();
        _imported = imported;
        Title = existing is null ? AppStrings.menu_insert_course : AppStrings.menu_edit_course;
        _name.Text = _course.Name;
        _comments.Text = _course.Comments;
        var codes = Services.Current.GetRequiredService<TrainerRepository>().Database.ListActivityCodes();
        foreach (var code in codes)
            _activity.Items.Add(code.ActValue);
        var index = codes.ToList().FindIndex(c => c.ActKey == _course.ActivityCode);
        if (index >= 0)
            _activity.SelectedIndex = index;
        var views = new List<View>
        {
            Ui.Heading(Title),
            Ui.Labeled(AppStrings.course_name, _name),
            Ui.Labeled(AppStrings.course_activity, _activity),
            Ui.Labeled(AppStrings.course_comments, _comments)
        };
        if (_imported is { Count: > 0 })
        {
            views.Add(new Label
            {
                Text = $"{_imported.Count} waypoints, {Distance.FromKilometers(_course.ElapsedDistanceKm).Format(null)}. Gain {_course.AltitudeGainKm:0.000} km, loss {_course.AltitudeLossKm:0.000} km. Review the course, then save. It stays unpublished."
            });
            var map = new LeafletMap();
            map.Show(_imported.Select(p => (p.Latitude, p.Longitude, p.WaypointType.ToString())));
            views.Add(map);
        }
        views.Add(Ui.Command(AppStrings.confirm, Save));
        Content = new ScrollView { Content = Ui.Column(views.ToArray()) };
    }

    private async Task Save()
    {
        if (string.IsNullOrWhiteSpace(_name.Text))
        {
            await DisplayAlertAsync(AppStrings.warning_title, AppStrings.course_name, AppStrings.confirm);
            return;
        }
        var store = Services.Current.GetRequiredService<TrainerRepository>();
        var codes = store.Database.ListActivityCodes().ToList();
        _course.Name = _name.Text.Trim();
        _course.Comments = _comments.Text ?? "";
        _course.ActivityCode = _activity.SelectedIndex >= 0 ? codes[_activity.SelectedIndex].ActKey : 1;
        _course.Login = Services.Current.GetRequiredService<AppSession>().User?.Login ?? "";
        _course.Timestamp = DateTime.UtcNow;
        if (_imported is not null)
        {
            _course.Publish = false;
            _course.WaypointTimestamp = DateTime.UtcNow;
            if (_imported.Count > 0)
                _course.ElapsedDistanceKm = _imported.Max(point => point.ElapsedDistanceKm);
        }
        var isNew = store.Database.ListCourses().All(c => c.CourseId != _course.CourseId);
        store.Database.UpsertCourse(_course);
        if (_imported is not null)
            store.Database.ReplaceWaypoints(_course.CourseId, _imported);
        store.Database.RecordAction(isNew ? ActionType.InsertCourse : ActionType.ModifyCourse, _course.CourseId);
        Services.Current.GetRequiredService<AppSession>().Course = _course;
        await Navigation.PopAsync();
    }
}

public class CourseDetailsPage : ContentPage
{
    private readonly Course _course;

    public CourseDetailsPage(Course course)
    {
        _course = course;
        Title = course.Name;
        var map = new LeafletMap();
        var points = Services.Current.GetRequiredService<TrainerRepository>().Database.ListWaypoints(course.CourseId);
        var home = Services.Current.GetRequiredService<AppSession>().User;
        map.Show(
            points.Select(p => (p.Latitude, p.Longitude, p.WaypointType.ToString())),
            centerLatitude: home?.Latitude,
            centerLongitude: home?.Longitude);
        Content = new ScrollView
        {
            Content = Ui.Column(
                Ui.Heading(course.Name),
                new Label { Text = course.Comments },
                new Label { Text = Distance.FromKilometers(course.ElapsedDistanceKm).Format(null) },
                Ui.IntervalList(points),
                map,
                new Label { Text = "Learning mode records the ride and adds turn waypoints. Map mode adds a waypoint where you tap." },
                Ui.Command("Learning mode", () => Start(learning: true)),
                Ui.Command("Map mode", () => Ui.Push(new MapCoursePage(course))),
                Ui.Command("Download GPX", DownloadGpx),
                Ui.Command(AppStrings.menu_edit_course, () => Ui.Push(new CourseEditPage(course))),
                Ui.Command("Races", () => Ui.Push(new RaceListPage(course))),
                Ui.Command(AppStrings.menu_delete_course, Delete))
        };
    }

    private async Task DownloadGpx()
    {
        var session = Services.Current.GetRequiredService<AppSession>();
        string? xml = null;
        if (!string.IsNullOrEmpty(session.Token))
            xml = await Services.Current.GetRequiredService<TrainerApi>().ExportGpxAsync(_course.CourseId);
        if (string.IsNullOrEmpty(xml))
        {
            var points = Services.Current.GetRequiredService<TrainerRepository>().Database.ListWaypoints(_course.CourseId);
            xml = GpxCourse.Export(_course.Name, points, IntervalCourse.FromMarkers(_course.CourseId, points));
        }
        var safe = new string(_course.Name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(safe))
            safe = "course";
        var path = Path.Combine(FileSystem.CacheDirectory, safe + ".gpx");
        await File.WriteAllTextAsync(path, xml);
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = safe + ".gpx",
            File = new ShareFile(path, "application/gpx+xml")
        });
    }

    private async Task Start(bool learning)
    {
        var session = Services.Current.GetRequiredService<AppSession>();
        session.Course = _course;
        var run = Services.Current.GetRequiredService<RunSession>();
        var store = Services.Current.GetRequiredService<TrainerRepository>();
        run.UseCourse(_course.CourseId, store.Database.ListWaypoints(_course.CourseId));
        run.UseSport(store.Database.ListActivityCodes().FirstOrDefault(code => code.ActKey == _course.ActivityCode)?.ActValue);
        run.Begin(learning);
        await Ui.Push(new RunningPage());
    }

    private async Task Delete()
    {
        var yes = await DisplayAlertAsync(AppStrings.alert_dialog_delete_course_title, _course.Name, AppStrings.confirm, AppStrings.cancel);
        if (!yes)
            return;
        var store = Services.Current.GetRequiredService<TrainerRepository>();
        store.Database.DeleteCourse(_course.CourseId);
        store.Database.RecordAction(ActionType.DeleteCourse, _course.CourseId);
        await Navigation.PopAsync();
    }
}

public class RaceListPage : ContentPage
{
    private readonly Course _course;
    private readonly VerticalStackLayout _list = new() { Spacing = 8 };

    public RaceListPage(Course course)
    {
        _course = course;
        Title = course.Name;
        Content = new ScrollView
        {
            Content = Ui.Column(Ui.Heading("Races"), _list, Ui.Command("New race", Create))
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _list.Clear();
        var races = Services.Current.GetRequiredService<TrainerRepository>().Database.ListRaces(_course.CourseId);
        if (races.Count == 0)
            _list.Add(new Label { Text = AppStrings.empty_race_list });
        foreach (var race in races)
        {
            var captured = race;
            _list.Add(new Button
            {
                Text = $"{captured.StartTime.ToLocalTime():g}  {captured.Comments}",
                Command = new Command(async () => await Ui.Push(new RaceDetailsPage(_course, captured)))
            });
        }
    }

    private async Task Create()
    {
        var race = new Race
        {
            CourseId = _course.CourseId,
            Login = Services.Current.GetRequiredService<AppSession>().User?.Login ?? "",
            StartTime = DateTime.UtcNow,
            FinishTime = DateTime.UtcNow,
            Timestamp = DateTime.UtcNow,
            ElapsedDistanceKm = _course.ElapsedDistanceKm
        };
        var store = Services.Current.GetRequiredService<TrainerRepository>();
        store.Database.UpsertRace(race);
        store.Database.RecordAction(ActionType.InsertRace, race.RaceId);
        await Ui.Push(new RaceEditPage(race));
    }
}

public class RaceEditPage : ContentPage
{
    private readonly Race _race;
    private readonly Entry _comments = Ui.Box(AppStrings.race_comments);

    public RaceEditPage(Race race)
    {
        _race = race;
        Title = AppStrings.menu_edit_race;
        _comments.Text = race.Comments;
        Content = new ScrollView
        {
            Content = Ui.Column(Ui.Heading(Title), Ui.Labeled(AppStrings.race_comments, _comments), Ui.Command(AppStrings.confirm, Save))
        };
    }

    private async Task Save()
    {
        _race.Comments = _comments.Text ?? "";
        _race.Timestamp = DateTime.UtcNow;
        var store = Services.Current.GetRequiredService<TrainerRepository>();
        store.Database.UpsertRace(_race);
        store.Database.RecordAction(ActionType.ModifyRace, _race.RaceId);
        await Navigation.PopAsync();
    }
}

public class RaceDetailsPage : ContentPage
{
    public RaceDetailsPage(Course course, Race race)
    {
        Title = course.Name;
        Content = new ScrollView
        {
            Content = Ui.Column(
            Ui.Heading(course.Name),
            new Label { Text = race.Comments },
            new Label { Text = Distance.FromKilometers(race.ElapsedDistanceKm).Format(null) },
            new Label { Text = "Goal " + race.GoalTime },
            Ui.IntervalList(Services.Current.GetRequiredService<TrainerRepository>().Database.ListWaypoints(course.CourseId)),
            Ui.Command(AppStrings.menu_edit_race, () => Ui.Push(new RaceEditPage(race))),
            Ui.Command("Stage", () =>
            {
                Services.Current.GetRequiredService<AppSession>().Race = race;
                Services.Current.GetRequiredService<AppSession>().Course = course;
                return Ui.Push(new StagingPage(course, race));
            }),
            Ui.Command(AppStrings.menu_delete_race, async () =>
            {
                var store = Services.Current.GetRequiredService<TrainerRepository>();
                store.Database.DeleteRace(race.RaceId);
                store.Database.RecordAction(ActionType.DeleteRace, race.RaceId);
                await Navigation.PopAsync();
            }))
        };
    }
}

public class StagingPage : ContentPage
{
    private readonly Label _distance = new();
    private readonly Course _course;
    private readonly Race _race;
    private readonly double? _startLatitude;
    private readonly double? _startLongitude;
    private bool _listening;
    private double? _lastStartSpokenKm;

    public StagingPage(Course course, Race race)
    {
        _course = course;
        _race = race;
        Title = "Staging";
        var map = new LeafletMap();
        var points = Services.Current.GetRequiredService<TrainerRepository>().Database.ListWaypoints(course.CourseId);
        var home = Services.Current.GetRequiredService<AppSession>().User;
        map.Show(
            points.Select(p => (p.Latitude, p.Longitude, p.WaypointType.ToString())),
            centerLatitude: home?.Latitude,
            centerLongitude: home?.Longitude);
        var start = points.FirstOrDefault(p => p.WaypointType == WaypointType.Start) ?? points.FirstOrDefault();
        if (start is null)
        {
            _distance.Text = "No start waypoint";
        }
        else
        {
            _startLatitude = start.Latitude;
            _startLongitude = start.Longitude;
            _distance.Text = "Waiting for GPS";
        }
        _distance.FontSize = 22;
        Content = new ScrollView
        {
            Content = Ui.Column(Ui.Heading(course.Name), new Label { Text = "Distance to start" }, _distance, map,
                Ui.Command(AppStrings.start, Start))
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_startLatitude is null || _listening)
            return;
        try
        {
            await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            Geolocation.LocationChanged += OnLocation;
            _listening = true;
            await Geolocation.StartListeningForegroundAsync(new GeolocationListeningRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(1)));
        }
        catch (Exception)
        {
            _distance.Text = "Waiting for GPS";
        }
    }

    protected override void OnDisappearing()
    {
        if (_listening)
        {
            Geolocation.LocationChanged -= OnLocation;
            _listening = false;
        }
        base.OnDisappearing();
    }

    private void OnLocation(object? sender, GeolocationLocationChangedEventArgs args)
    {
        if (_startLatitude is not double startLat || _startLongitude is not double startLon)
            return;
        var latitude = args.Location.Latitude;
        var longitude = args.Location.Longitude;
        MainThread.BeginInvokeOnMainThread(() => UpdateDistance(latitude, longitude, startLat, startLon));
    }

    private void UpdateDistance(double latitude, double longitude, double startLatitude, double startLongitude)
    {
        var kilometers = GeoMath.DistanceMeters(latitude, longitude, startLatitude, startLongitude) / 1000.0;
        _distance.Text = AudioPrompts.DistanceToStart(latitude, longitude, startLatitude, startLongitude);
        var prompt = AudioPrompts.ForWaypoint("Start", key => Preferences.Get(key, ""));
        if (!AudioPrompts.StagingDue(prompt, kilometers, _lastStartSpokenKm, out var spoken) || Preferences.Get("Mute", false))
        {
            _lastStartSpokenKm = spoken;
            return;
        }
        _lastStartSpokenKm = spoken;
        _ = Services.Current.GetRequiredService<SpeechFeedback>().SpeakAsync(
            [SpeechAtom.Say("Start"), SpeechAtom.Say(_distance.Text)]);
    }

    private async Task Start()
    {
        var store = Services.Current.GetRequiredService<TrainerRepository>();
        var run = Services.Current.GetRequiredService<RunSession>();
        run.UseCourse(_course.CourseId, store.Database.ListWaypoints(_course.CourseId));
        run.Begin(learning: false);
        var targetId = _race.TargetRaceId != Guid.Empty ? _race.TargetRaceId : _race.RaceId;
        var target = store.Database.ListRaces(_course.CourseId).FirstOrDefault(race => race.RaceId == targetId) ?? _race;
        var sport = store.Database.ListActivityCodes().FirstOrDefault(code => code.ActKey == _course.ActivityCode)?.ActValue;
        run.UseSport(sport);
        run.UseGhost(
            store.Database.ListPositions(target.RaceId),
            _race.GoalTime,
            TimeSpan.FromTicks(Math.Max(0, target.ElapsedTimeTicks)),
            sport);
        run.Go();
        Services.Current.GetRequiredService<AppSession>().Race = _race;
        await Ui.Push(new RunningPage());
    }
}

public class RunningPage : ContentPage
{
    private readonly Label _state = new() { FontSize = 18 };
    private readonly Label _time = ReadoutValue("00:00:00");
    private readonly Label _timeUnit = ReadoutUnit("sec");
    private readonly Label _distance = ReadoutValue("0.00");
    private readonly Label _distanceUnit = ReadoutUnit("mi");
    private readonly Label _pace = ReadoutValue("0.0");
    private readonly Label _paceUnit = ReadoutUnit("mph");
    private readonly Label _checkpoint = ReadoutValue("00:00:00");
    private readonly Label _checkpointUnit = ReadoutUnit("sec");
    private readonly Grid _checkpointRow;
    private readonly Button _addWaypoint = new() { Text = "Add Waypoint", IsVisible = false };
    private readonly Button _useLocation = new() { Text = "Use location", IsVisible = false };
    private View? _ride;
    private IDispatcherTimer? _timer;
    private bool _listening;
    private bool _tracking;
    private bool _locationOff;
    private const string LocationChoiceKey = "BackgroundLocationChoice";
    private const string LocationOffMessage = "Location is off. This ride will not record a track.";

    public RunningPage()
    {
        var learning = Services.Current.GetRequiredService<RunSession>().Learning;
        Title = learning ? "Learning mode" : AppStrings.app_name;
        _addWaypoint.Clicked += async (_, _) => await Ui.Push(new InsertWaypointPage());
        _useLocation.Clicked += (_, _) => ShowDisclosure();
        _checkpointRow = ReadoutRow("ChkPnt:", _checkpoint, _checkpointUnit);
        _checkpointRow.IsVisible = !learning;
        var readouts = new VerticalStackLayout
        {
            Spacing = 16,
            Padding = new Thickness(4, 8),
            Children =
            {
                ReadoutRow("Time:", _time, _timeUnit),
                ReadoutRow("Distance:", _distance, _distanceUnit),
                ReadoutRow("Pace:", _pace, _paceUnit),
                _checkpointRow,
                _state,
                _useLocation,
                _addWaypoint
            }
        };
        _ride = new ScrollView
        {
            Content = Ui.Column(readouts,
                Ui.Command(AppStrings.start, async () =>
                {
                    Services.Current.GetRequiredService<RunSession>().Go();
                    await Refresh();
                }),
                Ui.Command("Finish", async () =>
                {
                    var run = Services.Current.GetRequiredService<RunSession>();
                    run.Finish();
                    await SaveTrace(run);
                    await Navigation.PopAsync();
                }),
                Ui.Command(AppStrings.cancel, async () =>
                {
                    Services.Current.GetRequiredService<RunSession>().Abort();
                    await Navigation.PopAsync();
                }))
        };
    }

    private static Label ReadoutValue(string text) => new()
    {
        Text = text,
        FontSize = 26,
        FontAttributes = FontAttributes.Bold,
        HorizontalTextAlignment = TextAlignment.End,
        HorizontalOptions = LayoutOptions.Fill,
        LineBreakMode = LineBreakMode.NoWrap
    };

    private static Label ReadoutUnit(string text) => new()
    {
        Text = text,
        FontSize = 16,
        FontAttributes = FontAttributes.Bold,
        WidthRequest = 64,
        VerticalOptions = LayoutOptions.Center
    };

    private static Grid ReadoutRow(string caption, Label value, Label unit)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 8,
            MinimumHeightRequest = 48
        };
        grid.Add(new Label { Text = caption, FontSize = 18, VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.NoWrap }, 0, 0);
        grid.Add(value, 1, 0);
        grid.Add(unit, 2, 0);
        return grid;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        var choice = Preferences.Get(LocationChoiceKey, "");
        if (choice == "allow")
        {
            _ = BeginTracking();
            return;
        }
        if (choice == "decline")
        {
            DeclineLocation();
            return;
        }
        ShowDisclosure();
    }

    private void ShowDisclosure()
    {
        var allow = new Button
        {
            Text = "Allow",
            FontSize = 18,
            HeightRequest = 52,
            BackgroundColor = Color.FromArgb("#163A26"),
            TextColor = Colors.White
        };
        allow.Clicked += async (_, _) =>
        {
            Preferences.Set(LocationChoiceKey, "allow");
            Content = _ride;
            await BeginTracking();
        };
        var decline = new Button
        {
            Text = "No thanks",
            FontSize = 18,
            HeightRequest = 52,
            BackgroundColor = Color.FromArgb("#E6EEF4"),
            TextColor = Color.FromArgb("#163A26")
        };
        decline.Clicked += (_, _) =>
        {
            Preferences.Set(LocationChoiceKey, "decline");
            Content = _ride;
            DeclineLocation();
        };
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(24, 28),
                Spacing = 16,
                Children =
                {
                    new Label
                    {
                        Text = "Location while you ride",
                        FontSize = 28,
                        FontAttributes = FontAttributes.Bold
                    },
                    new Label
                    {
                        FontSize = 18,
                        Text = "gpsTrainer collects your location while a ride is being tracked, including when the app is in the background. The trail is the path of that ride, so the app can tell you pace, distance, and whether you are ahead of or behind your goal. Location is not collected in the background when you are not tracking a ride."
                    },
                    allow,
                    decline
                }
            }
        };
    }

    private void DeclineLocation()
    {
        Content = _ride;
        StopTracking();
        _locationOff = true;
        _useLocation.IsVisible = true;
        _state.Text = LocationOffMessage;
        StartClock();
    }

    private async Task BeginTracking()
    {
        Content = _ride;
        _useLocation.IsVisible = false;
        _locationOff = false;
        var granted = await EnsureLocationPermission();
        if (!granted)
        {
            StopTracking();
            _locationOff = true;
            _useLocation.IsVisible = true;
            _state.Text = LocationOffMessage;
            StartClock();
            return;
        }
        if (!_tracking)
        {
            Services.Current.GetRequiredService<TrackingHost>().Start();
            _tracking = true;
        }
        if (Preferences.Get("Simulate", false))
            StartSimulator();
        else
            await Listen();
        StartClock();
        await Refresh();
    }

    private void StartClock()
    {
        if (_timer is not null)
            return;
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += async (_, _) => await Refresh();
        _timer.Start();
    }

    private void StopTracking()
    {
        if (!_tracking)
            return;
        Services.Current.GetRequiredService<TrackingHost>().Stop();
        _tracking = false;
    }

    protected override void OnDisappearing()
    {
        _timer?.Stop();
        _timer = null;
        StopTracking();
        base.OnDisappearing();
    }

    private async Task Refresh()
    {
        var run = Services.Current.GetRequiredService<RunSession>();
        run.RefreshClock();
        _state.Text = _locationOff ? LocationOffMessage : run.Message;
        _time.Text = run.TimeText;
        _timeUnit.Text = run.TimeUnit;
        _distance.Text = run.DistanceValue;
        _distanceUnit.Text = run.DistanceUnit;
        _pace.Text = run.PaceValue;
        _paceUnit.Text = run.PaceUnit;
        _checkpoint.Text = run.CheckpointText;
        _checkpointUnit.Text = run.CheckpointUnit;
        _checkpointRow.IsVisible = !run.Learning;
        _addWaypoint.IsVisible = run.Learning;
        var speech = run.TakeSpeech();
        if (speech.Count > 0)
            await Services.Current.GetRequiredService<SpeechFeedback>().SpeakAsync(speech);
        if (Preferences.Get("KeepAudioHeadphonesAlive", false)
            && Services.Current.GetRequiredService<IBluetoothDevices>().IsAudioDevicePaired())
            await Services.Current.GetRequiredService<IAudioCuePlayer>().PlayAsync(AudioCue.Silence);
    }

    private void StartSimulator()
    {
        var lat = 40.015;
        var lon = -105.27;
        var timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += (_, _) =>
        {
            lat += 0.00008;
            Services.Current.GetRequiredService<RunSession>().OnSample(lat, lon, 5, 4.5, DateTime.UtcNow);
        };
        timer.Start();
    }

    private async Task Listen()
    {
        if (_listening)
            return;
        _listening = true;
        try
        {
            var request = new GeolocationListeningRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(1));
            Geolocation.LocationChanged += (_, args) =>
            {
                var loc = args.Location;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    var detected = Services.Current.GetRequiredService<RunSession>().OnSample(
                        loc.Latitude, loc.Longitude, loc.Accuracy, loc.Speed, loc.Timestamp.UtcDateTime, loc.Course, loc.Altitude);
                    if (detected is not null)
                        StoreLearnedWaypoint(detected);
                });
            };
            await Geolocation.StartListeningForegroundAsync(request);
        }
        catch (Exception ex)
        {
            _state.Text = ex.Message;
        }
    }

    private static async Task<bool> EnsureLocationPermission()
    {
        var when = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        if (when != PermissionStatus.Granted)
            return false;
        await Permissions.RequestAsync<Permissions.LocationAlways>();
        return true;
    }

    private async Task SaveTrace(RunSession run)
    {
        var session = Services.Current.GetRequiredService<AppSession>();
        var store = Services.Current.GetRequiredService<TrainerRepository>();
        if (session.Race is null)
            return;
        var points = run.RecordedTrack.ToList();
        if (points.Count == 0 && run.Latitude is not null && run.Longitude is not null)
        {
            points.Add(new TrackPoint
            {
                Latitude = run.Latitude.Value,
                Longitude = run.Longitude.Value,
                TimestampTicks = SoapDateTime.ToUnixTicks(DateTime.UtcNow)
            });
        }
        if (points.Count == 0)
            return;
        session.Race.ElapsedDistanceKm = run.ElapsedKilometers;
        session.Race.ElapsedTimeTicks = run.Elapsed.Ticks;
        session.Race.FinishTime = DateTime.UtcNow;
        session.Race.StartPositionTicks = points[0].TimestampTicks;
        store.Database.UpsertRace(session.Race);
        store.Database.ReplacePositions(session.Race.RaceId, points);
        store.Database.RecordAction(ActionType.ModifyRace, session.Race.RaceId);
        try
        {
            await Services.Current.GetRequiredService<TrainerApi>().UploadPositionsAsync(session.Race.RaceId, points);
        }
        catch (Exception)
        {
            // The phone copy is the ghost. Sync uploads it when the network is back.
        }
    }

    private static void StoreLearnedWaypoint(Waypoint waypoint)
    {
        var session = Services.Current.GetRequiredService<AppSession>();
        if (session.Course is null)
            return;
        waypoint.CourseId = session.Course.CourseId;
        var store = Services.Current.GetRequiredService<TrainerRepository>();
        try
        {
            store.Database.InsertWaypoint(waypoint);
            store.Database.RecordAction(ActionType.ModifyCourse, session.Course.CourseId);
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            // A sample that lands on an existing timestamp is already stored.
        }
    }
}

public class MapCoursePage : ContentPage
{
    private static readonly WaypointType[] MarkerTypes =
    [
        WaypointType.Start, WaypointType.Finish, WaypointType.Continue,
        WaypointType.TurnLeft, WaypointType.TurnRight, WaypointType.TurnAround,
        WaypointType.BearLeft, WaypointType.BearRight, WaypointType.Summit,
        WaypointType.FeedZone, WaypointType.Announcement
    ];

    private readonly Course _course;
    private readonly string _activity;
    private readonly CourseSketch _sketch = new();
    private readonly LeafletMap _map = new() { MinimumHeightRequest = 320, VerticalOptions = LayoutOptions.Fill };
    private readonly Switch _follow = new();
    private readonly Picker _type = new() { Title = "Waypoint" };
    private readonly Label _notice = new();
    private readonly Button _undo = new() { Text = "Undo last segment" };
    private bool _busy;

    public MapCoursePage(Course course)
    {
        _course = course;
        Title = "Map mode";
        var codes = Services.Current.GetRequiredService<TrainerRepository>().Database.ListActivityCodes();
        _activity = codes.FirstOrDefault(code => code.ActKey == course.ActivityCode)?.ActValue ?? "";
        _follow.IsToggled = RouteModePolicy.FollowsRoads(_activity);
        _sketch.Replace(Services.Current.GetRequiredService<TrainerRepository>().Database.ListWaypoints(course.CourseId).ToList());
        foreach (var type in MarkerTypes)
            _type.Items.Add(type.ToString());
        _type.SelectedIndex = MarkerTypes.ToList().IndexOf(WaypointType.Continue);
        _map.WaypointTapped += (_, tap) => _ = OnTap(tap.Latitude, tap.Longitude);
        _undo.Clicked += (_, _) => Undo();

        _map.HeightRequest = 280;
        _map.HorizontalOptions = LayoutOptions.Fill;
        _undo.HorizontalOptions = LayoutOptions.Fill;
        Content = new ScrollView
        {
            Content = Ui.Column(
                Ui.Heading("Map mode"),
                new Label { Text = "Tap the map to add a waypoint. The line from the previous point follows roads when that switch is on." },
                Ui.SwitchRow("Follow roads", null, _follow),
                _type,
                _notice,
                _map,
                _undo)
        };
        Redraw();
    }

    private async Task OnTap(double latitude, double longitude)
    {
        if (_busy)
            return;
        _busy = true;
        try
        {
            var type = _sketch.Waypoints.Count == 0
                ? WaypointType.Start
                : MarkerTypes[Math.Clamp(_type.SelectedIndex, 0, MarkerTypes.Length - 1)];
            var routing = CourseMapTap.TryCreateRouteRequest(
                _sketch, latitude, longitude, _follow.IsToggled, _activity, type, out var request, out var blocked);
            if (blocked is not null)
            {
                _notice.Text = blocked;
                return;
            }

            RouteResponseDto? route = null;
            if (routing && request is not null)
                route = await FetchRoute(request);
            _sketch.AddClick(latitude, longitude, type, followRoads: routing, route);
            _notice.Text = _sketch.Notice ?? $"{_sketch.Waypoints.Count} waypoints";
            Persist();
            Redraw();
        }
        catch (Exception ex)
        {
            _notice.Text = ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task<RouteResponseDto> FetchRoute(RouteRequestDto request)
    {
        var session = Services.Current.GetRequiredService<AppSession>();
        if (string.IsNullOrEmpty(session.Token))
            return RouteResponseDto.Fail("Sign in to follow roads.");
        try
        {
            return await Services.Current.GetRequiredService<TrainerApi>().RouteAsync(request);
        }
        catch (Exception ex)
        {
            return RouteResponseDto.Fail(ex.Message);
        }
    }

    private void Undo()
    {
        if (!_sketch.Undo())
        {
            _notice.Text = "Nothing to undo.";
            return;
        }

        _notice.Text = "Removed the last segment.";
        Persist();
        Redraw();
    }

    private void Persist()
    {
        var store = Services.Current.GetRequiredService<TrainerRepository>();
        var distance = CourseMapTap.StampElapsed(_sketch.Waypoints);
        foreach (var wp in _sketch.Waypoints)
            wp.CourseId = _course.CourseId;
        store.Database.ReplaceWaypoints(_course.CourseId, _sketch.Waypoints);
        _course.ElapsedDistanceKm = distance;
        _course.Timestamp = DateTime.UtcNow;
        _course.WaypointTimestamp = DateTime.UtcNow;
        store.Database.UpsertCourse(_course);
        store.Database.RecordAction(ActionType.ModifyCourse, _course.CourseId);
        Services.Current.GetRequiredService<AppSession>().Course = _course;
    }

    private void Redraw()
    {
        var last = _sketch.Waypoints.LastOrDefault();
        var pins = _sketch.Waypoints.Select(wp => (
            wp.Latitude,
            wp.Longitude,
            wp.WaypointType == WaypointType.Continue && !ReferenceEquals(wp, last) ? "" : wp.WaypointType.ToString()));
        var user = Services.Current.GetRequiredService<AppSession>().User;
        var opening = MapDefaults.OpeningCenter(user?.Latitude, user?.Longitude);
        _map.Show(
            pins,
            acceptTaps: true,
            fit: _sketch.Waypoints.Count > 1,
            centerLatitude: opening.Latitude,
            centerLongitude: opening.Longitude);
        _undo.IsEnabled = _sketch.CanUndo;
    }
}

public class InsertWaypointPage : ContentPage
{
    private readonly Picker _type = new() { Title = "Waypoint" };
    private readonly Entry _description = Ui.Box("Description");

    public InsertWaypointPage()
    {
        Title = "Waypoint";
        foreach (var type in Enum.GetValues<WaypointType>())
            _type.Items.Add(type.ToString());
        _type.SelectedIndex = (int)WaypointType.TurnLeft;
        Content = new ScrollView
        {
            Content = Ui.Column(
                Ui.Heading("Waypoint"),
                Ui.Labeled("Waypoint", _type),
                Ui.Labeled("Description", _description),
                Ui.Command(AppStrings.confirm, Save))
        };
    }

    private async Task Save()
    {
        var session = Services.Current.GetRequiredService<AppSession>();
        var run = Services.Current.GetRequiredService<RunSession>();
        if (session.Course is null || run.Latitude is null || run.Longitude is null)
        {
            await DisplayAlertAsync(AppStrings.warning_title, AppStrings.WaitingForGpsData, AppStrings.confirm);
            return;
        }
        var type = Enum.GetValues<WaypointType>()[Math.Max(0, _type.SelectedIndex)];
        var store = Services.Current.GetRequiredService<TrainerRepository>();
        store.Database.InsertWaypoint(new Waypoint
        {
            CourseId = session.Course.CourseId,
            Latitude = run.Latitude.Value,
            Longitude = run.Longitude.Value,
            WaypointType = type,
            TextDescription = _description.Text,
            ElapsedDistanceKm = run.ElapsedKilometers,
            AltitudeKm = Altitude.Unknown.Kilometers,
            TimestampTicks = SoapDateTime.ToUnixTicks(DateTime.UtcNow)
        });
        store.Database.RecordAction(ActionType.ModifyCourse, session.Course.CourseId);
        run.UseCourse(session.Course.CourseId, store.Database.ListWaypoints(session.Course.CourseId));
        await Navigation.PopAsync();
    }
}

public class PreferencesPage : ContentPage
{
    public PreferencesPage()
    {
        Title = AppStrings.menu_options;
        var layout = Ui.Column(Ui.Heading(AppStrings.menu_options));
        var metric = new Switch
        {
            IsToggled = SportUnits.IsMetric(Preferences.Get("DefaultSport", "Cycling"), key => Preferences.Get(key, ""))
        };
        metric.Toggled += (_, _) =>
        {
            var value = metric.IsToggled ? SportUnits.Metric : SportUnits.English;
            foreach (var sport in PreferenceCatalog.Sports)
                Preferences.Set(sport + "Units", value);
        };
        layout.Add(Ui.SwitchRow("Metric units (kilometers and kph).", "Off uses miles and mph.", metric));
        foreach (var pref in PreferenceCatalog.Core())
        {
            if (pref.DefaultValue is "true" or "false")
            {
                var key = pref.Key;
                var toggle = new Switch
                {
                    IsToggled = Preferences.Get(key, pref.DefaultValue == "true")
                };
                toggle.Toggled += (_, _) => Preferences.Set(key, toggle.IsToggled);
                layout.Add(Ui.SwitchRow(pref.Summary, null, toggle));
            }
            else
            {
                AddPreference(layout, pref.Key, pref.DefaultValue);
            }
        }
        layout.Add(Ui.Heading("Audio prompts"));
        foreach (var pref in PreferenceCatalog.AudioPrompts())
            AddPreference(layout, pref.Key, pref.DefaultValue);
        layout.Add(Ui.Heading("Waypoint prompts"));
        foreach (var pref in PreferenceCatalog.WaypointPrompts())
            AddPreference(layout, pref.Key, pref.DefaultValue);
        var api = Ui.Box("API base URL");
        api.Text = ApiAddress.Current;
        api.Unfocused += (_, _) => Preferences.Set(ApiAddress.PreferenceKey, api.Text ?? "");
        layout.Add(Ui.Labeled("API base URL", api));
        layout.Add(Ui.Heading("Delete account"));
        layout.Add(new Label
        {
            Text = "This deletes the account and the courses, waypoints, rides, and location trails stored with it. This cannot be undone."
        });
        var confirm = Ui.Box("Password or DELETE");
        confirm.Keyboard = Keyboard.Create(KeyboardFlags.None);
        confirm.IsSpellCheckEnabled = false;
        confirm.IsTextPredictionEnabled = false;
        confirm.IsVisible = false;
        var hint = new Label { Text = "Type your password or DELETE.", IsVisible = false };
        var submit = Ui.Command("Delete account", () => DeleteAccount(confirm));
        submit.IsVisible = false;
        var cancel = new Button { Text = AppStrings.cancel, IsVisible = false };
        var open = new Button { Text = "Delete account" };
        open.Clicked += (_, _) =>
        {
            open.IsVisible = false;
            hint.IsVisible = true;
            confirm.IsVisible = true;
            submit.IsVisible = true;
            cancel.IsVisible = true;
        };
        cancel.Clicked += (_, _) =>
        {
            confirm.Text = "";
            open.IsVisible = true;
            hint.IsVisible = false;
            confirm.IsVisible = false;
            submit.IsVisible = false;
            cancel.IsVisible = false;
        };
        layout.Add(open);
        layout.Add(hint);
        layout.Add(confirm);
        layout.Add(submit);
        layout.Add(cancel);
        Content = new ScrollView { Content = layout };
    }

    private async Task DeleteAccount(Entry confirm)
    {
        var confirmation = confirm.Text ?? "";
        if (!AccountDeletion.ShouldCallServer(confirmation))
        {
            await DisplayAlertAsync("Delete account", AccountDeletion.Message(AccountDeletion.Result.Rejected), AppStrings.confirm);
            return;
        }

        var session = Services.Current.GetRequiredService<AppSession>();
        var login = session.User?.Login ?? "";
        if (login.Length == 0)
        {
            await DisplayAlertAsync("Delete account", AccountDeletion.Message(AccountDeletion.Result.Unauthorized), AppStrings.confirm);
            return;
        }

        AccountDeletion.Result result;
        try
        {
            result = await Services.Current.GetRequiredService<TrainerApi>().DeleteAccountAsync(login, confirmation);
        }
        catch (Exception)
        {
            result = AccountDeletion.Result.Failed;
        }

        if (!AccountDeletion.ClearsLocalData(result))
        {
            await DisplayAlertAsync("Delete account", AccountDeletion.Message(result), AppStrings.confirm);
            return;
        }

        PhoneSignOut.Apply(session);
        if (Application.Current?.Windows.Count > 0)
            Application.Current.Windows[0].Page = new NavigationPage(new LoginPage());
    }

    private static void AddPreference(VerticalStackLayout layout, string key, string fallback)
    {
        var entry = Ui.Box(key);
        entry.Text = Preferences.Get(key, fallback);
        entry.Unfocused += (_, _) => Preferences.Set(key, string.IsNullOrWhiteSpace(entry.Text) ? fallback : entry.Text.Trim());
        layout.Add(Ui.Labeled(key, entry));
    }
}

public class GpsInfoPage : ContentPage
{
    private readonly IGpsInfoFeed _feed;
    private readonly Label _message = new() { LineBreakMode = LineBreakMode.WordWrap };
    private readonly VerticalStackLayout _rows = new() { Spacing = 2 };
    private readonly Button _settings = new() { Text = GpsInfoText.OpenSettings, IsVisible = false };

    public GpsInfoPage()
    {
        Title = AppStrings.menu_gps_info;
        _feed = Services.Current.GetRequiredService<IGpsInfoFeed>();
        _settings.Clicked += (_, _) => _feed.OpenSettings();
        Content = new ScrollView
        {
            Content = Ui.Column(Ui.Heading(AppStrings.menu_gps_info), _message, _rows, _settings)
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _feed.Updated += OnUpdate;
        _ = _feed.StartAsync();
    }

    protected override void OnDisappearing()
    {
        _feed.Updated -= OnUpdate;
        _feed.Stop();
        base.OnDisappearing();
    }

    private void OnUpdate(object? sender, GpsInfoUpdate update)
    {
        MainThread.BeginInvokeOnMainThread(() => Show(update));
    }

    private void Show(GpsInfoUpdate update)
    {
        var metric = SportUnits.IsMetric(Preferences.Get("DefaultSport", "Cycling"), key => Preferences.Get(key, ""));
        var view = GpsInfoText.Present(update.Blocker, update.Sample, metric);
        _message.Text = view.Message;
        _message.IsVisible = view.Message.Length > 0;
        _settings.IsVisible = view.ShowSettings;
        _rows.Clear();
        foreach (var row in view.Rows)
            _rows.Add(Ui.FactRow(row.Label, row.Value));
    }
}

public class DataPage : ContentPage
{
    private readonly Label _status = new();

    public DataPage()
    {
        Title = AppStrings.menu_data;
        Content = new ScrollView
        {
            Content = Ui.Column(
                Ui.Heading(AppStrings.menu_data),
                Ui.Command(AppStrings.data_button_sync, Sync),
                Ui.Command(AppStrings.data_button_compact, Compact),
                _status)
        };
    }

    private async Task Sync()
    {
        _status.Text = AppStrings.UploadingData;
        _status.Text = await Services.Current.GetRequiredService<SyncService>().SynchronizeAsync();
    }

    private Task Compact()
    {
        _status.Text = "SQLite vacuum is available from the database file on device. The old CompactDatabaseService is not reimplemented as a separate Android service.";
        return Task.CompletedTask;
    }
}

public class AboutPage : ContentPage
{
    private int _taps;

    public AboutPage()
    {
        Title = AppStrings.menu_about;
        var icon = new Image { Source = "gpstrainer.png", HeightRequest = 96, WidthRequest = 96 };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            _taps++;
            if (_taps >= 3)
            {
                var debug = !Preferences.Get("Debug", false);
                Preferences.Set("Debug", debug);
                _taps = 0;
                DisplayAlertAsync(AppStrings.debug, debug ? AppStrings.debug : "", AppStrings.confirm);
            }
        };
        icon.GestureRecognizers.Add(tap);
        icon.HorizontalOptions = LayoutOptions.Center;
        Content = new ScrollView
        {
            Content = Ui.Column(icon, Ui.Heading(AppStrings.app_name),
                new Label { Text = AppStrings.rev + " 1.46" },
                new Label { Text = AppStrings.build + " 20131013" },
                new Label { Text = AppStrings.peak_software, LineBreakMode = LineBreakMode.WordWrap })
        };
    }
}

public class LeafletMap : WebView
{
    public event EventHandler<(double Latitude, double Longitude)>? WaypointTapped;

    public LeafletMap()
    {
        HeightRequest = 240;
        Navigating += OnNavigating;
    }

    public void Show(
        IEnumerable<(double Latitude, double Longitude, string Label)> pins,
        bool acceptTaps = false,
        bool fit = false,
        double? centerLatitude = null,
        double? centerLongitude = null)
    {
        var list = pins.ToList();
        var centerLat = list.Count > 0 ? list[0].Latitude : centerLatitude ?? 40.015;
        var centerLon = list.Count > 0 ? list[0].Longitude : centerLongitude ?? -105.27;
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var line = string.Join(",", list.Select(p =>
            $"[{p.Latitude.ToString(culture)},{p.Longitude.ToString(culture)}]"));
        var markers = string.Join("\n", list.Where(p => !string.IsNullOrEmpty(p.Label)).Select(p =>
            $"L.marker([{p.Latitude.ToString(culture)}, {p.Longitude.ToString(culture)}]).addTo(map).bindPopup('{Escape(p.Label)}');"));
        var tap = acceptTaps
            ? "map.on('click', function(ev){ window.location.href = 'gpstrainer://waypoint?lat=' + ev.latlng.lat + '&lng=' + ev.latlng.lng; });"
            : "";
        var frame = fit && list.Count > 1
            ? "map.fitBounds([" + line + "], {padding:[16,16]});"
            : $"map.setView([{centerLat.ToString(culture)}, {centerLon.ToString(culture)}], 14);";
        var attribution = Escape(MapDefaults.Attribution);
        Source = new HtmlWebViewSource
        {
            Html = $$"""
                <!DOCTYPE html><html><head>
                <meta name="viewport" content="width=device-width, initial-scale=1" />
                <link rel="stylesheet" href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css" />
                <script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"></script>
                <style>html,body,#map{height:100%;margin:0}</style></head>
                <body><div id="map"></div>
                <script>
                var map = L.map('map');
                L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', { maxZoom: 19, attribution: '{{attribution}}' }).addTo(map);
                var line = [{{line}}];
                if (line.length > 1) L.polyline(line, {color:'#1d4ed8', weight:4}).addTo(map);
                {{markers}}
                {{frame}}
                {{tap}}
                </script></body></html>
                """
        };
    }

    private void OnNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (!MapTapUrl.TryParse(e.Url, out var latitude, out var longitude))
            return;
        e.Cancel = true;
        WaypointTapped?.Invoke(this, (latitude, longitude));
    }

    private static string Escape(string value) => value.Replace("'", "").Replace("&", "&amp;");
}
