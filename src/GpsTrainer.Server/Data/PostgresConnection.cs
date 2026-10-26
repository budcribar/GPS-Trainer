using Npgsql;

namespace GpsTrainer.Server.Data;

/// <summary>
/// Turns Railway's <c>postgresql://</c> <c>DATABASE_URL</c> into an Npgsql connection string.
/// </summary>
public static class PostgresConnection
{
    public static string FromDatabaseUrl(string databaseUrl)
    {
        if (string.IsNullOrWhiteSpace(databaseUrl))
            throw new InvalidOperationException("DATABASE_URL is empty.");

        if (!databaseUrl.Contains("://", StringComparison.Ordinal))
            return databaseUrl;

        if (!Uri.TryCreate(databaseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != "postgres" && uri.Scheme != "postgresql"))
        {
            throw new InvalidOperationException(
                "DATABASE_URL must be a postgresql:// URL or an Npgsql connection string.");
        }

        var user = "";
        var password = "";
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var parts = uri.UserInfo.Split(':', 2);
            user = Uri.UnescapeDataString(parts[0]);
            if (parts.Length == 2)
                password = Uri.UnescapeDataString(parts[1]);
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
            Database = uri.AbsolutePath.Trim('/'),
            Username = user,
            Password = password
        };
        ApplySsl(builder, uri.Host, ParseQuery(uri.Query));
        return builder.ConnectionString;
    }

    private static void ApplySsl(NpgsqlConnectionStringBuilder builder, string host, Dictionary<string, string> query)
    {
        query.TryGetValue("sslmode", out var sslMode);
        if (string.IsNullOrEmpty(sslMode) && query.TryGetValue("ssl", out var ssl) && ssl.Equals("true", StringComparison.OrdinalIgnoreCase))
            sslMode = "require";

        var privateNetwork = host.EndsWith(".railway.internal", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase);

        switch ((sslMode ?? "").Trim().ToLowerInvariant())
        {
            case "disable":
                builder.SslMode = SslMode.Disable;
                return;
            case "allow":
            case "prefer":
                builder.SslMode = SslMode.Prefer;
                return;
            case "verify-ca":
                builder.SslMode = SslMode.VerifyCA;
                return;
            case "verify-full":
                builder.SslMode = SslMode.VerifyFull;
                return;
            case "require":
                // Npgsql 10 Require encrypts and does not verify the CA. That is the old
                // Trust Server Certificate behavior; the property itself is now a no-op.
                builder.SslMode = SslMode.Require;
                return;
            default:
                // A public host must use TLS. Railway's private network may present a
                // certificate outside the trust store, or skip TLS until the client asks,
                // so Prefer keeps the connection up. Require already skips CA checks.
                builder.SslMode = privateNetwork ? SslMode.Prefer : SslMode.Require;
                return;
        }
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query))
            return values;
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0)
                continue;
            values[Uri.UnescapeDataString(pair[..eq])] = Uri.UnescapeDataString(pair[(eq + 1)..]);
        }
        return values;
    }
}
