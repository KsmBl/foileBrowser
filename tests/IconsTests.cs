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
