using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GpsTrainer.Web.Maps;

/// <summary>
/// Builds the map named by <c>Map:Provider</c>. An empty value means Leaflet, which is what the live site uses.
/// A future Google provider is a <see cref="ICourseMap"/> plus its own script, returned from here when the setting is <c>Google</c>.
/// </summary>
public static class CourseMaps
{
    public const string LeafletProvider = "Leaflet";

    public static ICourseMap Create(IServiceProvider services)
    {
        var name = services.GetRequiredService<IConfiguration>()["Map:Provider"];
        if (string.IsNullOrWhiteSpace(name) || name.Equals(LeafletProvider, StringComparison.OrdinalIgnoreCase))
            return ActivatorUtilities.CreateInstance<LeafletCourseMap>(services);

        throw new InvalidOperationException(
            $"Map provider '{name}' is not registered. Add a class that implements {nameof(ICourseMap)} "
            + "(for example GoogleCourseMap, with its own script under wwwroot/course-editor) and return it from "
            + $"{nameof(CourseMaps)}.{nameof(Create)} when Map:Provider is {name}. CourseEditor.razor stays as it is.");
    }
}
