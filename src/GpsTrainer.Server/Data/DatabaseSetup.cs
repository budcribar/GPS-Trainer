using Microsoft.EntityFrameworkCore;

namespace GpsTrainer.Server.Data;

public static class DatabaseSetup
{
    public static void UseProvider(DbContextOptionsBuilder options, string? provider, string connectionString)
    {
        if (IsPostgres(provider))
        {
            options.UseNpgsql(connectionString);
            return;
        }

        if (string.IsNullOrWhiteSpace(provider) || provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            options.UseSqlite(connectionString);
            return;
        }

        throw new InvalidOperationException(
            $"Unknown DatabaseProvider '{provider}'. Use Sqlite for development or Postgres for gps-trainer.com.");
    }

    public static string RequireProductionDatabaseUrl(string? databaseUrl)
    {
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            throw new InvalidOperationException(
                "DATABASE_URL is required when ASPNETCORE_ENVIRONMENT is Production. Set DATABASE_URL=${{TimescaleDB.DATABASE_URL}}.");
        }

        return PostgresConnection.FromDatabaseUrl(databaseUrl);
    }

    public static bool IsPostgres(string? provider) =>
        provider is not null && (
            provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase)
            || provider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase)
            || provider.Equals("Npgsql", StringComparison.OrdinalIgnoreCase));
}
