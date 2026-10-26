using System.Security.Cryptography;
using System.Text;

namespace GpsTrainer.Server.Security;

public static class PasswordHasher
{
    private const int Iterations = 100_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2")
            return false;
        if (!int.TryParse(parts[1], out var iterations))
            return false;
        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}

public static class AuthTokens
{
    public static string Issue(string login, string signingKey, DateTimeOffset expires)
    {
        // A fresh id keeps a sign-out from cancelling the next login in the same second.
        var payload = $"{login}|{expires.ToUnixTimeSeconds()}|{Guid.NewGuid():N}";
        var sig = Sign(payload, signingKey);
        return $"{Base64Url(payload)}.{Base64Url(sig)}";
    }

    public static bool TryRead(string? token, string signingKey, out string login) =>
        TryRead(token, signingKey, out login, out _, out _);

    /// <summary>
    /// <paramref name="tokenId"/> is the per-login id, or the signature on an older
    /// two-part token. Sign-out stores that id so the same bearer stops working.
    /// </summary>
    public static bool TryRead(string? token, string signingKey, out string login, out long expiresUnix, out string tokenId)
    {
        login = "";
        expiresUnix = 0;
        tokenId = "";
        if (string.IsNullOrWhiteSpace(token))
            return false;
        var parts = token.Split('.');
        if (parts.Length != 2 || parts[1].Length == 0)
            return false;
        string payload;
        byte[] sig;
        try
        {
            payload = Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
            sig = Base64UrlDecode(parts[1]);
        }
        catch (FormatException)
        {
            return false;
        }
        var expected = Sign(payload, signingKey);
        if (!CryptographicOperations.FixedTimeEquals(sig, expected))
            return false;
        var split = payload.Split('|');
        if (split.Length is < 2 or > 3 || !long.TryParse(split[1], out var unix))
            return false;
        if (DateTimeOffset.FromUnixTimeSeconds(unix) < DateTimeOffset.UtcNow)
            return false;
        login = split[0];
        expiresUnix = unix;
        tokenId = split.Length == 3 && split[2].Length > 0 ? split[2] : parts[1];
        return login.Length > 0;
    }

    private static byte[] Sign(string payload, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
    }

    private static string Base64Url(string text) => Base64Url(Encoding.UTF8.GetBytes(text));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }
}
