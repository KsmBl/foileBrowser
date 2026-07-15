using FoileBrowser.Models;
using FoileBrowser.Services;

namespace FoileBrowser.Tests;

[TestFixture]
public class VolumeListingTests
{
    [Test]
    public async Task ListVolumes_Includes_Root_And_Excludes_Pseudo_Filesystems()
    {
        var volumes = await new FileSystemService().ListVolumesAsync();

        Assert.That(volumes, Is.Not.Empty);
        if (!OperatingSystem.IsWindows())
        {
            Assert.That(volumes.Any(v => v.RootPath == "/"), Is.True, "the root filesystem should be listed");
            Assert.That(volumes.Any(v => v.RootPath.StartsWith("/proc")), Is.False, "pseudo filesystems are hidden");
            Assert.That(volumes.Any(v => v.RootPath.StartsWith("/sys/")), Is.False);
        }
    }

    [Test]
    public void DriveVolume_IsRemovable_Reflects_Kind()
    {
        Assert.That(new DriveVolume { Label = "d", RootPath = "/", Kind = VolumeKind.Fixed }.IsRemovable, Is.False);
        Assert.That(new DriveVolume { Label = "usb", RootPath = "/media/x", Kind = VolumeKind.Removable }.IsRemovable, Is.True);
        Assert.That(new DriveVolume { Label = "phone", RootPath = "/gvfs/x", Kind = VolumeKind.Gvfs }.IsRemovable, Is.True);
    }
}
