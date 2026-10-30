using GpsTrainer;

namespace GpsTrainer.Server.Routing;

/// <summary>
/// One elevation source. Callers pass longitude then latitude and read
/// <see cref="ElevationLineResponseDto"/>. They do not choose a host, a cache, or a quota gate.
/// </summary>
public interface IElevationProvider
{
    string Name { get; }

    Task<ElevationLineResponseDto> ElevationLineAsync(
        IReadOnlyList<(double Longitude, double Latitude)> points,
        CancellationToken cancellationToken);
}
