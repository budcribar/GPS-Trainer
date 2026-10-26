using GpsTrainer.Server.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace GpsTrainer.Server.Tests;

public class PostgresConnectionTests
{
    [Fact]
    public void Railway_url_requires_ssl_and_trusts_the_server_certificate()
    {
        var parsed = PostgresConnection.FromDatabaseUrl(
            "postgresql://gps:p%40ss@db.example.com:5432/railway?sslmode=require");
        var builder = new NpgsqlConnectionStringBuilder(parsed);
        Assert.Equal("db.example.com", builder.Host);
        Assert.Equal(5432, builder.Port);
        Assert.Equal("railway", builder.Database);
        Assert.Equal("gps", builder.Username);
        Assert.Equal("p@ss", builder.Password);
        Assert.Equal(SslMode.Require, builder.SslMode);
    }

    [Fact]
    public void Private_railway_host_prefers_ssl_when_the_url_omits_sslmode()
    {
        var parsed = PostgresConnection.FromDatabaseUrl("postgres://u:p@timescale.railway.internal/gps");
        var builder = new NpgsqlConnectionStringBuilder(parsed);
        Assert.Equal("timescale.railway.internal", builder.Host);
        Assert.Equal(5432, builder.Port);
        Assert.Equal("gps", builder.Database);
        Assert.Equal(SslMode.Prefer, builder.SslMode);
    }

    [Fact]
    public void Public_host_defaults_to_ssl_require()
    {
        var parsed = PostgresConnection.FromDatabaseUrl("postgresql://u:p@example.com:5433/gps");
        var builder = new NpgsqlConnectionStringBuilder(parsed);
        Assert.Equal(5433, builder.Port);
        Assert.Equal(SslMode.Require, builder.SslMode);
    }

    [Fact]
    public void Explicit_prefer_and_disable_are_honored()
    {
        var prefer = new NpgsqlConnectionStringBuilder(PostgresConnection.FromDatabaseUrl(
            "postgresql://u:p@example.com/gps?sslmode=prefer"));
        Assert.Equal(SslMode.Prefer, prefer.SslMode);

        var off = new NpgsqlConnectionStringBuilder(PostgresConnection.FromDatabaseUrl(
            "postgresql://u:p@example.com/gps?sslmode=disable"));
        Assert.Equal(SslMode.Disable, off.SslMode);
    }

    [Fact]
    public void Production_without_database_url_fails_fast()
    {
        var error = Assert.Throws<InvalidOperationException>(() => DatabaseSetup.RequireProductionDatabaseUrl(null));
        Assert.Contains("DATABASE_URL", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sqlite_startup_skips_extension_sql()
    {
        await DatabaseExtensionsInitializer.ApplyAsync("Sqlite", "Data Source=:memory:", NullLogger.Instance);
    }

    [Fact]
    public void Postgres_model_declares_postgis_and_timescaledb()
    {
        var options = new DbContextOptionsBuilder<GpsTrainerDb>()
            .UseNpgsql("Host=127.0.0.1;Database=gpstrainer;Username=gpstrainer;Password=")
            .Options;
        using var db = new GpsTrainerDb(options);
        var extensions = db.PostgresExtensionNames;
        Assert.Contains("postgis", extensions);
        Assert.Contains("timescaledb", extensions);
    }
}
