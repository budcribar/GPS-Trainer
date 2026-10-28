using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace GpsTrainer.Web.Services;

public sealed class Session
{
    public string? Token { get; private set; }
    public string? Login { get; private set; }
    public UserProfileDto? User { get; private set; }
    public event Action? Changed;

    public void Set(LoginResponse response)
    {
        Token = response.Token;
        Login = response.User.Login;
        User = response.User;
        Changed?.Invoke();
    }

    public void Clear()
    {
        Token = null;
        Login = null;
        User = null;
        Changed?.Invoke();
    }

    public void UpdateUser(UserProfileDto user)
    {
        User = user;
        Login = user.Login;
        Changed?.Invoke();
    }

    public void Apply(SessionSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.Token) || string.IsNullOrWhiteSpace(snapshot.Login))
            return;
        Token = snapshot.Token;
        Login = snapshot.Login;
        User = snapshot.User;
        Changed?.Invoke();
    }

    public SessionSnapshot Capture() => new()
    {
        Token = Token ?? "",
        Login = Login ?? "",
        User = User
    };
}

public sealed class GpsApi
{
    private readonly HttpClient _http;
    private readonly Session _session;

    public GpsApi(HttpClient http, Session session)
    {
        _http = http;
        _session = session;
    }

    public async Task<LoginResponse?> LoginAsync(string login, string password)
    {
        var response = await _http.PostAsJsonAsync("api/auth/login", new LoginRequest { Login = login, Password = password });
        if (!response.IsSuccessStatusCode)
            return null;
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        if (body is not null)
            _session.Set(body);
        return body;
    }

    public async Task<bool> DeleteAccountAsync(string confirmation)
    {
        if (string.IsNullOrEmpty(_session.Login))
            return false;
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/users/{Uri.EscapeDataString(_session.Login)}")
        {
            Content = JsonContent.Create(new AccountDeletionRequest { Confirmation = confirmation })
        };
        Authorize(request);
        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return false;
        await LogoutAsync();
        return true;
    }

    public async Task LogoutAsync()
    {
        if (!string.IsNullOrEmpty(_session.Token))
        {
            try
            {
                await SendAsync(HttpMethod.Post, "api/auth/logout", new { });
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }
        }
        _session.Clear();
    }

    public async Task<string> SignUpAsync(UserWriteDto user)
    {
        var response = await _http.PostAsJsonAsync("api/users", user);
        var body = await response.Content.ReadFromJsonAsync<InsertUserResponse>();
        return body?.Result ?? "error";
    }

    public async Task<IReadOnlyList<Course>> CoursesAsync()
    {
        if (_session.Login is null)
            return [];
        return await GetAsync<List<Course>>($"api/courses?login={Uri.EscapeDataString(_session.Login)}") ?? [];
    }

    public async Task<Course?> SaveCourseAsync(Course course, bool create = false)
    {
        course.Login = _session.Login ?? course.Login;
        var response = create || course.CourseId == Guid.Empty
            ? await SendAsync(HttpMethod.Post, "api/courses", course)
            : await SendAsync(HttpMethod.Put, $"api/courses/{course.CourseId}", course);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<Course>();
    }

    public async Task<bool> DeleteCourseAsync(Guid courseId)
    {
        if (string.IsNullOrEmpty(_session.Login) || courseId == Guid.Empty)
            return false;
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"api/courses/{courseId}?login={Uri.EscapeDataString(_session.Login)}");
        Authorize(request);
        var response = await _http.SendAsync(request);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> SaveWaypointsAsync(Guid courseId, IReadOnlyList<Waypoint> waypoints)
    {
        var response = await SendAsync(HttpMethod.Put, $"api/courses/{courseId}/waypoints", waypoints);
        return response.IsSuccessStatusCode;
    }

    public async Task<Course?> CourseAsync(Guid courseId) =>
        await GetAsync<Course>($"api/courses/{courseId}");

    public async Task<IReadOnlyList<Waypoint>> WaypointsAsync(Guid courseId) =>
        await GetAsync<List<Waypoint>>($"api/courses/{courseId}/waypoints") ?? [];

    public async Task<IReadOnlyList<Race>> RacesAsync()
    {
        if (_session.Login is null)
            return [];
        return await GetAsync<List<Race>>($"api/races?login={Uri.EscapeDataString(_session.Login)}") ?? [];
    }

    public async Task<IReadOnlyList<ActivityCode>> ActivitiesAsync() =>
        await GetAsync<List<ActivityCode>>("api/activity-codes") ?? [];

    public async Task<bool> ChangePasswordAsync(PasswordChangeRequest body)
    {
        var response = await SendAsync(HttpMethod.Post, "api/auth/password", body);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> ForgotAsync(ForgotPasswordRequest body)
    {
        var response = await _http.PostAsJsonAsync("api/auth/forgot", body);
        return response.IsSuccessStatusCode;
    }

    public async Task<string?> ExportGpxAsync(Guid courseId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/courses/{courseId}/export/gpx");
        Authorize(request);
        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<GpxImportResult?> PreviewGpxAsync(string xml, string? fileName, double? toleranceMeters = null)
    {
        var address = "api/courses/import/gpx?fileName=" + Uri.EscapeDataString(fileName ?? "");
        if (toleranceMeters is not null)
            address += "&toleranceMeters=" + toleranceMeters.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var request = new HttpRequestMessage(HttpMethod.Post, address)
        {
            Content = new StringContent(xml, System.Text.Encoding.UTF8, "application/gpx+xml")
        };
        Authorize(request);
        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<GpxImportResult>();
    }

    public async Task<bool> ImportGpxAsync(Guid courseId, string xml)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/courses/{courseId}/import/gpx")
        {
            Content = new StringContent(xml, System.Text.Encoding.UTF8, "application/gpx+xml")
        };
        Authorize(request);
        var response = await _http.SendAsync(request);
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<CourseIntervalDto>> IntervalsAsync(Guid courseId) =>
        await GetAsync<List<CourseIntervalDto>>($"api/courses/{courseId}/intervals") ?? [];

    public async Task<IReadOnlyList<IntervalResultDto>> IntervalResultsAsync(Guid courseId) =>
        await GetAsync<List<IntervalResultDto>>($"api/courses/{courseId}/interval-results") ?? [];

    public async Task<ValidationResultDto?> ValidateAsync(Guid courseId)
    {
        var response = await SendAsync(HttpMethod.Post, $"api/courses/{courseId}/validate", new { });
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<ValidationResultDto>();
    }

    public async Task<IReadOnlyList<Course>> PublishedAsync(double latitude, double longitude, double radiusKm) =>
        await GetAsync<List<Course>>($"api/courses/published?latitude={latitude}&longitude={longitude}&radiusKm={radiusKm}") ?? [];

    public async Task<IReadOnlyList<RacePromotionDto>> PromotionsAsync() =>
        await GetAsync<List<RacePromotionDto>>("api/promotions") ?? [];

    public async Task<RacePromotionDto?> PromoteAsync(RacePromotionDto body)
    {
        var response = await SendAsync(HttpMethod.Post, "api/promotions", body);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<RacePromotionDto>();
    }

    public async Task<bool> RegisterAsync(Guid promotionId)
    {
        var response = await SendAsync(HttpMethod.Post, $"api/promotions/{promotionId}/register", new { });
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<UserProfileDto>> AdminUsersAsync() =>
        await GetAsync<List<UserProfileDto>>("api/admin/users") ?? [];

    public async Task<UserProfileDto?> ProfileAsync()
    {
        if (_session.Login is null)
            return null;
        return await GetAsync<UserProfileDto>($"api/users/{Uri.EscapeDataString(_session.Login)}");
    }

    public async Task<UserProfileDto?> SaveLocationAsync(double latitude, double longitude)
    {
        if (_session.Login is null)
            return null;
        var response = await SendAsync(HttpMethod.Put, $"api/users/{Uri.EscapeDataString(_session.Login)}/location", new UserLocationDto
        {
            Latitude = latitude,
            Longitude = longitude
        });
        if (!response.IsSuccessStatusCode)
            return null;
        var profile = await response.Content.ReadFromJsonAsync<UserProfileDto>();
        if (profile is not null)
            _session.UpdateUser(profile);
        return profile;
    }

    public async Task<LoopGenerateResponseDto> GenerateLoopsAsync(LoopGenerateRequestDto body)
    {
        try
        {
            var response = await SendAsync(HttpMethod.Post, "api/routing/loops", body);
            if (!response.IsSuccessStatusCode)
            {
                var failed = await response.Content.ReadFromJsonAsync<LoopGenerateResponseDto>();
                return failed ?? new LoopGenerateResponseDto { Message = "No loop was generated." };
            }
            return await response.Content.ReadFromJsonAsync<LoopGenerateResponseDto>()
                ?? new LoopGenerateResponseDto { Message = "No loop was generated." };
        }
        catch (HttpRequestException)
        {
            return new LoopGenerateResponseDto { Message = "No loop was generated." };
        }
        catch (System.Text.Json.JsonException)
        {
            return new LoopGenerateResponseDto { Message = "No loop was generated." };
        }
    }

    public async Task<RouteResponseDto> RouteAsync(RouteRequestDto body)
    {
        var response = await SendAsync(HttpMethod.Post, "api/routing/directions", body);
        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            var limited = await ReadJson<RouteResponseDto>(response);
            return limited ?? RouteResponseDto.Fail("Too many road requests from this network. Wait a minute and try again.", straightFallback: false);
        }
        if (!response.IsSuccessStatusCode)
            return RouteResponseDto.Fail("Routing failed.");
        return await response.Content.ReadFromJsonAsync<RouteResponseDto>()
            ?? RouteResponseDto.Fail("Routing failed.");
    }

    public async Task<ElevationLineResponseDto> ElevationLineAsync(ElevationLineRequestDto body)
    {
        var response = await SendAsync(HttpMethod.Post, "api/routing/elevation", body);
        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            var limited = await ReadJson<ElevationLineResponseDto>(response);
            return limited ?? new ElevationLineResponseDto { Message = "Too many road requests from this network. Wait a minute and try again." };
        }
        if (!response.IsSuccessStatusCode)
            return new ElevationLineResponseDto { Message = "Elevation lookup failed." };
        try
        {
            return await response.Content.ReadFromJsonAsync<ElevationLineResponseDto>()
                ?? new ElevationLineResponseDto { Message = "Elevation lookup failed." };
        }
        catch (System.Text.Json.JsonException)
        {
            return new ElevationLineResponseDto { Message = "Elevation lookup failed." };
        }
    }

    public async Task<Guid> GenerateRaceAsync(Guid courseId, string goalTime)
    {
        if (_session.Login is null || courseId == Guid.Empty)
            return Guid.Empty;
        var response = await SendAsync(HttpMethod.Post, "api/races/generate", new GenerateRaceRequest
        {
            CourseId = courseId,
            Login = _session.Login,
            GoalTime = string.IsNullOrWhiteSpace(goalTime) ? "00:00:00" : goalTime.Trim()
        });
        if (!response.IsSuccessStatusCode)
            return Guid.Empty;
        try
        {
            return await response.Content.ReadFromJsonAsync<Guid>();
        }
        catch (System.Text.Json.JsonException)
        {
            return Guid.Empty;
        }
    }

    public async Task<GeocodeResponseDto> GeocodeAsync(string address)
    {
        if (_session.Token is null)
            return new GeocodeResponseDto { Message = "Sign in to search for an address." };
        var response = await SendAsync(HttpMethod.Post, "api/routing/geocode", new GeocodeRequestDto { Address = address });
        if (!response.IsSuccessStatusCode)
            return new GeocodeResponseDto { Message = "Geocoding failed." };
        return await response.Content.ReadFromJsonAsync<GeocodeResponseDto>()
            ?? new GeocodeResponseDto { Message = "Geocoding failed." };
    }

    public async Task<SiteInfoDto?> SiteAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/site");
        Authorize(request);
        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return null;
        return await ReadJson<SiteInfoDto>(response);
    }

    public async Task<RoutingSettingsDto?> RoutingSettingsAsync() =>
        await GetAsync<RoutingSettingsDto>("api/admin/routing");

    public async Task<RoutingSettingsDto?> SaveRoutingSettingsAsync(RoutingSettingsWriteDto body)
    {
        var response = await SendAsync(HttpMethod.Put, "api/admin/routing", body);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<RoutingSettingsDto>();
    }

    private async Task<T?> GetAsync<T>(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        Authorize(request);
        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return default;
        return await response.Content.ReadFromJsonAsync<T>();
    }

    private static async Task<T?> ReadJson<T>(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>();
        }
        catch (System.Text.Json.JsonException)
        {
            return default;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object body)
    {
        using var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body) };
        Authorize(request);
        return await _http.SendAsync(request);
    }

    private void Authorize(HttpRequestMessage request)
    {
        if (!string.IsNullOrEmpty(_session.Token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.Token);
    }
}
