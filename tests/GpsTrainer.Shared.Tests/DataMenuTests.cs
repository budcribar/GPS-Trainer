namespace GpsTrainer.Shared.Tests;

public class DataMenuTests
{
    [Fact]
    public void The_course_list_does_not_open_data_and_the_page_remains()
    {
        var screens = File.ReadAllText(RepoFile("src/GpsTrainer.App/Pages/Screens.cs"));
        Assert.Contains("public class DataPage", screens, StringComparison.Ordinal);
        Assert.DoesNotContain("new DataPage()", screens, StringComparison.Ordinal);
        Assert.DoesNotContain("menu_data, null", screens, StringComparison.Ordinal);
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
