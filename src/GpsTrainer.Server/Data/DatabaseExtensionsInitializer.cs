using Npgsql;

namespace GpsTrainer.Server.Data;

/// <summary>
/// Creates PostGIS and TimescaleDB on every Postgres start. The Railway image cannot be
/// reached from outside to run this SQL by hand.
/// </summary>
public static class DatabaseExtensionsInitializer
{
    public static readonly string[] ExtensionNames = ["postgis", "timescaledb"];

    public static async Task ApplyAsync(string? provider, string connectionString, ILogger logger, CancellationToken cancellationToken = default)
    {
        if (!DatabaseSetup.IsPostgres(provider))
        {
            logger.LogInformation("Skipping PostgreSQL extensions because the provider is {Provider}.", string.IsNullOrWhiteSpace(provider) ? "Sqlite" : provider);
            return;
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var before = await ReadInstalledAsync(connection, cancellationToken);
        foreach (var name in ExtensionNames)
        {
            try
            {
                await using var command = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS \"" + name + "\";", connection);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                // Geometry columns are not in the schema yet, so a missing PostGIS install does not stop startup.
                // Timescale hypertables stay opt-in. The ordinary Position table is the fallback.
                logger.LogError(ex, "CREATE EXTENSION {Extension} failed. Confirm the role can create extensions and that timescaledb is in shared_preload_libraries. The app keeps lat/long columns and a normal Position table.", name);
            }
        }

        var after = await ReadInstalledAsync(connection, cancellationToken);
        foreach (var name in ExtensionNames)
        {
            if (after.Contains(name) && before.Contains(name))
                logger.LogInformation("PostgreSQL extension {Extension} was already present.", name);
            else if (after.Contains(name))
                logger.LogInformation("PostgreSQL extension {Extension} was created.", name);
            else
                logger.LogWarning("PostgreSQL extension {Extension} is not installed.", name);
        }

        try
        {
            await connection.ReloadTypesAsync(cancellationToken);
            logger.LogInformation("Npgsql reloaded PostgreSQL types after CREATE EXTENSION.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Npgsql ReloadTypes failed after CREATE EXTENSION. PostGIS types may be invisible until the next process start.");
        }
    }

    private static async Task<HashSet<string>> ReadInstalledAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = new NpgsqlCommand("SELECT extname FROM pg_extension WHERE extname = ANY (@names);", connection);
        command.Parameters.AddWithValue("names", ExtensionNames);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            installed.Add(reader.GetString(0));
        return installed;
    }
}
