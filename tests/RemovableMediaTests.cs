using FoileBrowser.Models;
using FoileBrowser.Services;
using FoileBrowser.ViewModels;

namespace FoileBrowser.Tests;

/// <summary>
/// Mounting and unmounting removable media from the sidebar (PRD §6.10) — the point being that
/// neither needs a terminal.
/// </summary>
[TestFixture]
public class RemovableMediaTests
{
    private sealed class RecordingDevice : IDeviceService
    {
        internal List<string> Mounted { get; } = [];
        internal List<string> Ejected { get; } = [];

        /// <summary>Where a mount lands, or null to act like a device that refused.</summary>
        internal string? MountPoint { get; set; }

        public Task EjectAsync(string mountPath, CancellationToken cancellationToken = default)
        {
            Ejected.Add(mountPath);
            return Task.CompletedTask;
        }

        public Task<string?> MountAsync(string device, CancellationToken cancellationToken = default)
        {
            Mounted.Add(device);
            return Task.FromResult(MountPoint);
        }
    }

    private string _settingsDir = null!;
    private string _settingsFile = null!;

    [SetUp]
    public void SetUp()
    {
        _settingsDir = Path.Combine(Path.GetTempPath(), "foile-media-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_settingsDir);
        _settingsFile = Path.Combine(_settingsDir, "settings.json");
    }

    [TearDown]
    public void TearDown()
    {
        _shells.DisposeAll();
        TempTree.Remove(_settingsDir);
    }

    private readonly ShellTracker _shells = new();

    private MainWindowViewModel CreateShell(FakeFileSystem fs, IDeviceService device)
    {
        var settings = new SettingsService(_settingsFile);
        return _shells.Track(new MainWindowViewModel(fs, new FileOperationService(), new RecordingTrash(),
            new SearchService(), new PreviewService(), settings, new TagService(settings), new ShellService(),
            device: device));
    }

    private static DriveVolume Unmounted(string device, string label) => new()
    {
        Label = label,
        RootPath = string.Empty,
        Device = device,
        Disk = "sdb",
        Kind = VolumeKind.Removable,
        TotalBytes = 16_000_000_000,
        IsMounted = false,
    };

    private static SidebarItemViewModel RowFor(MainWindowViewModel vm, string label)
        => vm.Sections.SelectMany(s => s.Items).Single(i => i.Name.Contains(label, StringComparison.Ordinal));

    [Test]
    public async Task An_Unmounted_Stick_Is_Listed_So_There_Is_Something_To_Click()
    {
        var fs = new FakeFileSystem();
        fs.Volumes.Add(Unmounted("/dev/sdb1", "BACKUP"));
        var vm = CreateShell(fs, new RecordingDevice());

        await vm.InitializeAsync();

        var row = RowFor(vm, "BACKUP");
        Assert.Multiple(() =>
        {
            Assert.That(row.NeedsMounting, Is.True);
            Assert.That(row.Device, Is.EqualTo("/dev/sdb1"));
            Assert.That(row.HasCapacity, Is.False, "no free-space bar to fill without a mount");
            Assert.That(row.IsEjectable, Is.False, "nothing to eject yet");
        });
    }

    [Test]
    public async Task Opening_An_Unmounted_Stick_Mounts_It_And_Browses_It()
    {
        var landed = Directory.CreateDirectory(Path.Combine(_settingsDir, "media")).FullName;
        var fs = new FakeFileSystem();
        fs.Volumes.Add(Unmounted("/dev/sdb1", "BACKUP"));
        var device = new RecordingDevice { MountPoint = landed };
        var vm = CreateShell(fs, device);
        await vm.InitializeAsync();

        await vm.OpenSidebarItemCommand.ExecuteAsync(RowFor(vm, "BACKUP"));

        Assert.Multiple(() =>
        {
            Assert.That(device.Mounted, Is.EqualTo(new[] { "/dev/sdb1" }));
            Assert.That(vm.ActiveTab!.CurrentPath, Is.EqualTo(landed), "and lands in it");
        });
    }

    [Test]
    public async Task A_Device_That_Refuses_To_Mount_Says_So_Instead_Of_Doing_Nothing()
    {
        var fs = new FakeFileSystem();
        fs.Volumes.Add(Unmounted("/dev/sdb1", "BACKUP"));
        var device = new RecordingDevice { MountPoint = null }; // no filesystem, or polkit said no
        var vm = CreateShell(fs, device);
        await vm.InitializeAsync();
        var before = vm.ActiveTab!.CurrentPath;

        await vm.OpenSidebarItemCommand.ExecuteAsync(RowFor(vm, "BACKUP"));

        Assert.Multiple(() =>
        {
            Assert.That(device.Mounted, Is.Not.Empty, "it was attempted");
            Assert.That(vm.ActiveTab!.CurrentPath, Is.EqualTo(before), "and did not navigate anywhere");
            Assert.That(vm.ActiveTab!.StatusText, Does.Contain("Could not mount"));
        });
    }

    [Test]
    public async Task A_Mounted_Volume_Is_Opened_Rather_Than_Mounted_Again()
    {
        var mounted = Directory.CreateDirectory(Path.Combine(_settingsDir, "already")).FullName;
        var fs = new FakeFileSystem();
        fs.Volumes.Add(new DriveVolume
        {
            Label = "STICK",
            RootPath = mounted,
            Device = "/dev/sdc1",
            Kind = VolumeKind.Removable,
            FreeBytes = 1,
            TotalBytes = 2,
        });
        var device = new RecordingDevice();
        var vm = CreateShell(fs, device);
        await vm.InitializeAsync();

        await vm.OpenSidebarItemCommand.ExecuteAsync(RowFor(vm, "STICK"));

        Assert.Multiple(() =>
        {
            Assert.That(device.Mounted, Is.Empty);
            Assert.That(vm.ActiveTab!.CurrentPath, Is.EqualTo(mounted));
        });
    }

    [Test]
    public async Task A_Mounted_Removable_Can_Still_Be_Ejected()
    {
        var mounted = Directory.CreateDirectory(Path.Combine(_settingsDir, "ejectme")).FullName;
        var fs = new FakeFileSystem();
        fs.Volumes.Add(new DriveVolume
        {
            Label = "STICK",
            RootPath = mounted,
            Device = "/dev/sdc1",
            Kind = VolumeKind.Removable,
            FreeBytes = 1,
            TotalBytes = 2,
        });
        var device = new RecordingDevice();
        var vm = CreateShell(fs, device);
        await vm.InitializeAsync();

        var row = RowFor(vm, "STICK");
        Assert.That(row.IsEjectable, Is.True);
        await vm.EjectCommand.ExecuteAsync(row);

        Assert.That(device.Ejected, Is.EqualTo(new[] { mounted }));
    }
}
