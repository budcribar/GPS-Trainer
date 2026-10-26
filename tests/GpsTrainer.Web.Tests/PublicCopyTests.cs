using System.Net;
using GpsTrainer.Web.Pages;
using GpsTrainer.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GpsTrainer.Web.Tests;

public sealed class PublicCopyTests : TestContext
{
    public PublicCopyTests()
    {
        Services.AddSingleton(new Session());
        Services.AddSingleton(new HttpClient(new QuietHandler()) { BaseAddress = new Uri("http://localhost/") });
        Services.AddSingleton<GpsApi>();
    }

    [Fact]
    public void Support_lists_the_two_training_videos()
    {
        var cut = RenderComponent<Help>();
        var lessons = cut.FindAll(".support-lessons li");
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "GpsTrainer.Web", "Pages", "Help.razor"));

        Assert.Contains("@page \"/help\"", source);
        Assert.Contains("@page \"/tutorial\"", source);
        Assert.Contains("gpstrainer-icon.svg", cut.Find(".support-mark").GetAttribute("src"));
        Assert.Equal("Support", cut.Find("h1").TextContent);
        Assert.Equal(
            ["Training videos", "Documentation", "Account", "Synchronization"],
            cut.FindAll("h2").Select(heading => heading.TextContent).ToArray());
        Assert.Equal(2, lessons.Count);
        Assert.Equal("Lesson 1", lessons[0].QuerySelector("a")!.TextContent);
        Assert.Equal("https://www.youtube.com/watch?v=Q1QgitCJEDs", lessons[0].QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("_blank", lessons[0].QuerySelector("a")!.GetAttribute("target"));
        Assert.Equal("noopener", lessons[0].QuerySelector("a")!.GetAttribute("rel"));
        Assert.Equal("Define your first course", lessons[0].QuerySelector("span")!.TextContent);
        Assert.Equal("Lesson 2", lessons[1].QuerySelector("a")!.TextContent);
        Assert.Equal("https://www.youtube.com/watch?v=Mam7qHclL2Y", lessons[1].QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("_blank", lessons[1].QuerySelector("a")!.GetAttribute("target"));
        Assert.Equal("noopener", lessons[1].QuerySelector("a")!.GetAttribute("rel"));
        Assert.Equal("More advanced features", lessons[1].QuerySelector("span")!.TextContent);
        Assert.Equal("Privacy policy", cut.Find("a[href='privacy']").TextContent);
        Assert.Contains("gpsTrainer speaks your pace, speed, distance, and the turns on a course while you move. Intervals are a start marker and a finish marker. After a race, this site times how long the track took to cross each pair.", cut.Markup);
        var card = SupportCard();
        Assert.Contains("background: #fff", card, StringComparison.Ordinal);
        Assert.DoesNotContain("#E8B836", card, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("border-top", card, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_home_page_does_not_describe_itself_as_a_port()
    {
        var cut = RenderComponent<Home>();

        Assert.Contains("gpstrainer-icon.svg", cut.Find(".home-mark").GetAttribute("src"));
        Assert.Equal("Endurance sports software", cut.Find("h1").TextContent);
        Assert.Equal("What is gpsTrainer?", cut.Find("a[href='about']").TextContent);
        Assert.Equal("TruePace", cut.Find("a[href='truepace']").TextContent);
        Assert.Equal("Generate a Fort Collins cycling loop", cut.Find("a[href='generate']").TextContent);
        Assert.Contains("This race proven system lets you define and race your own courses, or download courses and race against times that are already logged. Set speed and pace goals and hear where you are against those goals.", cut.Markup);
        Assert.Contains("gpsTrainer keeps track of distance, elapsed time, average pace, average speed, and altitude gained. It tells you if you are ahead or behind your goal, and by how much, at the checkpoint intervals you set.", cut.Markup);
        Assert.Contains("technology is part of the training program for cycling, running, and cross-country skiing.", cut.Markup);
        Assert.Contains("from the paved trail corridors and on-road bike lanes.", cut.Markup);
        Assert.DoesNotContain("this port", cut.Markup);
        Assert.DoesNotContain("public host", cut.Markup);
        var card = HomeCard();
        Assert.Contains("background: #fff", card, StringComparison.Ordinal);
        Assert.DoesNotContain("#E8B836", card, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("border-top", card, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Support_keeps_the_testimonials_link_and_drops_the_forum_notes()
    {
        var cut = RenderComponent<Help>();

        Assert.Equal("Support", cut.Find("h1").TextContent);
        Assert.Contains("The phone and this site share one login.", cut.Markup);
        Assert.Contains("A course shows up on the phone after it is enabled for download.", cut.Markup);
        Assert.Contains("Testimonials", cut.Find("a[href='testimonials']").TextContent);
        Assert.DoesNotContain("Forum", cut.Markup);
        Assert.DoesNotContain("InstantForum", cut.Markup);
        Assert.DoesNotContain("PBKDF2", cut.Markup);
        Assert.DoesNotContain("16-character", cut.Markup);
    }

    [Fact]
    public void Contact_keeps_the_emails_and_phone_and_drops_fax_and_mail()
    {
        var cut = RenderComponent<Contact>();
        var lines = cut.FindAll("p").Select(p => p.TextContent.Trim()).ToArray();

        Assert.Contains("gpstrainer-icon.svg", cut.Find(".contact-mark").GetAttribute("src"));
        Assert.Equal("Contact", cut.Find("h1").TextContent);
        Assert.Equal(
            ["Support email: support@gpsTrainer.com", "Information email: info@gpsTrainer.com", "Privacy policy"],
            lines);
        Assert.Equal("privacy", cut.Find("a[href='privacy']").GetAttribute("href"));
        Assert.Equal("mailto:support@gpsTrainer.com", cut.Find("a[href='mailto:support@gpsTrainer.com']").GetAttribute("href"));
        Assert.Equal("mailto:info@gpsTrainer.com", cut.Find("a[href='mailto:info@gpsTrainer.com']").GetAttribute("href"));
        Assert.DoesNotContain("801-740-4947", cut.Markup);
        Assert.DoesNotContain("Fax", cut.Markup);
        Assert.DoesNotContain("P.O. Box", cut.Markup);
        Assert.DoesNotContain("Mail:", cut.Markup);
        Assert.DoesNotContain("recovered", cut.Markup);
        Assert.DoesNotContain("new site host", cut.Markup);
        var card = ContactCard();
        Assert.Contains("background: #fff", card, StringComparison.Ordinal);
        Assert.DoesNotContain("#E8B836", card, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("border-top", card, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Download_tells_the_reader_to_use_the_phone_app()
    {
        var cut = RenderComponent<Download>();

        Assert.Contains("Use the gpsTrainer phone app with the same account as this site.", cut.Markup);
        Assert.Contains("Sign in, then enable a course for download.", cut.Markup);
        Assert.DoesNotContain("EXCLUDED", cut.Markup);
        Assert.DoesNotContain("Windows Mobile", cut.Markup);
        Assert.DoesNotContain("MAUI", cut.Markup);
        Assert.DoesNotContain("recovered", cut.Markup);
    }

    [Fact]
    public void TruePace_keeps_the_goal_explanation()
    {
        var cut = RenderComponent<TruePace>();

        Assert.Contains("TruePace is the original name for a goal time laid onto a course.", cut.Markup);
        Assert.Contains("Uphill segments cost more time than the same distance on the flat. This site shows the goal you set.", cut.Markup);
        Assert.DoesNotContain("recovered", cut.Markup);
        Assert.DoesNotContain("GenerateRace", cut.Markup);
    }

    [Fact]
    public void Find_does_not_name_the_old_search_call()
    {
        var cut = RenderComponent<Find>();

        Assert.Contains("Published courses that an admin has approved, within the radius of the point you enter.", cut.Markup);
        Assert.DoesNotContain("SelectCourses", cut.Markup);
    }

    [Fact]
    public void Forgot_password_does_not_mention_the_mail_server()
    {
        var cut = RenderComponent<Forgot>();

        Assert.Contains("Enter the login or the email on the account. This form does not ask a security question.", cut.Markup);
        Assert.DoesNotContain("SMTP", cut.Markup);
        cut.Find("button").Click();
        Assert.Equal("If that account exists, a reset was issued.", cut.Find("p:last-of-type").TextContent);
        Assert.DoesNotContain("does not confirm", cut.Markup);
    }

    [Fact]
    public void Admin_does_not_describe_the_old_admin_site()
    {
        var cut = RenderComponent<Admin>();

        Assert.Equal("Admin", cut.Find("h1").TextContent);
        Assert.Contains("Sign in with an admin login.", cut.Markup);
        Assert.DoesNotContain("ExecuteSQLCommand", cut.Markup);
        Assert.DoesNotContain("recovered", cut.Markup);
        Assert.DoesNotContain("AdminLogins", cut.Markup);
    }

    private static string HomeCard() => CardRule(".home {");

    private static string SupportCard() => CardRule(".support {");

    private static string ContactCard() => CardRule(".contact {");

    [Fact]
    public void Privacy_states_what_the_account_and_the_ride_store()
    {
        var cut = RenderComponent<Privacy>();

        Assert.Contains("gpstrainer-icon.svg", cut.Find(".privacy-mark").GetAttribute("src"));
        Assert.Equal("Privacy policy", cut.Find("h1").TextContent);
        Assert.Contains("We do not rent or sell your personal information.", cut.Markup);
        Assert.Contains("email address", cut.Markup);
        Assert.Contains("home location", cut.Markup);
        Assert.Contains("location trail", cut.Markup);
        Assert.Contains("only while a ride is being tracked", cut.Markup);
        Assert.Contains("does not connect to Strava", cut.Markup);
        Assert.Contains("OpenRouteService", cut.Markup);
        Assert.Contains("does not delete them on a schedule", cut.Markup);
        Assert.Contains("Account page deletes the account", cut.Markup);
        Assert.Contains("courses, waypoints, rides, and location trails", cut.Markup);
        Assert.Equal("mailto:support@gpsTrainer.com", cut.Find("a[href='mailto:support@gpsTrainer.com']").GetAttribute("href"));
        Assert.DoesNotContain("recovered", cut.Markup);
        Assert.DoesNotContain("PBKDF2", cut.Markup);
        Assert.DoesNotContain("endpoint", cut.Markup);
        var card = CardRule(".privacy {");
        Assert.Contains("background: #fff", card, StringComparison.Ordinal);
        Assert.DoesNotContain("#E8B836", card, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("border-top", card, StringComparison.OrdinalIgnoreCase);
    }

    private static string CardRule(string selector)
    {
        var css = File.ReadAllText(Path.Combine(RepoRoot(), "src", "GpsTrainer.Web", "wwwroot", "css", "app.css"));
        var start = css.IndexOf(selector, StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = css.IndexOf('}', start);
        return css[start..end];
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GPS-Trainer.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private sealed class QuietHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
    }
}
