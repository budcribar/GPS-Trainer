namespace GpsTrainer;

public sealed class LoginRequest
{
    public string Login { get; set; } = "";
    public string Password { get; set; } = "";
}

public sealed class LoginResponse
{
    public string Token { get; set; } = "";
    public UserProfileDto User { get; set; } = new();
}

public sealed class InsertUserResponse
{
    public string Result { get; set; } = "";
}

public sealed class UserProfileDto
{
    public string Login { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Country { get; set; } = "US";
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTime BirthDate { get; set; }
    public bool IsMale { get; set; } = true;
    public int RacingCategoryKey { get; set; }
    public bool IsActive { get; set; }
    public string PhoneNumber { get; set; } = "";
    public bool WantsEmails { get; set; }
    public DateTime ExpirationDate { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

public sealed class UserWriteDto
{
    public string Login { get; set; } = "";
    public string Password { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Country { get; set; } = "US";
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTime BirthDate { get; set; }
    public bool IsMale { get; set; } = true;
    public int RacingCategoryKey { get; set; }
    public string PhoneNumber { get; set; } = "";
    public bool WantsEmails { get; set; } = true;
    public DateTime? ExpirationDate { get; set; }
}

public sealed class PushRegistrationDto
{
    public string Token { get; set; } = "";
}

public sealed class GenerateRaceRequest
{
    public Guid CourseId { get; set; }
    public string Login { get; set; } = "";
    public string GoalTime { get; set; } = "00:00:00";
}

public sealed class PasswordChangeRequest
{
    public string CurrentPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
}

public sealed class AccountDeletionRequest
{
    public string Confirmation { get; set; } = "";
}

public sealed class ForgotPasswordRequest
{
    public string Login { get; set; } = "";
    public string Email { get; set; } = "";
}

public sealed class ResetPasswordRequest
{
    public string Login { get; set; } = "";
    public string Token { get; set; } = "";
    public string NewPassword { get; set; } = "";
}

public sealed class ForgotPasswordResponse
{
    public bool Accepted { get; set; } = true;
    public string? ResetToken { get; set; }
}

public sealed class CourseIntervalDto
{
    public Guid IntervalId { get; set; }
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public double LengthKm { get; set; }
    public double StartElapsedKm { get; set; }
    public double FinishElapsedKm { get; set; }
    public long StartTimestampTicks { get; set; }
    public long FinishTimestampTicks { get; set; }
    public double StartLatitude { get; set; }
    public double StartLongitude { get; set; }
    public double FinishLatitude { get; set; }
    public double FinishLongitude { get; set; }
}

public sealed class PlaceIntervalRequest
{
    public string Name { get; set; } = "";
    public double StartElapsedKm { get; set; }
    public double FinishElapsedKm { get; set; }
}

public sealed class MoveIntervalDistanceRequest
{
    public double StartElapsedKm { get; set; }
    public double FinishElapsedKm { get; set; }
}

public sealed class IntervalResultDto
{
    public Guid RaceId { get; set; }
    public string Login { get; set; } = "";
    public DateTime Date { get; set; }
    public List<string> Splits { get; set; } = [];
    public string Total { get; set; } = "";
}

public sealed class RacePromotionDto
{
    public Guid RacePromotionId { get; set; }
    public Guid CourseId { get; set; }
    public DateTime RaceDateStart { get; set; }
    public DateTime RaceDateFinish { get; set; }
    public decimal EntryFee { get; set; }
    public string Login { get; set; } = "";
}

public sealed class RaceRegistrationDto
{
    public Guid RacePromotionId { get; set; }
    public string Login { get; set; } = "";
}

public sealed class SiteInfoDto
{
    public string Name { get; set; } = "gpsTrainer";
    public string Domain { get; set; } = SiteIdentity.Domain;
    public string Url { get; set; } = SiteIdentity.PublicUrl;
    public string SupportEmail { get; set; } = SiteIdentity.SupportEmail;
    public string GooglePlayUrl { get; set; } = "";
    public string AppStoreUrl { get; set; } = "";
}

public sealed class MoveIntervalRequest
{
    public long PreviousStartTicks { get; set; }
    public long PreviousFinishTicks { get; set; }
    public Waypoint NewStart { get; set; } = new();
    public Waypoint NewFinish { get; set; } = new();
}

public sealed class ValidationResultDto
{
    public bool Ok { get; set; }
    public string Message { get; set; } = "";
}

public static class UserMapping
{
    public static UserProfileDto ToProfile(UserAccount user) => new()
    {
        Login = user.Login,
        FirstName = user.FirstName,
        LastName = user.LastName,
        Country = user.Country,
        Street = user.Street,
        City = user.City,
        State = user.State,
        Email = user.Email,
        BirthDate = user.BirthDate,
        IsMale = user.IsMale,
        RacingCategoryKey = user.RacingCategoryKey,
        IsActive = user.IsActive,
        PhoneNumber = user.PhoneNumber,
        WantsEmails = user.WantsEmails,
        ExpirationDate = user.ExpirationDate,
        Latitude = user.Latitude,
        Longitude = user.Longitude
    };
}
