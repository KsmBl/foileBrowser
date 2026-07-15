using FoileBrowser.Models;
using FoileBrowser.Services;
using FoileBrowser.ViewModels;

namespace FoileBrowser.Tests;

[TestFixture]
public class OperationQueueViewModelTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "foile-queue-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Test]
    public async Task Enqueued_Copy_Runs_And_Completes()
    {
        var queue = new OperationQueueViewModel(new FileOperationService());
        var srcDir = Directory.CreateDirectory(Path.Combine(_root, "src")).FullName;
        var dstDir = Directory.CreateDirectory(Path.Combine(_root, "dst")).FullName;
        var file = Path.Combine(srcDir, "x.txt");
        await File.WriteAllTextAsync(file, "payload");

        var op = queue.Enqueue(FileOperationKind.Copy, [file], dstDir);
        await op.Completion;

        Assert.That(op.Status, Is.EqualTo(OperationStatus.Completed));
        Assert.That(op.Progress, Is.EqualTo(1));
        Assert.That(File.Exists(Path.Combine(dstDir, "x.txt")), Is.True);
    }

    [Test]
    public async Task Multiple_Operations_Run_Sequentially()
    {
        var queue = new OperationQueueViewModel(new FileOperationService());
        var dstDir = Directory.CreateDirectory(Path.Combine(_root, "dst")).FullName;
        var f1 = Path.Combine(_root, "a.txt");
        var f2 = Path.Combine(_root, "b.txt");
        await File.WriteAllTextAsync(f1, "1");
        await File.WriteAllTextAsync(f2, "2");

        var op1 = queue.Enqueue(FileOperationKind.Copy, [f1], dstDir);
        var op2 = queue.Enqueue(FileOperationKind.Copy, [f2], dstDir);
        await Task.WhenAll(op1.Completion, op2.Completion);

        Assert.That(queue.Operations, Has.Count.EqualTo(2));
        Assert.That(File.Exists(Path.Combine(dstDir, "a.txt")), Is.True);
        Assert.That(File.Exists(Path.Combine(dstDir, "b.txt")), Is.True);
        Assert.That(queue.ActiveCount, Is.EqualTo(0));
    }

    [Test]
    public async Task Failed_Operation_Is_Marked_Failed()
    {
        var queue = new OperationQueueViewModel(new FileOperationService());
        var missing = Path.Combine(_root, "does-not-exist.txt");
        var dstDir = Directory.CreateDirectory(Path.Combine(_root, "dst")).FullName;

        var op = queue.Enqueue(FileOperationKind.Copy, [missing], dstDir);
        await op.Completion;

        Assert.That(op.Status, Is.EqualTo(OperationStatus.Failed));
        Assert.That(op.ErrorMessage, Is.Not.Null);
    }
}
