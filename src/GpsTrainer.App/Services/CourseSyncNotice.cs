namespace GpsTrainer.App;

/// <summary>Tells the open screens that the server no longer has some rows.</summary>
public sealed class CourseSyncNotice
{
    public event Action<IReadOnlyList<Guid>, IReadOnlyList<Guid>>? Changed;

    public void Report(IReadOnlyList<Guid> removedCourses, IReadOnlyList<Guid> removedRaces)
    {
        if (removedCourses.Count == 0 && removedRaces.Count == 0)
            return;
        var courses = removedCourses.ToArray();
        var races = removedRaces.ToArray();
        MainThread.BeginInvokeOnMainThread(() => Changed?.Invoke(courses, races));
    }
}

/// <summary>
/// Refreshes the course list and leaves a screen whose course was removed.
/// A ride that is already on screen stays put.
/// </summary>
public sealed class CourseScreenSync
{
    public CourseScreenSync(CourseSyncNotice notice)
    {
        notice.Changed += OnChanged;
    }

    private async void OnChanged(IReadOnlyList<Guid> courses, IReadOnlyList<Guid> races)
    {
        try
        {
            var navigation = (Application.Current?.Windows.FirstOrDefault()?.Page as NavigationPage)?.Navigation;
            if (navigation is null)
                return;
            foreach (var page in navigation.NavigationStack.ToList())
            {
                if (page is CourseListPage list)
                    list.Reload();
                if (page is RaceListPage raceList && (courses.Contains(raceList.CourseId) || races.Count > 0))
                    raceList.Reload();
            }
            while (navigation.NavigationStack.LastOrDefault() is Page top)
            {
                if (top is RunningPage)
                    return;
                var courseId = top switch
                {
                    CourseDetailsPage details => details.CourseId,
                    MapCoursePage map => map.CourseId,
                    CourseEditPage editor => editor.CourseId,
                    RaceListPage raceList => raceList.CourseId,
                    _ => (Guid?)null
                };
                if (courseId is not Guid id || !courses.Contains(id))
                    return;
                await navigation.PopAsync();
            }
        }
        catch (Exception)
        {
            // The ride and the pages already on screen keep working.
        }
    }
}
