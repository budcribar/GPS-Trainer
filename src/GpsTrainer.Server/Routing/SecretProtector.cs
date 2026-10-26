using System.Security.Cryptography;
using System.Text;

namespace GpsTrainer.Server.Routing;

/// <summary>
/// AES-GCM blob for an admin-supplied routing key. The ciphertext is what goes in the database.
/// </summary>
public sealed class SecretProtector
{
    private static readonly string Ephemeral = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly byte[] _key;

    public SecretProtector(IConfiguration config)
    {
        var material = First(
            config["Routing:SettingsProtectionKey"],
            Environment.GetEnvironmentVariable("ROUTING_SETTINGS_PROTECTION_KEY"),
            config["GpsTrainer:AuthSigningKey"]);
        // Without a stable protection key, stored keys only decrypt in this process.
        // Environment routing keys still work after a restart.
        if (string.IsNullOrWhiteSpace(material))
            material = Ephemeral;
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(material));
    }

    public string Protect(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plain, cipher, tag);
        var packed = new byte[nonce.Length + tag.Length + cipher.Length];
        nonce.CopyTo(packed, 0);
        tag.CopyTo(packed, nonce.Length);
        cipher.CopyTo(packed, nonce.Length + tag.Length);
        return Convert.ToBase64String(packed);
    }

    public string? Unprotect(string? packed)
    {
        if (string.IsNullOrWhiteSpace(packed))
            return null;
        try
        {
            var bytes = Convert.FromBase64String(packed);
            if (bytes.Length < 28)
                return null;
            var nonce = bytes.AsSpan(0, 12);
            var tag = bytes.AsSpan(12, 16);
            var cipher = bytes.AsSpan(28);
            var plain = new byte[cipher.Length];
            using var aes = new AesGcm(_key, 16);
            aes.Decrypt(nonce, cipher, tag, plain);
            var text = Encoding.UTF8.GetString(plain);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static string Mask(string? key)
    {
        if (string.IsNullOrEmpty(key))
            return "";
        if (key.Length <= 4)
            return "••••";
        return "••••" + key[^4..];
    }

    private static string? First(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }
        return null;
    }
}
