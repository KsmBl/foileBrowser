using FoileBrowser.Services;

namespace FoileBrowser.Tests;

[TestFixture]
public class TmpThumb
{
    [Test]
    public async Task Probe()
    {
        var dir = Path.Combine(Path.GetTempPath(), "thumbprobe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var paths = new List<string>();
        for (var i = 0; i < 3; ++i)
        {
            var p = Path.Combine(dir, $"p{i}.png");
            await File.WriteAllBytesAsync(p, ImageFixture.Encode(".png", 120, 90));
            paths.Add(p);
        }

        var svc = new ThumbnailService();
        var ready = new List<string>();
        svc.Ready += (_, p) => { lock (ready) ready.Add(p); };

        foreach (var p in paths)
            Console.WriteLine($"PROBE canRender={ThumbnailService.CanRender(p)} first={svc.Get(p) is not null}");

        for (var i = 0; i < 50 && ready.Count < 3; ++i) await Task.Delay(100);

        Console.WriteLine($"PROBE readyCount={ready.Count}");
        foreach (var p in paths)
            Console.WriteLine($"PROBE second={svc.Get(p) is not null}");

        TempTree.Remove(dir);
    }
}
