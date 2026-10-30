using GpsTrainer;
using Microsoft.Extensions.DependencyInjection;

namespace GpsTrainer.Server.Routing;

/// <summary>
/// <c>Routing:ElevationProvider</c> picks the elevation source. An empty value
/// is OpenRouteService. Any other name answers with <see cref="ElevationLineResponseDto.Unavailable"/>.
/// </summary>
public static class ElevationProviderSelection
{
    public const string ConfigurationKey = "Routing:ElevationProvider";

    public static string Selected(IConfiguration configuration)
    {
        var configured = configuration[ConfigurationKey];
        return string.IsNullOrWhiteSpace(configured)
            ? OpenRouteServiceElevationProvider.ProviderName
            : configured.Trim();
    }

    public static bool UsesOpenRouteService(IConfiguration configuration) =>
        string.Equals(
            Selected(configuration),
            OpenRouteServiceElevationProvider.ProviderName,
            StringComparison.OrdinalIgnoreCase);

    public static IElevationProvider Resolve(IServiceProvider services)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        if (UsesOpenRouteService(configuration))
            return services.GetRequiredService<OpenRouteServiceElevationProvider>();
        return new UnavailableElevationProvider(Selected(configuration));
    }

    private sealed class UnavailableElevationProvider(string name) : IElevationProvider
    {
        public string Name { get; } = name;

        public Task<ElevationLineResponseDto> ElevationLineAsync(
            IReadOnlyList<(double Longitude, double Latitude)> points,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ElevationLineResponseDto
            {
                Unavailable = true,
                Message = "Elevation provider is not available."
            });
    }
}
