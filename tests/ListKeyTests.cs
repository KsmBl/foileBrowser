using FoileBrowser.Views;
using Hawkynt.NativeForms;

namespace FoileBrowser.Tests;

/// <summary>
/// What the keys over a file listing mean (PRD §6.1/§6.6). Both listings run through this one
/// mapping, so the details view and the gallery cannot drift apart.
/// </summary>
/// <remarks>
/// Return used to go the other way — back out to the parent folder — which left the everyday gesture
/// of stepping into the folder under the cursor with no key at all, and made Return the one key here
/// that did the opposite of what it does in every other file manager.
/// </remarks>
[TestFixture]
public class ListKeyTests
{
    [Test]
    public void Return_Opens_What_Is_Selected()
        => Assert.That(Gestures.ForListKey(Keys.Enter, alt: false), Is.EqualTo(ListAction.Activate));

    /// <summary>Alt+Enter is the properties dialog, which the menu bar dispatches.</summary>
    [Test]
    public void Alt_Return_Is_Left_For_The_Menu_Bar()
        => Assert.That(Gestures.ForListKey(Keys.Enter, alt: true), Is.EqualTo(ListAction.None));

    [Test]
    public void Backspace_Goes_Up()
        => Assert.That(Gestures.ForListKey(Keys.Back, alt: false), Is.EqualTo(ListAction.GoUp));

    [Test]
    public void Space_Opens_The_Quick_Preview()
        => Assert.That(Gestures.ForListKey(Keys.Space, alt: false), Is.EqualTo(ListAction.QuickPreview));

    [Test]
    public void Delete_And_F2_Do_What_They_Say()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Gestures.ForListKey(Keys.Delete, alt: false), Is.EqualTo(ListAction.Delete));
            Assert.That(Gestures.ForListKey(Keys.F2, alt: false), Is.EqualTo(ListAction.Rename));
        });
    }

    /// <summary>Anything else belongs to type-to-select or the menu bar, not to the listing.</summary>
    [Test]
    [TestCase(Keys.A)]
    [TestCase(Keys.Up)]
    [TestCase(Keys.Down)]
    [TestCase(Keys.Home)]
    [TestCase(Keys.F5)]
    public void Everything_Else_Passes_Through(Keys key)
        => Assert.That(Gestures.ForListKey(key, alt: false), Is.EqualTo(ListAction.None));
}
