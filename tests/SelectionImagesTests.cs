using FoileBrowser.Models;
using FoileBrowser.Services;

namespace FoileBrowser.Tests;

/// <summary>
/// What a multi-item selection offers the preview panel to show (PRD §6.5).
/// </summary>
/// <remarks>
/// Selecting several things used to produce a slab of statistics whatever was in it, so picking a
/// handful of photographs — or the folder holding them — showed byte totals and no photographs.
/// </remarks>
[TestFixture]
public class SelectionImagesTests
{
    private static FileSystemEntry File(string path) => new()
    {
        Name = System.IO.Path.GetFileName(path),
        FullPath = path,
        Kind = FileSystemEntryKind.File,
    };

    private static FileSystemEntry Folder(string path) => new()
    {
        Name = System.IO.Path.GetFileName(path),
        FullPath = path,
        Kind = FileSystemEntryKind.Directory,
    };

    /// <summary>A directory listing built from a map, so a test can shape a whole tree inline.</summary>
    private static Func<string, CancellationToken, Task<IReadOnlyList<FileSystemEntry>>> Tree(
        Dictionary<string, FileSystemEntry[]> map)
        => (path, _) => Task.FromResult<IReadOnlyList<FileSystemEntry>>(
            map.TryGetValue(path, out var children) ? children : []);

    [Test]
    public async Task Picked_Files_Come_Back_In_The_Order_They_Were_Given()
    {
        var result = await SelectionImages.CollectAsync(
            [File("/a/one.png"), File("/a/notes.txt"), File("/a/two.jpg")],
            Tree([]));

        Assert.That(result.Paths, Is.EqualTo(new[] { "/a/one.png", "/a/two.jpg" }),
            "the text file is not a picture, and the pictures keep their order");
    }

    [Test]
    public async Task A_Selected_Folder_Contributes_The_Pictures_Inside_It()
    {
        var result = await SelectionImages.CollectAsync(
            [Folder("/album")],
            Tree(new() { ["/album"] = [File("/album/a.png"), File("/album/readme.md"), File("/album/b.png")] }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Paths, Is.EqualTo(new[] { "/album/a.png", "/album/b.png" }));
            Assert.That(result.Folders, Is.EqualTo(1));
            Assert.That(result.FolderFiles, Is.EqualTo(3), "everything in it is counted, picture or not");
        });
    }

    [Test]
    public async Task Files_Picked_Directly_Come_Before_Anything_Found_By_Walking()
    {
        var result = await SelectionImages.CollectAsync(
            [Folder("/album"), File("/loose.png")],
            Tree(new() { ["/album"] = [File("/album/a.png")] }));

        Assert.That(result.Paths, Is.EqualTo(new[] { "/loose.png", "/album/a.png" }),
            "what was clicked is what should come up first");
    }

    [Test]
    public async Task A_Picture_Reached_Two_Ways_Is_Only_Shown_Once()
    {
        var result = await SelectionImages.CollectAsync(
            [File("/album/a.png"), Folder("/album")],
            Tree(new() { ["/album"] = [File("/album/a.png")] }));

        Assert.That(result.Paths, Is.EqualTo(new[] { "/album/a.png" }));
    }

    [Test]
    public async Task The_Walk_Goes_Into_Sub_Folders()
    {
        var result = await SelectionImages.CollectAsync(
            [Folder("/root")],
            Tree(new()
            {
                ["/root"] = [Folder("/root/sub")],
                ["/root/sub"] = [File("/root/sub/deep.png")],
            }));

        Assert.That(result.Paths, Is.EqualTo(new[] { "/root/sub/deep.png" }));
    }

    /// <summary>A selection can be the root of a disk, and a preview panel is not worth a full walk.</summary>
    [Test]
    public async Task The_Walk_Stops_Going_Deeper_Eventually()
    {
        var map = new Dictionary<string, FileSystemEntry[]>();
        var path = "/d";
        for (var depth = 0; depth <= SelectionImages.MaxDepth + 2; ++depth)
        {
            var child = path + "/d";
            map[path] = [Folder(child), File(path + "/pic.png")];
            path = child;
        }

        var result = await SelectionImages.CollectAsync([Folder("/d")], Tree(map));

        Assert.That(result.Paths, Has.Count.EqualTo(SelectionImages.MaxDepth + 1),
            "one picture per level, down to the depth limit and no further");
    }

    [Test]
    public async Task A_Folder_That_Cannot_Be_Listed_Is_Skipped_Rather_Than_Fatal()
    {
        var result = await SelectionImages.CollectAsync(
            [Folder("/denied"), File("/ok.png")],
            (path, _) => path == "/denied"
                ? throw new UnauthorizedAccessException()
                : Task.FromResult<IReadOnlyList<FileSystemEntry>>([]));

        Assert.That(result.Paths, Is.EqualTo(new[] { "/ok.png" }));
    }

    [Test]
    public void Cancelling_Mid_Walk_Stops_It()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(() => SelectionImages.CollectAsync(
            [Folder("/album")],
            Tree(new() { ["/album"] = [File("/album/a.png")] }),
            cts.Token));
    }

    [Test]
    public async Task A_Selection_With_No_Pictures_Reports_None()
    {
        var result = await SelectionImages.CollectAsync(
            [File("/a.txt"), File("/b.log")],
            Tree([]));

        Assert.Multiple(() =>
        {
            Assert.That(result.Paths, Is.Empty);
            Assert.That(result.Truncated, Is.False);
        });
    }

    /// <summary>
    /// A source tree is not a photo album, however many of its names an obscure raster format also
    /// claims.
    /// </summary>
    /// <remarks>
    /// Four picture formats are named after the four commonest things in a checkout: <c>.cs</c> is an
    /// Atari StarPainter screen, <c>.cpp</c> an Amstrad CPC Plus one, <c>.rs</c> a Sun raster and
    /// <c>.csv</c> a table of pixel values. Taking the name as evidence meant selecting a folder of
    /// repositories filled the gallery with several hundred source files and pushed every real
    /// photograph in the tree past the limit — which is how a folder came to preview as nothing at
    /// all.
    /// </remarks>
    [Test]
    public async Task A_Folder_Of_Source_Code_Offers_Only_Its_Actual_Pictures()
    {
        var result = await SelectionImages.CollectAsync(
            [Folder("/repo")],
            Tree(new()
            {
                ["/repo"] =
                [
                    File("/repo/Program.cs"),
                    File("/repo/native.cpp"),
                    File("/repo/lib.rs"),
                    File("/repo/data.csv"),
                    File("/repo/logo.png"),
                ],
            }));

        Assert.That(result.Paths, Is.EqualTo(new[] { "/repo/logo.png" }));
    }

    /// <summary>The limit is for pictures, so files that are not pictures cannot exhaust it.</summary>
    [Test]
    public async Task Source_Files_Do_Not_Crowd_Out_The_Pictures_Behind_Them()
    {
        var sources = Enumerable.Range(0, SelectionImages.MaxImages + 50)
            .Select(i => File($"/repo/src/File{i}.cs"))
            .ToArray();

        var result = await SelectionImages.CollectAsync(
            [Folder("/repo")],
            Tree(new()
            {
                ["/repo"] = [Folder("/repo/src"), Folder("/repo/docs")],
                ["/repo/src"] = sources,
                ["/repo/docs"] = [File("/repo/docs/screenshot.png")],
            }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Paths, Does.Contain("/repo/docs/screenshot.png"), "the picture is reached");
            Assert.That(result.Truncated, Is.False, "and nothing was dropped to reach it");
        });
    }
}
