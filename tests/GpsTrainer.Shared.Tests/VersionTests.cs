namespace GpsTrainer.Shared.Tests;

public class VersionTests
{
    [Fact]
    public void The_app_version_is_1_47_and_the_about_screen_matches()
    {
        var project = File.ReadAllText(RepoFile("src/GpsTrainer.App/GpsTrainer.App.csproj"));
        var screens = File.ReadAllText(RepoFile("src/GpsTrainer.App/Pages/Screens.cs"));
        Assert.Contains("<ApplicationDisplayVersion>1.47</ApplicationDisplayVersion>", project, StringComparison.Ordinal);
        Assert.Contains("<ApplicationVersion>147</ApplicationVersion>", project, StringComparison.Ordinal);
        Assert.Contains("AppStrings.rev + \" 1.47\"", screens, StringComparison.Ordinal);
    }

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(relative);
    }
}
