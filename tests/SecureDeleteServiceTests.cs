using FoileBrowser.Services;

namespace FoileBrowser.Tests;

[TestFixture]
public class SecureDeleteServiceTests
{
    private string _dir = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "foile-shred-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        TempTree.Remove(_dir);
    }

    [Test]
    public async Task Overwrites_The_Contents_With_Zeroes_Before_Deleting()
    {
        // Keep a hard link so the file's data survives the unlink and can be inspected afterwards.
        var path = Path.Combine(_dir, "secret.bin");
        var payload = new byte[4096];
        Random.Shared.NextBytes(payload);
        File.WriteAllBytes(path, payload);

        var link = Path.Combine(_dir, "link.bin");
        HardLink(path, link);

        await new SecureDeleteService().ShredAsync(path);

        Assert.That(File.Exists(path), Is.False, "the original name is gone");
        Assert.That(File.ReadAllBytes(link), Is.All.Zero, "the data itself was overwritten");
    }

    [Test]
    public async Task Reports_Cumulative_Progress_Across_A_Whole_Tree()
    {
        var tree = Path.Combine(_dir, "tree");
        Directory.CreateDirectory(Path.Combine(tree, "nested"));
        File.WriteAllBytes(Path.Combine(tree, "a.bin"), new byte[1000]);
        File.WriteAllBytes(Path.Combine(tree, "nested", "b.bin"), new byte[2000]);

        var reports = new SyncProgress();
        await new SecureDeleteService().ShredAsync(tree, reports);

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(tree), Is.False, "the whole tree is gone");
            Assert.That(reports.Values, Is.Ordered, "progress only ever moves forwards");
            Assert.That(reports.Values.LastOrDefault(), Is.EqualTo(3000),
                "the final report is the total bytes overwritten across both files");
        });
    }

    /// <summary>Captures progress on the reporting thread, so assertions don't race the callback.</summary>
    private sealed class SyncProgress : IProgress<long>
    {
        public List<long> Values { get; } = [];

        public void Report(long value) => Values.Add(value);
    }

    [Test]
    public async Task Deletes_A_Whole_Directory_Tree()
    {
        var sub = Path.Combine(_dir, "a", "b");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "deep.txt"), "content");
        File.WriteAllText(Path.Combine(_dir, "a", "shallow.txt"), "content");

        await new SecureDeleteService().ShredAsync(Path.Combine(_dir, "a"));

        Assert.That(Directory.Exists(Path.Combine(_dir, "a")), Is.False);
    }

    [Test]
    public async Task Unlinks_A_Symlink_Without_Touching_Its_Target()
    {
        var target = Path.Combine(_dir, "target.txt");
        File.WriteAllText(target, "keep me");
        var link = Path.Combine(_dir, "link.txt");
        File.CreateSymbolicLink(link, target);

        await new SecureDeleteService().ShredAsync(link);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(link), Is.False, "the link is gone");
            Assert.That(File.ReadAllText(target), Is.EqualTo("keep me"), "the target is untouched");
        });
    }

    [Test]
    public async Task Overwrites_A_Read_Only_File_Rather_Than_Skipping_It()
    {
        var path = Path.Combine(_dir, "locked.bin");
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        var link = Path.Combine(_dir, "locked-link.bin");
        HardLink(path, link);
        File.SetAttributes(path, FileAttributes.ReadOnly);

        await new SecureDeleteService().ShredAsync(path);

        Assert.That(File.Exists(path), Is.False);
        Assert.That(File.ReadAllBytes(link), Is.All.Zero);
    }

    [Test]
    public void Missing_Paths_Are_A_No_Op()
    {
        Assert.DoesNotThrowAsync(() =>
            new SecureDeleteService().ShredAsync(Path.Combine(_dir, "does-not-exist")));
    }

    /// <summary>Creates a hard link so a file's data outlives the unlink and stays inspectable.</summary>
    private static void HardLink(string source, string destination)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            Assert.Ignore("hard links are created via link(2) in this test");
        var psi = new System.Diagnostics.ProcessStartInfo("ln") { UseShellExecute = false };
        psi.ArgumentList.Add(source);
        psi.ArgumentList.Add(destination);
        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.WaitForExit();
        Assert.That(proc.ExitCode, Is.Zero, "could not create the hard link the test needs");
    }
}
