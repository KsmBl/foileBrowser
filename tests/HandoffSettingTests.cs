using System.Text.Json;
using FoileBrowser.Models;

namespace FoileBrowser.Tests;

/// <summary>
/// Where a handed-over folder lands is a preference (PRD §6.12). The view switches on the stored
/// string, so these pin the default and that the value survives a settings round-trip — a typo in
/// either direction would silently fall back to opening a tab.
/// </summary>
[TestFixture]
public class HandoffSettingTests
{
    [Test]
    public void Defaults_To_A_Tab()
    {
        Assert.That(new AppSettings().OpenHandoffIn, Is.EqualTo("Tab"));
    }

    [TestCase("Tab")]
    [TestCase("Pane")]
    [TestCase("Window")]
    public void Survives_A_Settings_Round_Trip(string choice)
    {
        var written = JsonSerializer.Serialize(new AppSettings { OpenHandoffIn = choice });

        var read = JsonSerializer.Deserialize<AppSettings>(written);

        Assert.That(read, Is.Not.Null);
        Assert.That(read!.OpenHandoffIn, Is.EqualTo(choice));
    }

    [Test]
    public void An_Unknown_Value_Is_Left_Alone_For_The_View_To_Fall_Back_On()
    {
        // Settings files are hand-editable; the view treats anything it does not know as "Tab"
        // rather than refusing to start, so the model keeps whatever it was given.
        var read = JsonSerializer.Deserialize<AppSettings>("""{"OpenHandoffIn":"Elsewhere"}""");

        Assert.That(read!.OpenHandoffIn, Is.EqualTo("Elsewhere"));
    }
}
