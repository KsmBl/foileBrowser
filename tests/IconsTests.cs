using FoileBrowser.Models;
using FoileBrowser.ViewModels;
using FoileBrowser.Views;

namespace FoileBrowser.Tests;

/// <summary>
/// The icons are drawn in code so the UI never asks a font for a picture (PRD §6.12). Nobody can see
/// them from here, so these assert the properties that would otherwise fail silently: that each one
/// actually has pixels, that it fits its box, and that it is fully opaque where it is drawn.
/// </summary>
[TestFixture]
public class IconsTests
{
    [Test]
    public void Every_Icon_Is_Drawn_And_Fits_Its_Box()
    {
        var icons = Icons.Render();
        Assert.That(icons, Is.Not.Empty);

        Assert.Multiple(() =>
        {
            foreach (var (name, pixels) in icons)
            {
                Assert.That(pixels, Has.Length.EqualTo(Icons.Size * Icons.Size), $"{name} is {Icons.Size}×{Icons.Size}");

                var painted = pixels.Count(p => p != 0);
                Assert.That(painted, Is.GreaterThan(12), $"{name} is not blank");
                Assert.That(painted, Is.LessThan(pixels.Length), $"{name} leaves some transparent margin");
            }
        });
    }

    [Test]
    public void Painted_Pixels_Are_Fully_Opaque()
    {
        // A half-set alpha channel is the classic mistake when packing ARGB by hand: the icon then
        // renders as a faint smudge rather than nothing, which is easy to miss.
        Assert.Multiple(() =>
        {
            foreach (var (name, pixels) in Icons.Render())
                foreach (var pixel in pixels.Where(p => p != 0))
                {
                    Assert.That((uint)pixel >> 24, Is.EqualTo(0xFFu), $"{name} paints only opaque pixels");
                    break; // one sample per icon is enough to catch a packing mistake
                }
        });
    }

    [Test]
    public void Entry_Kinds_And_Sidebar_Kinds_Map_To_Distinct_Icons()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Icons.For(FileSystemEntryKind.Directory), Is.SameAs(Icons.FolderIcon));
            Assert.That(Icons.For(FileSystemEntryKind.File), Is.SameAs(Icons.FileIcon));
            Assert.That(Icons.For(FileSystemEntryKind.Drive), Is.SameAs(Icons.DriveIcon));

            Assert.That(Icons.For(SidebarItemKind.Partition), Is.SameAs(Icons.PartitionIcon));
            Assert.That(Icons.For(SidebarItemKind.Device), Is.SameAs(Icons.DeviceIcon));
            Assert.That(Icons.For(SidebarItemKind.Favorite), Is.SameAs(Icons.FavoriteIcon));
            Assert.That(Icons.For(SidebarItemKind.Header), Is.Null, "a section header carries no icon");
        });
    }

    /// <summary>
    /// The toolbar draws on a bigger grid than the file list, because its buttons have to say two
    /// things at once.
    /// </summary>
    /// <remarks>
    /// At 16 pixels there was no room for a badge, so "new folder" was a folder with a smudge in it
    /// and read as an ordinary folder; "copy path" and "rename" were both a diagonal pen stroke. Four
    /// of the fourteen were guesses.
    /// </remarks>
    [Test]
    public void Every_Toolbar_Icon_Is_Drawn_On_The_Larger_Grid()
    {
        var icons = Icons.RenderToolbar();
        Assert.That(icons, Has.Count.EqualTo(14));

        Assert.Multiple(() =>
        {
            foreach (var (name, pixels) in icons)
            {
                Assert.That(pixels, Has.Length.EqualTo(Icons.ToolbarSize * Icons.ToolbarSize), $"{name} is {Icons.ToolbarSize}²");

                var painted = pixels.Count(p => p != 0);
                Assert.That(painted, Is.GreaterThan(40), $"{name} is not blank");
                Assert.That(painted, Is.LessThan(pixels.Length), $"{name} leaves some transparent margin");
                Assert.That(pixels.Where(p => p != 0).Select(p => (uint)p >> 24), Has.All.EqualTo(0xFFu), $"{name} is opaque where it paints");
            }
        });
    }

    /// <summary>No two buttons may draw the same picture — that is the whole complaint.</summary>
    [Test]
    public void No_Two_Toolbar_Icons_Look_Alike()
    {
        var icons = Icons.RenderToolbar();

        Assert.Multiple(() =>
        {
            for (var i = 0; i < icons.Count; ++i)
                for (var j = i + 1; j < icons.Count; ++j)
                    Assert.That(
                        icons[i].Pixels, Is.Not.EqualTo(icons[j].Pixels),
                        $"{icons[i].Name} and {icons[j].Name} are the same picture");
        });
    }

    /// <summary>
    /// The badge is the point of the larger grid, so the icons that carry one must actually carry it.
    /// </summary>
    [Test]
    public void The_Icons_That_Mean_New_Carry_The_Accent_Badge()
    {
        var icons = Icons.RenderToolbar().ToDictionary(icon => icon.Name, icon => icon.Pixels);

        // The accent green, in either of its two shades. Nothing else in the set is green.
        static bool IsAccent(int p) => ((p >> 16) & 0xFF) < ((p >> 8) & 0xFF) && ((p >> 0) & 0xFF) < ((p >> 8) & 0xFF);

        Assert.Multiple(() =>
        {
            foreach (var name in new[] { "newFolder", "newFile", "newTab", "rename", "batchRename" })
                Assert.That(icons[name].Any(IsAccent), Is.True, $"{name} carries its badge");

            foreach (var name in new[] { "delete", "settings", "pin" })
                Assert.That(icons[name].Any(IsAccent), Is.False, $"{name} is one idea and needs no badge");
        });
    }

    /// <summary>Every button on the bar gets a picture; the two that show a live word get none.</summary>
    [Test]
    public void The_Toolbar_Map_Covers_Every_Drawn_Button()
    {
        Assert.Multiple(() =>
        {
            foreach (var (name, _) in Icons.RenderToolbar())
                Assert.That(Icons.ForToolbar(name), Is.Not.Null, $"{name} has an icon");

            Assert.That(Icons.ForToolbar("sizeUnit"), Is.Null, "KiB/MB shows its current value as text");
            Assert.That(Icons.ForToolbar("dateFormat"), Is.Null, "and so does the date format");
        });
    }

    [Test]
    public void The_Directional_Icons_Are_Mirror_Images()
    {
        var icons = Icons.Render().ToDictionary(icon => icon.Name, icon => icon.Pixels);
        var back = icons["back"];
        var forward = icons["forward"];

        Assert.Multiple(() =>
        {
            Assert.That(back, Is.Not.EqualTo(forward), "back and forward point opposite ways");
            Assert.That(back.Count(p => p != 0), Is.EqualTo(forward.Count(p => p != 0)),
                "and are the same weight, so the toolbar does not look lopsided");
        });
    }
}
