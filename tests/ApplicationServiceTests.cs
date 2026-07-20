using FoileBrowser.Services;

namespace FoileBrowser.Tests;

[TestFixture]
public class ApplicationServiceTests
{
    private string _dir = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "foile-apps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_dir, recursive: true);

    private string WriteDesktop(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Test]
    public void Parses_Name_Exec_And_Mime_Types()
    {
        var file = WriteDesktop("editor.desktop", """
            [Desktop Entry]
            Type=Application
            Name=Test Editor
            Exec=testedit --flag %f
            MimeType=text/plain;text/markdown;
            """);

        var entry = DesktopEntry.TryParse(file);

        Assert.That(entry, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(entry!.Id, Is.EqualTo("editor.desktop"));
            Assert.That(entry.Name, Is.EqualTo("Test Editor"));
            Assert.That(entry.MimeTypes, Is.EquivalentTo(new[] { "text/plain", "text/markdown" }));
        });
    }

    [Test]
    public void Ignores_Localized_Names_And_Later_Groups()
    {
        var file = WriteDesktop("localized.desktop", """
            [Desktop Entry]
            Type=Application
            Name=Plain Name
            Name[de]=Deutscher Name
            Exec=app %f
            [Desktop Action New]
            Name=Not The Entry Name
            """);

        Assert.That(DesktopEntry.TryParse(file)?.Name, Is.EqualTo("Plain Name"));
    }

    [Test]
    public void Skips_Hidden_NoDisplay_And_Non_Application_Entries()
    {
        var hidden = WriteDesktop("hidden.desktop", """
            [Desktop Entry]
            Type=Application
            Name=Hidden
            Exec=app
            NoDisplay=true
            """);
        var link = WriteDesktop("link.desktop", """
            [Desktop Entry]
            Type=Link
            Name=A Link
            URL=https://example.com
            """);

        Assert.Multiple(() =>
        {
            Assert.That(DesktopEntry.TryParse(hidden), Is.Null);
            Assert.That(DesktopEntry.TryParse(link), Is.Null);
        });
    }

    [Test]
    public void Substitutes_The_File_Into_The_Exec_Field_Code()
    {
        var file = WriteDesktop("app.desktop", """
            [Desktop Entry]
            Type=Application
            Name=App
            Exec=viewer %i --open %f --quiet
            """);

        var argv = DesktopEntry.TryParse(file)!.BuildCommand("/tmp/a b.txt");

        // %i is dropped, %f becomes the path, and the path is passed as one argv entry despite its space.
        Assert.That(argv, Is.EqualTo(new[] { "viewer", "--open", "/tmp/a b.txt", "--quiet" }));
    }

    [Test]
    public void Appends_The_File_When_Exec_Declares_No_Field_Code()
    {
        var file = WriteDesktop("noarg.desktop", """
            [Desktop Entry]
            Type=Application
            Name=App
            Exec=viewer
            """);

        Assert.That(DesktopEntry.TryParse(file)!.BuildCommand("/tmp/x.txt"),
            Is.EqualTo(new[] { "viewer", "/tmp/x.txt" }));
    }

    [Test]
    public void Wildcard_Registration_Matches_Any_Subtype()
    {
        var file = WriteDesktop("any-text.desktop", """
            [Desktop Entry]
            Type=Application
            Name=Any Text
            Exec=app %f
            MimeType=text/*;
            """);

        var entry = DesktopEntry.TryParse(file)!;

        Assert.Multiple(() =>
        {
            Assert.That(entry.MatchesWildcard("text/markdown"), Is.True);
            Assert.That(entry.MatchesWildcard("image/png"), Is.False);
            Assert.That(entry.MatchesWildcard("nonsense"), Is.False);
        });
    }

    [Test]
    public async Task Candidates_Are_Empty_Off_Linux_Rather_Than_Throwing()
    {
        var svc = new ApplicationService();
        var candidates = await svc.GetCandidatesAsync(Path.Combine(_dir, "nothing-here.txt"));

        // On Linux an unknown file may still map to a generic type; either way this must not throw.
        Assert.That(candidates, Is.Not.Null);
        if (!OperatingSystem.IsLinux())
            Assert.That(candidates, Is.Empty);
    }
}
