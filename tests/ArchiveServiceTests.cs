using System.Formats.Tar;
using System.IO.Compression;
using FoileBrowser.Services;

namespace FoileBrowser.Tests;

[TestFixture]
public class ArchiveServiceTests
{
    private string _root = null!;
    private ArchiveService _archives = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "foile-arc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _archives = new ArchiveService();
    }

    [TearDown]
    public void TearDown()
    {
        TempTree.Remove(_root);
    }

    private string MakeZip()
    {
        var zip = Path.Combine(_root, "sample.zip");
        using var z = ZipFile.Open(zip, ZipArchiveMode.Create);
        using (var w = new StreamWriter(z.CreateEntry("hello.txt").Open())) w.Write("hello world");
        using (var w = new StreamWriter(z.CreateEntry("sub/deep.txt").Open())) w.Write("deep content");
        return zip;
    }

    [Test]
    public void SourceGenerator_Statically_Registered_The_Formats()
    {
        // The compile-time generator replaces reflective discovery; it should find the descriptors
        // across the FileFormat.*/FileSystem.*/Compression.* assemblies (no Assembly.LoadFrom, AOT-safe).
        Assert.That(FoileBrowser.Generated.GeneratedFormats.Count, Is.GreaterThan(100));
    }

    [Test]
    public void IsArchive_Recognises_Zip_And_Rejects_Text()
    {
        Assert.That(_archives.IsArchive("/x/file.zip"), Is.True);
        Assert.That(_archives.IsArchive("/x/notes.txt"), Is.False);
    }

    [Test]
    public void Identify_Names_The_Format()
    {
        Assert.That(_archives.Identify("/x/file.zip"), Is.Not.Null.And.Contain("ZIP").IgnoreCase);
    }

    [Test]
    public async Task List_Returns_Archive_Entries()
    {
        var zip = MakeZip();

        var entries = await _archives.ListAsync(zip);

        Assert.That(entries.Select(e => e.Name), Does.Contain("hello.txt"));
        var hello = entries.First(e => e.Name == "hello.txt");
        Assert.That(hello.Size, Is.EqualTo(11));
        Assert.That(hello.IsDirectory, Is.False);
    }

    [Test]
    public async Task ExtractEntry_Writes_A_Single_File_Streamed()
    {
        var zip = MakeZip();
        var dest = Path.Combine(_root, "out", "deep.txt");

        await _archives.ExtractEntryAsync(zip, "sub/deep.txt", dest);

        Assert.That(File.Exists(dest), Is.True);
        Assert.That(await File.ReadAllTextAsync(dest), Is.EqualTo("deep content"));
    }

    [Test]
    public async Task ExtractAll_Writes_Files_To_Disk()
    {
        var zip = MakeZip();
        var dest = Path.Combine(_root, "out");

        await _archives.ExtractAllAsync(zip, dest);

        Assert.That(await File.ReadAllTextAsync(Path.Combine(dest, "hello.txt")), Is.EqualTo("hello world"));
        Assert.That(File.Exists(Path.Combine(dest, "sub", "deep.txt")), Is.True);
    }

    [Test]
    public void Listing_A_NonArchive_Throws()
    {
        Assert.ThrowsAsync<NotSupportedException>(() => _archives.ListAsync("/x/plain.txt"));
    }
    // ---- compressed tarballs -------------------------------------------------------------------

    /// <summary>Writes a tar holding the same two entries as the zip, through an optional wrapper.</summary>
    private string MakeTarball(string name, Func<Stream, Stream>? compress = null)
    {
        var path = Path.Combine(_root, name);
        using (var file = File.Create(path))
        {
            var outer = compress?.Invoke(file) ?? file;
            try
            {
                using var tar = new TarWriter(outer, leaveOpen: true);
                Write(tar, "hello.txt", "hello world");
                Write(tar, "sub/deep.txt", "deep content");
            }
            finally
            {
                if (!ReferenceEquals(outer, file))
                    outer.Dispose();
            }
        }

        return path;

        static void Write(TarWriter tar, string name, string content)
            => tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name)
            {
                DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)),
            });
    }

    /// <summary>
    /// A tarball is one file to a person and two formats to a registry, and the registry's answer was
    /// the one that reached the UI.
    /// </summary>
    /// <remarks>
    /// gzip, bzip2 and xz compress a single unnamed stream and so cannot list anything, which is true
    /// of the format and useless for the file: on Unix a tarball is how software ships. Only the bare
    /// <c>.tar</c> opened, and that is the rarer thing to actually have — so the whole family read as
    /// "tar files do not open".
    /// </remarks>
    [TestCase("sample.tar", null)]
    [TestCase("sample.tar.gz", "gz")]
    [TestCase("sample.tgz", "gz")]
    public async Task A_Tarball_Is_Entered_And_Lists_What_Is_In_It(string name, string? compression)
    {
        var path = MakeTarball(name, Wrap(compression));

        Assert.That(_archives.IsArchive(path), Is.True, $"{name} is enterable");

        var entries = await _archives.ListAsync(path);

        Assert.That(
            entries.Where(e => !e.IsDirectory).Select(e => e.Name),
            Is.EquivalentTo(new[] { "hello.txt", "sub/deep.txt" }));
    }

    [TestCase("sample.tar", null)]
    [TestCase("sample.tar.gz", "gz")]
    public async Task A_Tarballs_Entry_Extracts_Its_Contents(string name, string? compression)
    {
        var path = MakeTarball(name, Wrap(compression));
        var dest = Path.Combine(_root, "out", "hello.txt");

        await _archives.ExtractEntryAsync(path, "hello.txt", dest);

        Assert.That(await File.ReadAllTextAsync(dest), Is.EqualTo("hello world"));
    }

    /// <summary>The contraction names no format of its own, so it reports what it holds.</summary>
    [Test]
    public void A_Tgz_Says_What_It_Is()
        => Assert.That(_archives.Identify(MakeTarball("sample.tgz", Wrap("gz"))), Does.Contain("tar"));

    private static Func<Stream, Stream>? Wrap(string? compression) => compression switch
    {
        "gz" => s => new GZipStream(s, CompressionLevel.Fastest, leaveOpen: true),
        _ => null,
    };
}
