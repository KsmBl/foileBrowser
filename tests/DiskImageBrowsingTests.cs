using System.Diagnostics;
using FoileBrowser.Services;
using FoileBrowser.ViewModels;

namespace FoileBrowser.Tests;

/// <summary>
/// Disk and filesystem images browsed the way archives are (PRD §6.11) — an ISO, a FAT volume or a
/// SquashFS is a container of files, and there is no reason opening one should feel different from
/// opening a ZIP.
/// </summary>
/// <remarks>
/// The images are built here with the system's own tools rather than checked in, so what is being
/// read is a real image some other program wrote. A test is ignored rather than failed where the
/// tool that makes its format is not installed.
/// </remarks>
[TestFixture]
public class DiskImageBrowsingTests
{
    private string _root = null!;
    private string _source = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "foile-images-" + Guid.NewGuid().ToString("N"));
        _source = Directory.CreateDirectory(Path.Combine(_root, "src", "sub")).FullName;
        System.IO.File.WriteAllText(Path.Combine(_root, "src", "hello.txt"), "hello from the image");
        System.IO.File.WriteAllText(Path.Combine(_source, "deep.txt"), "deeper");
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static bool Run(string tool, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(tool) { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in args)
                psi.ArgumentList.Add(a);

            using var process = Process.Start(psi);
            if (process is null)
                return false;

            process.WaitForExit(30_000);
            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>An ISO9660 image with Rock Ridge/Joliet names, as any CD burner would write.</summary>
    private string MakeIso()
    {
        var iso = Path.Combine(_root, "test.iso");
        if (!Run("genisoimage", "-quiet", "-o", iso, "-J", "-r", Path.Combine(_root, "src"))
            && !Run("mkisofs", "-quiet", "-o", iso, "-J", "-r", Path.Combine(_root, "src")))
            Assert.Ignore("no genisoimage/mkisofs on this machine");

        return iso;
    }

    private string MakeSquashFs()
    {
        var image = Path.Combine(_root, "test.squashfs");
        if (!Run("mksquashfs", Path.Combine(_root, "src"), image, "-quiet", "-no-progress"))
            Assert.Ignore("no mksquashfs on this machine");

        return image;
    }

    private static FileEntryViewModel Entry(string path) =>
        new(new Models.FileSystemEntry
        {
            Name = Path.GetFileName(path),
            FullPath = path,
            Kind = Models.FileSystemEntryKind.File,
        });

    // ---- what the format registry claims ----

    [Test]
    public void An_Iso_Is_Recognised_As_Something_That_Can_Be_Listed()
    {
        var iso = MakeIso();
        var archives = new ArchiveService();

        Assert.Multiple(() =>
        {
            Assert.That(archives.Identify(iso), Is.Not.Null, "the registry knows the format");
            Assert.That(archives.IsArchive(iso), Is.True, "and can list it, so it opens like an archive");
        });
    }

    // ---- browsing one ----

    [Test]
    public async Task An_Iso_Opens_As_A_Folder_And_Lists_What_Is_In_It()
    {
        var iso = MakeIso();
        var tab = new FileTabViewModel(new FileSystemService());
        await tab.NavigateToAsync(_root);

        await tab.OpenCommand.ExecuteAsync(Entry(iso));

        var names = tab.Entries.Select(e => e.Name).ToList();
        Assert.That(names, Does.Contain("hello.txt"));
        Assert.That(names, Does.Contain("sub"));
    }

    [Test]
    public async Task A_Folder_Inside_An_Iso_Can_Be_Entered()
    {
        var iso = MakeIso();
        var tab = new FileTabViewModel(new FileSystemService());
        await tab.NavigateToAsync(_root);
        await tab.OpenCommand.ExecuteAsync(Entry(iso));

        await tab.OpenCommand.ExecuteAsync(tab.Entries.Single(e => e.Name == "sub"));

        Assert.That(tab.Entries.Select(e => e.Name), Does.Contain("deep.txt"));
    }

    [Test]
    public async Task An_Images_Crumbs_Show_The_Real_Path_It_Lives_At()
    {
        // The same rule archives follow: the trail runs through the image file, so clicking a folder
        // crumb gets you back out of it.
        var iso = MakeIso();
        var tab = new FileTabViewModel(new FileSystemService());
        await tab.NavigateToAsync(_root);
        await tab.OpenCommand.ExecuteAsync(Entry(iso));

        var names = tab.Breadcrumbs.Select(c => c.Name).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(names, Does.Contain("test.iso"));
            Assert.That(names[0], Is.EqualTo("/"), "and the real folders above it");
        });
    }

    [Test]
    public async Task A_SquashFs_Opens_The_Same_Way()
    {
        var image = MakeSquashFs();
        var tab = new FileTabViewModel(new FileSystemService());
        await tab.NavigateToAsync(_root);

        await tab.OpenCommand.ExecuteAsync(Entry(image));

        Assert.That(tab.Entries.Select(e => e.Name), Does.Contain("hello.txt"));
    }

    [Test]
    public async Task A_File_Inside_An_Image_Can_Be_Read_Out_Of_It()
    {
        var iso = MakeIso();
        var archives = new ArchiveService();
        var entries = await archives.ListAsync(iso);
        var hello = entries.Single(e => e.Name.EndsWith("hello.txt", StringComparison.OrdinalIgnoreCase));

        var destination = Path.Combine(_root, "extracted.txt");
        await archives.ExtractEntryAsync(iso, hello.Name, destination);

        Assert.That(await System.IO.File.ReadAllTextAsync(destination), Does.Contain("hello from the image"));
    }

    /// <summary>An ext4 volume populated at creation, which is what mke2fs's -d does.</summary>
    private string MakeExt4()
    {
        var image = Path.Combine(_root, "test.ext4");
        Fill(image, 8);
        if (!Run("mkfs.ext4", "-q", "-F", "-d", Path.Combine(_root, "src"), image))
            Assert.Ignore("no mkfs.ext4 on this machine");

        return image;
    }

    /// <summary>A FAT volume, filled with mtools so nothing here needs to mount anything.</summary>
    private string MakeFat()
    {
        var image = Path.Combine(_root, "test.fat");
        Fill(image, 8);
        if (!Run("mkfs.vfat", "-n", "TESTFAT", image))
            Assert.Ignore("no mkfs.vfat on this machine");
        if (!Run("mcopy", "-i", image, "-s", Path.Combine(_root, "src", "hello.txt"),
                 Path.Combine(_root, "src", "sub"), "::"))
            Assert.Ignore("no mcopy (mtools) on this machine");

        return image;
    }

    /// <summary>An empty file of the given size in MiB, for a filesystem to be laid down on.</summary>
    private static void Fill(string path, int megabytes)
    {
        using var file = System.IO.File.Create(path);
        file.SetLength(megabytes * 1024L * 1024L);
    }

    [Test]
    public async Task An_Ext4_Volume_Lists_What_Is_In_It()
    {
        var image = MakeExt4();
        var tab = new FileTabViewModel(new FileSystemService());
        await tab.NavigateToAsync(_root);

        await tab.OpenCommand.ExecuteAsync(Entry(image));

        Assert.That(tab.Entries.Select(e => e.Name), Does.Contain("hello.txt"));
    }

    [Test]
    public async Task A_Fat_Volume_Lists_What_Is_In_It()
    {
        var image = MakeFat();
        var tab = new FileTabViewModel(new FileSystemService());
        await tab.NavigateToAsync(_root);

        await tab.OpenCommand.ExecuteAsync(Entry(image));

        Assert.That(tab.Entries.Select(e => e.Name), Does.Contain("hello.txt"));
    }

    [Test]
    public async Task A_Freshly_Formatted_Volume_Opens_To_Nothing_Rather_Than_Failing()
    {
        // An empty filesystem is a legitimate thing to open; it should read as an empty folder.
        var image = Path.Combine(_root, "empty.fat");
        Fill(image, 8);
        if (!Run("mkfs.vfat", "-n", "EMPTY", image))
            Assert.Ignore("no mkfs.vfat on this machine");

        var tab = new FileTabViewModel(new FileSystemService());
        await tab.NavigateToAsync(_root);

        await tab.OpenCommand.ExecuteAsync(Entry(image));

        Assert.That(tab.Entries, Is.Empty);
    }
}
