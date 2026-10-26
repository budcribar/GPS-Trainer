using GpsTrainer;
using GpsTrainer.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace GpsTrainer.Server.Routing;

public sealed record ResolvedRouting(string Provider, string? ApiKey, string KeySource);

public sealed class RoutingSettingsStore
{
    private readonly SecretProtector _protector;

    public RoutingSettingsStore(SecretProtector protector) => _protector = protector;

    public async Task<ResolvedRouting> ResolveAsync(GpsTrainerDb db, IConfiguration config, CancellationToken cancellationToken = default)
    {
        var row = await db.RoutingSettings.FindAsync([1], cancellationToken);
        var provider = First(row?.Provider, config["Routing:Provider"], OpenRouteServiceRoutingProvider.ProviderName)!;

        if (!string.IsNullOrWhiteSpace(row?.ProtectedApiKey))
        {
            var stored = _protector.Unprotect(row.ProtectedApiKey);
            if (!string.IsNullOrWhiteSpace(stored))
                return new ResolvedRouting(provider, stored, "stored");
        }

        var environment = First(config["Routing:OrsApiKey"], Environment.GetEnvironmentVariable("ORS_API_KEY"));
        return new ResolvedRouting(provider, environment, environment is null ? "none" : "environment");
    }

    public RoutingSettingsDto Describe(ResolvedRouting resolved) => new()
    {
        Provider = resolved.Provider,
        Providers = [OpenRouteServiceRoutingProvider.ProviderName],
        KeyConfigured = !string.IsNullOrWhiteSpace(resolved.ApiKey),
        KeyMask = SecretProtector.Mask(resolved.ApiKey),
        KeySource = resolved.KeySource
    };

    public async Task<string?> SaveAsync(GpsTrainerDb db, RoutingSettingsWriteDto body, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(body.Provider, OpenRouteServiceRoutingProvider.ProviderName, StringComparison.OrdinalIgnoreCase))
            return "Only OpenRouteService is available.";

        var row = await db.RoutingSettings.FindAsync([1], cancellationToken);
        if (row is null)
        {
            row = new RoutingSettingRow { Id = 1, Provider = OpenRouteServiceRoutingProvider.ProviderName };
            db.RoutingSettings.Add(row);
        }

        row.Provider = OpenRouteServiceRoutingProvider.ProviderName;
        if (body.ClearStoredKey)
            row.ProtectedApiKey = null;
        else if (!string.IsNullOrWhiteSpace(body.ApiKey))
            row.ProtectedApiKey = _protector.Protect(body.ApiKey.Trim());

        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    private static string? First(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }
        return null;
    }
}
