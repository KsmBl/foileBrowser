using FoileBrowser.Services;

namespace FoileBrowser.Tests;

[TestFixture]
public class TmpDim
{
    [Test]
    public async Task Probe()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dimprobe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var png = Path.Combine(dir, "a.png");
        await File.WriteAllBytesAsync(png, ImageFixture.Encode(".png", 40, 24));

        var svc = new MetadataService();
        var ready = new TaskCompletionSource();
        var first = svc.Get(png, "img.dimensions", () => ready.TrySetResult());
        Console.WriteLine($"PROBE first='{first}'");
        await Task.WhenAny(ready.Task, Task.Delay(4000));
        Console.WriteLine($"PROBE second='{svc.Get(png, "img.dimensions", () => { })}'");
        Console.WriteLine($"PROBE canDecodeExt(png)={ImageSupport.CanDecodeExtension("png")}");
        TempTree.Remove(dir);
    }
}
