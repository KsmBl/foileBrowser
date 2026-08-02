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
        TempTree.Remove(_root);
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

    // Both byte-moving strategies must be byte-exact, including partial final blocks and empty files.
    [TestCase(CopyStrategy.Overlapped)]
    [TestCase(CopyStrategy.Sequential)]
    public async Task Copy_Is_Byte_Exact_Across_Many_Blocks(CopyStrategy strategy)
    {
        // A tiny buffer forces many read/write cycles so the double-buffer swap and partial tail run.
        var ops = new FileOperationService(() => new CopyOptions
        {
            Strategy = strategy,
            BufferSize = 64,
            SequentialBufferSize = 64,
        });
        var dstDir = Directory.CreateDirectory(Src("dst")).FullName;

        var payload = new byte[64 * 10 + 7]; // not a whole multiple of the buffer
        Random.Shared.NextBytes(payload);
        var file = Src("blob.bin");
        await File.WriteAllBytesAsync(file, payload);

        await ops.TransferAsync([file], dstDir, FileOperationKind.Copy, null, Rename);

        Assert.That(await File.ReadAllBytesAsync(Path.Combine(dstDir, "blob.bin")), Is.EqualTo(payload));
    }

    [Test]
    public async Task Copy_Handles_Empty_File()
    {
        var dstDir = Directory.CreateDirectory(Src("dst")).FullName;
        var file = Src("empty.bin");
        await File.WriteAllBytesAsync(file, []);

        await _ops.TransferAsync([file], dstDir, FileOperationKind.Copy, null, Rename);

        Assert.That(new FileInfo(Path.Combine(dstDir, "empty.bin")).Length, Is.EqualTo(0));
    }

    [Test]
    public void DriveProfiler_Honours_Forced_Strategy()
    {
        var overlapped = DriveProfiler.Recommend("/a", "/b", new CopyOptions { Strategy = CopyStrategy.Overlapped });
        var sequential = DriveProfiler.Recommend("/a", "/b", new CopyOptions { Strategy = CopyStrategy.Sequential });

        Assert.That(overlapped, Is.EqualTo(CopyStrategy.Overlapped));
        Assert.That(sequential, Is.EqualTo(CopyStrategy.Sequential));
    }

    [Test]
    public void Settings_Map_To_Copy_Options()
    {
        var options = new Models.AppSettings
        {
            CopyBufferKiB = 512,
            SequentialBufferKiB = 4096,
            CopyStrategy = "Sequential",
        }.ToCopyOptions();

        Assert.That(options.BufferSize, Is.EqualTo(512 * 1024));
        Assert.That(options.SequentialBufferSize, Is.EqualTo(4096 * 1024));
        Assert.That(options.Strategy, Is.EqualTo(CopyStrategy.Sequential));
    }
}
