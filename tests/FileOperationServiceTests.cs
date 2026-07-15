using FoileBrowser.Models;
using FoileBrowser.Services;

namespace FoileBrowser.Tests;

[TestFixture]
public class FileOperationServiceTests
{
    private string _root = null!;
    private FileOperationService _ops = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "foile-ops-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _ops = new FileOperationService();
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string Src(string rel) => Path.Combine(_root, rel);

    // Auto-rename on any collision.
    private static ConflictResolution Rename(ConflictRequest _) => ConflictResolution.Rename;

    [Test]
    public async Task Copy_Duplicates_File_Into_Destination()
    {
        var srcDir = Directory.CreateDirectory(Src("a")).FullName;
        var dstDir = Directory.CreateDirectory(Src("b")).FullName;
        var file = Path.Combine(srcDir, "note.txt");
        await File.WriteAllTextAsync(file, "hello");

        await _ops.TransferAsync([file], dstDir, FileOperationKind.Copy, null, Rename);

        Assert.That(File.Exists(file), Is.True, "source remains after copy");
        Assert.That(await File.ReadAllTextAsync(Path.Combine(dstDir, "note.txt")), Is.EqualTo("hello"));
    }

    [Test]
    public async Task Move_Relocates_File()
    {
        var srcDir = Directory.CreateDirectory(Src("a")).FullName;
        var dstDir = Directory.CreateDirectory(Src("b")).FullName;
        var file = Path.Combine(srcDir, "note.txt");
        await File.WriteAllTextAsync(file, "data");

        await _ops.TransferAsync([file], dstDir, FileOperationKind.Move, null, Rename);

        Assert.That(File.Exists(file), Is.False, "source is gone after move");
        Assert.That(File.Exists(Path.Combine(dstDir, "note.txt")), Is.True);
    }

    [Test]
    public async Task Copy_Recurses_Into_Directories()
    {
        var srcDir = Directory.CreateDirectory(Src("tree")).FullName;
        Directory.CreateDirectory(Path.Combine(srcDir, "inner"));
        await File.WriteAllTextAsync(Path.Combine(srcDir, "inner", "deep.txt"), "x");
        var dstDir = Directory.CreateDirectory(Src("out")).FullName;

        await _ops.TransferAsync([srcDir], dstDir, FileOperationKind.Copy, null, Rename);

        Assert.That(File.Exists(Path.Combine(dstDir, "tree", "inner", "deep.txt")), Is.True);
    }

    [Test]
    public async Task Copy_Conflict_Rename_Keeps_Both()
    {
        var dstDir = Directory.CreateDirectory(Src("dst")).FullName;
        var file = Src("f.txt");
        await File.WriteAllTextAsync(file, "one");
        await File.WriteAllTextAsync(Path.Combine(dstDir, "f.txt"), "existing");

        await _ops.TransferAsync([file], dstDir, FileOperationKind.Copy, null, Rename);

        Assert.That(File.Exists(Path.Combine(dstDir, "f.txt")), Is.True);
        Assert.That(File.Exists(Path.Combine(dstDir, "f (2).txt")), Is.True, "renamed copy created");
    }

    [Test]
    public async Task Copy_Conflict_Skip_Leaves_Original()
    {
        var dstDir = Directory.CreateDirectory(Src("dst")).FullName;
        var file = Src("f.txt");
        await File.WriteAllTextAsync(file, "new");
        await File.WriteAllTextAsync(Path.Combine(dstDir, "f.txt"), "existing");

        await _ops.TransferAsync([file], dstDir, FileOperationKind.Copy, null, _ => ConflictResolution.Skip);

        Assert.That(await File.ReadAllTextAsync(Path.Combine(dstDir, "f.txt")), Is.EqualTo("existing"));
    }

    [Test]
    public void Transfer_Cancel_Resolution_Throws()
    {
        var dstDir = Directory.CreateDirectory(Src("dst")).FullName;
        var file = Src("f.txt");
        File.WriteAllText(file, "x");
        File.WriteAllText(Path.Combine(dstDir, "f.txt"), "y");

        Assert.CatchAsync<OperationCanceledException>(() =>
            _ops.TransferAsync([file], dstDir, FileOperationKind.Copy, null, _ => ConflictResolution.Cancel));
    }

    [Test]
    public async Task Reports_Progress_To_Completion()
    {
        var dstDir = Directory.CreateDirectory(Src("dst")).FullName;
        var file = Src("big.bin");
        await File.WriteAllBytesAsync(file, new byte[4096]);
        double last = 0;
        var progress = new Progress<OperationProgress>(p => last = Math.Max(last, p.Fraction));

        await _ops.TransferAsync([file], dstDir, FileOperationKind.Copy, progress, Rename);
        await Task.Delay(20); // let the last posted progress callback drain

        Assert.That(last, Is.GreaterThan(0));
    }

    [Test]
    public async Task CreateFolder_And_CreateFile_Make_Unique_Names()
    {
        var first = await _ops.CreateFolderAsync(_root, "New folder");
        var second = await _ops.CreateFolderAsync(_root, "New folder");

        Assert.That(Directory.Exists(first), Is.True);
        Assert.That(second, Is.Not.EqualTo(first));
        Assert.That(Directory.Exists(second), Is.True);

        var file = await _ops.CreateFileAsync(_root, "a.txt");
        Assert.That(File.Exists(file), Is.True);
    }

    [Test]
    public async Task Rename_Changes_Name()
    {
        var file = Src("old.txt");
        await File.WriteAllTextAsync(file, "x");

        var renamed = await _ops.RenameAsync(file, "new.txt");

        Assert.That(File.Exists(file), Is.False);
        Assert.That(renamed, Is.EqualTo(Src("new.txt")));
        Assert.That(File.Exists(renamed), Is.True);
    }

    [Test]
    public void Rename_To_Existing_Name_Throws()
    {
        var a = Src("a.txt");
        var b = Src("b.txt");
        File.WriteAllText(a, "1");
        File.WriteAllText(b, "2");

        Assert.CatchAsync<IOException>(() => _ops.RenameAsync(a, "b.txt"));
    }
}
