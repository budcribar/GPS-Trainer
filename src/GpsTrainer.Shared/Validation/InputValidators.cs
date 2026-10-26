using System.Text.RegularExpressions;

namespace GpsTrainer;

public static class EmailValidator
{
    public const string Pattern = @"^[_A-Za-z0-9-]+(\.[_A-Za-z0-9-]+)*@[A-Za-z0-9]+(\.[A-Za-z0-9]+)*(\.[A-Za-z]{2,})$";

    public static bool Validate(string? value) =>
        value is not null && Regex.IsMatch(value, Pattern, RegexOptions.CultureInvariant);
}

public static class PhoneNumberValidator
{
    public const string Pattern = @"^((\(\d{3}\) ?)|(\d{3}-))?\d{3}-\d{4}$";

    public static bool Validate(string? value) =>
        value is not null && Regex.IsMatch(value, Pattern, RegexOptions.CultureInvariant);
}
