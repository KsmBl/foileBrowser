using FoileBrowser.Views;
using Hawkynt.NativeForms;

namespace FoileBrowser.Tests;

/// <summary>
/// What a drop will and will not accept (PRD §6.3). The gesture itself needs a pointer; these pin
/// the rules that stop it doing something destructive or nonsensical.
/// </summary>
[TestFixture]
public class FileDropTests
{
    private static string P(params string[] parts) => Path.Combine([Path.DirectorySeparatorChar.ToString(), .. parts]);

    [Test]
    public void Files_Can_Be_Dropped_Into_Another_Folder()
    {
        var drag = new FileDrag([P("from", "a.txt")], P("from"));

        Assert.That(
            FileDrop.Allowed(drag, P("to")),
            Is.EqualTo(DragDropEffects.Copy | DragDropEffects.Move));
    }

    [Test]
    public void Dropping_Back_Where_They_Already_Are_Is_Refused()
    {
        var drag = new FileDrag([P("from", "a.txt")], P("from"));

        Assert.That(FileDrop.Allowed(drag, P("from")), Is.EqualTo(DragDropEffects.None));
    }

    [Test]
    public void A_Folder_Cannot_Be_Dropped_Inside_Itself()
    {
        var folder = P("from", "photos");
        var drag = new FileDrag([folder], P("from"));

        Assert.That(
            FileDrop.Allowed(drag, Path.Combine(folder, "2026")),
            Is.EqualTo(DragDropEffects.None),
            "moving a folder into its own subtree would recurse");
    }

    [Test]
    public void A_Sibling_With_A_Shared_Prefix_Is_Still_Fine()
    {
        // "photos" must not be taken to contain "photos-old" just because the name starts the same.
        var drag = new FileDrag([P("from", "photos")], P("from"));

        Assert.That(
            FileDrop.Allowed(drag, P("from", "photos-old")),
            Is.EqualTo(DragDropEffects.Copy | DragDropEffects.Move));
    }

    [Test]
    public void Nothing_To_Drop_Or_Nowhere_To_Put_It_Is_Refused()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FileDrop.Allowed(null, P("to")), Is.EqualTo(DragDropEffects.None));
            Assert.That(FileDrop.Allowed(new FileDrag([], P("from")), P("to")), Is.EqualTo(DragDropEffects.None));
            Assert.That(FileDrop.Allowed(new FileDrag([P("a")], P("from")), null), Is.EqualTo(DragDropEffects.None));
        });
    }
}
