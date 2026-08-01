using FoileBrowser.Models;
using FoileBrowser.ViewModels;
using FoileBrowser.Views;
using Hawkynt.NativeForms;

namespace FoileBrowser.Tests;

/// <summary>
/// The two buttons under the thumb, which mean here what they mean in a browser (PRD §6.1).
/// </summary>
/// <remarks>
/// They did nothing at all before, and nothing said why: the toolkit named only three mouse buttons,
/// so a press on either arrived as <see cref="MouseButtons.None"/> and fell through to the selection
/// handling like any other click.
/// </remarks>
[TestFixture]
public class MouseNavigationTests
{
    private static MouseEventArgs Press(MouseButtons button) => new(button, 10, 10, 0, KeyModifiers.None);

    private static async Task<FileTabViewModel> BrowsedTwoFolders()
    {
        var fs = new FakeFileSystem();
        fs.Entries.Add(new FileSystemEntry { Name = "sub", FullPath = "/demo/sub", Kind = FileSystemEntryKind.Directory });

        var tab = new FileTabViewModel(fs);
        await tab.NavigateToAsync("/demo");
        await tab.NavigateToAsync("/demo/sub");
        return tab;
    }

    [Test]
    public async Task The_First_Side_Button_Goes_Back()
    {
        var tab = await BrowsedTwoFolders();
        Assert.That(tab.CanGoBack, Is.True, "precondition: there is somewhere to go back to");

        var handled = Gestures.TryNavigate(Press(MouseButtons.XButton1), tab);

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(tab.CurrentPath, Is.EqualTo("/demo"));
        });
    }

    [Test]
    public async Task The_Second_Side_Button_Goes_Forward_Again()
    {
        var tab = await BrowsedTwoFolders();
        Gestures.TryNavigate(Press(MouseButtons.XButton1), tab);

        var handled = Gestures.TryNavigate(Press(MouseButtons.XButton2), tab);

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(tab.CurrentPath, Is.EqualTo("/demo/sub"));
        });
    }

    /// <summary>
    /// Back at the start of the history is not an error, but it must not fall through to the list
    /// either — the press was still meant for navigation, not for changing the selection.
    /// </summary>
    [Test]
    public async Task A_Side_Button_With_Nowhere_To_Go_Is_Still_Consumed()
    {
        var fs = new FakeFileSystem();
        var tab = new FileTabViewModel(fs);
        await tab.NavigateToAsync("/demo");

        Assert.Multiple(() =>
        {
            Assert.That(tab.CanGoBack, Is.False);
            Assert.That(Gestures.TryNavigate(Press(MouseButtons.XButton1), tab), Is.True);
            Assert.That(tab.CurrentPath, Is.EqualTo("/demo"));
        });
    }

    [Test]
    [TestCase(MouseButtons.Left)]
    [TestCase(MouseButtons.Right)]
    [TestCase(MouseButtons.Middle)]
    [TestCase(MouseButtons.None)]
    public async Task Every_Other_Button_Is_Left_Alone(MouseButtons button)
    {
        var tab = await BrowsedTwoFolders();

        Assert.Multiple(() =>
        {
            Assert.That(Gestures.TryNavigate(Press(button), tab), Is.False);
            Assert.That(tab.CurrentPath, Is.EqualTo("/demo/sub"), "the listing still gets the click");
        });
    }
}
