using FoileBrowser.Models;
using FoileBrowser.Services;

namespace FoileBrowser.Tests;

[TestFixture]
public class FileSystemServiceTests
{
    private string _root = null!;
    private FileSystemService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "foile-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _service = new FileSystemService();
    }

    [TearDown]
    public void TearDown()
    {
        TempTree.Remove(_root);
    }

    [Test]
    public async Task ListDirectory_Returns_Files_And_Folders_With_Metadata()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        await File.WriteAllTextAsync(Path.Combine(_root, "hello.txt"), "hello world");

        var entries = await _service.ListDirectoryAsync(_root);

        var dir = entries.Single(e => e.Name == "sub");
        var file = entries.Single(e => e.Name == "hello.txt");

        Assert.Multiple(() =>
        {
            Assert.That(dir.Kind, Is.EqualTo(FileSystemEntryKind.Directory));
            Assert.That(dir.Size, Is.Null, "directory sizes are computed on demand later");
            Assert.That(file.Kind, Is.EqualTo(FileSystemEntryKind.File));
            Assert.That(file.Size, Is.EqualTo(11));
            Assert.That(file.Extension, Is.EqualTo("txt"));
            Assert.That(file.Modified, Is.Not.Null);
        });
    }

    [Test]
    public async Task ListDirectory_Flags_DotPrefixed_Entries_As_Hidden()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, ".config"), "x");

        var entries = await _service.ListDirectoryAsync(_root);

        Assert.That(entries.Single(e => e.Name == ".config").IsHidden, Is.True);
    }

    [Test]
    public void ListDirectory_Throws_For_Missing_Directory()
    {
        var missing = Path.Combine(_root, "does-not-exist");

        Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => _service.ListDirectoryAsync(missing));
    }

    [Test]
    public async Task ListDirectory_Honours_Cancellation()
    {
        for (var i = 0; i < 50; i++)
            await File.WriteAllTextAsync(Path.Combine(_root, $"f{i}.dat"), "x");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // TaskCanceledException derives from OperationCanceledException; CatchAsync matches subtypes.
        Assert.CatchAsync<OperationCanceledException>(
            () => _service.ListDirectoryAsync(_root, cts.Token));
    }

    [Test]
    public void GetParent_Returns_Parent_And_Null_At_Root()
    {
        var child = Path.Combine(_root, "child");
        Directory.CreateDirectory(child);

        Assert.That(_service.GetParent(child), Is.EqualTo(_root));

        var rootOfRoot = Path.GetPathRoot(_root)!;
        Assert.That(_service.GetParent(rootOfRoot), Is.Null, "a filesystem root has no parent");
    }

    [Test]
    public void DirectoryExists_Reflects_Reality()
    {
        Assert.That(_service.DirectoryExists(_root), Is.True);
        Assert.That(_service.DirectoryExists(Path.Combine(_root, "nope")), Is.False);
    }
}
