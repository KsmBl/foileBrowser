using FoileBrowser.Services;

namespace FoileBrowser.Tests;

[TestFixture]
public class DirectorySizeServiceTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "foile-size-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Test]
    public async Task Sums_Files_Recursively()
    {
        await File.WriteAllBytesAsync(Path.Combine(_root, "a.bin"), new byte[100]);
        var sub = Directory.CreateDirectory(Path.Combine(_root, "sub")).FullName;
        await File.WriteAllBytesAsync(Path.Combine(sub, "b.bin"), new byte[250]);

        var service = new DirectorySizeService();
        var size = await service.GetSizeAsync(_root);

        Assert.That(size, Is.EqualTo(350));
    }

    [Test]
    public async Task Computing_A_Folder_Also_Caches_Its_Subfolders()
    {
        var sub = Directory.CreateDirectory(Path.Combine(_root, "sub")).FullName;
        await File.WriteAllBytesAsync(Path.Combine(sub, "b.bin"), new byte[250]);
        var service = new DirectorySizeService();

        await service.GetSizeAsync(_root);

        // Drilling into "sub" should be instant — its size was cached during the parent walk.
        Assert.That(service.TryGetCached(sub, out var size), Is.True);
        Assert.That(size, Is.EqualTo(250));
    }

    [Test]
    public async Task Result_Is_Cached_For_Instant_Reuse()
    {
        await File.WriteAllBytesAsync(Path.Combine(_root, "a.bin"), new byte[64]);
        var service = new DirectorySizeService();

        Assert.That(service.TryGetCached(_root, out _), Is.False);
        var size = await service.GetSizeAsync(_root);

        Assert.That(service.TryGetCached(_root, out var cached), Is.True);
        Assert.That(cached, Is.EqualTo(size));
    }

    [Test]
    public async Task Reports_Progress()
    {
        for (var i = 0; i < 3; i++)
            await File.WriteAllBytesAsync(Path.Combine(_root, $"f{i}.bin"), new byte[10]);

        var service = new DirectorySizeService();
        long lastReported = 0;
        var progress = new Progress<long>(v => lastReported = Math.Max(lastReported, v));

        var size = await service.GetSizeAsync(_root, progress);
        await Task.Delay(20); // let the final posted progress callback drain

        Assert.That(size, Is.EqualTo(30));
        Assert.That(lastReported, Is.EqualTo(30));
    }

    [Test]
    public void Cancellation_Is_Observed()
    {
        var service = new DirectorySizeService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.CatchAsync<OperationCanceledException>(() => service.GetSizeAsync(_root, null, cts.Token));
    }
}
