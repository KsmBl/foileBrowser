using FoileBrowser.Services;

namespace FoileBrowser.Tests;

[TestFixture]
public class SettingsAndTagsTests
{
    private string _dir = null!;
    private string _file = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "foile-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(_dir, "settings.json");
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Test]
    public async Task Missing_File_Yields_Defaults()
    {
        var settings = new SettingsService(_file);
        await settings.LoadAsync();

        Assert.That(settings.Current.ThemeVariant, Is.EqualTo("System"));
        Assert.That(settings.Current.IsDualPane, Is.True);
    }

    [Test]
    public async Task Save_Then_Load_Round_Trips()
    {
        var settings = new SettingsService(_file);
        await settings.LoadAsync();
        settings.Current.ThemeVariant = "Dark";
        settings.Current.AccentColor = "#FF0000";
        settings.Current.Favorites.Add("/home/user/work");
        await settings.SaveAsync();

        var reloaded = new SettingsService(_file);
        await reloaded.LoadAsync();

        Assert.That(reloaded.Current.ThemeVariant, Is.EqualTo("Dark"));
        Assert.That(reloaded.Current.AccentColor, Is.EqualTo("#FF0000"));
        Assert.That(reloaded.Current.Favorites, Does.Contain("/home/user/work"));
    }

    [Test]
    public async Task Corrupt_File_Falls_Back_To_Defaults()
    {
        await File.WriteAllTextAsync(_file, "{ this is not valid json ");
        var settings = new SettingsService(_file);

        await settings.LoadAsync();

        Assert.That(settings.Current.ThemeVariant, Is.EqualTo("System"));
    }

    [Test]
    public async Task Tag_Service_Persists_Through_Settings()
    {
        var settings = new SettingsService(_file);
        await settings.LoadAsync();
        var tags = new TagService(settings);

        await tags.SetTagAsync("/x/file.txt", "#E5484D");

        Assert.That(tags.GetTag("/x/file.txt"), Is.EqualTo("#E5484D"));

        var reloaded = new SettingsService(_file);
        await reloaded.LoadAsync();
        Assert.That(new TagService(reloaded).GetTag("/x/file.txt"), Is.EqualTo("#E5484D"));
    }

    [Test]
    public async Task Clearing_A_Tag_Removes_It()
    {
        var settings = new SettingsService(_file);
        await settings.LoadAsync();
        var tags = new TagService(settings);
        await tags.SetTagAsync("/x/file.txt", "#46A758");

        await tags.SetTagAsync("/x/file.txt", null);

        Assert.That(tags.GetTag("/x/file.txt"), Is.Null);
    }

    [Test]
    public void Palette_Has_Six_Colors()
    {
        var tags = new TagService(new SettingsService(_file));
        Assert.That(tags.Palette, Has.Count.EqualTo(6));
    }
}
