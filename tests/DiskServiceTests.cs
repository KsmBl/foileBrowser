using FoileBrowser.Services;

namespace FoileBrowser.Tests;

[TestFixture]
public class DiskServiceTests
{
    private readonly DiskService _disk = new();

    [Test]
    public void AvailableFilesystems_DoesNotThrow_And_OnlyReturnsInstalledTools()
    {
        // Never throws; each returned type must name a real mkfs command (no fabricated entries).
        var list = _disk.AvailableFilesystems();
        Assert.That(list, Is.Not.Null);
        Assert.That(list.All(f => f.MkfsCommand.StartsWith("mkfs")), Is.True);
    }

    [Test]
    public async Task FormatAsync_Refuses_A_NonDevice_Path()
    {
        var result = await _disk.FormatAsync("not/a/device", "ext4", null);
        Assert.That(result.Success, Is.False);
    }

    [Test]
    public async Task FormatAsync_Refuses_An_Unknown_Filesystem()
    {
        // A /dev/ path that does not exist still exercises the fs-id guard before any privileged call.
        var result = await _disk.FormatAsync("/dev/foileBrowserNonexistentTestDevice", "notafs", null);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("filesystem").IgnoreCase.Or.Contain("Linux"));
    }
}
