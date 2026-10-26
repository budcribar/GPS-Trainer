using System.Text.Json;
using GpsTrainer;

namespace GpsTrainer.Web.Services;

/// <summary>The signed-in login kept in the browser so a refresh does not sign the user out.</summary>
public sealed class SessionSnapshot
{
    public const string StorageKey = "gpstrainer.session";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Token { get; set; } = "";
    public string Login { get; set; } = "";
    public UserProfileDto? User { get; set; }

    public static string Serialize(SessionSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, Json);

    public static SessionSnapshot? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            var snapshot = JsonSerializer.Deserialize<SessionSnapshot>(json, Json);
            if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.Token) || string.IsNullOrWhiteSpace(snapshot.Login))
                return null;
            return snapshot;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
