using FoileBrowser.Services;

namespace FoileBrowser.Tests;

[TestFixture]
public class SearchServiceTests
{
    private string _root = null!;
    private SearchService _search = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "foile-search-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "sub", "deep"));
        File.WriteAllText(Path.Combine(_root, "readme.md"), "x");
        File.WriteAllText(Path.Combine(_root, "sub", "notes.txt"), "x");
        File.WriteAllText(Path.Combine(_root, "sub", "deep", "report.md"), "x");
        File.WriteAllText(Path.Combine(_root, "sub", "deep", "image.png"), "x");
        _search = new SearchService();
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private async Task<List<string>> CollectAsync(string query, string[]? exts = null)
    {
        var names = new List<string>();
        await foreach (var hit in _search.SearchAsync(_root, query, exts))
            names.Add(hit.Name);
        return names;
    }

    [Test]
    public async Task Search_Finds_Matches_Recursively()
    {
        var names = await CollectAsync("md");

        Assert.That(names, Does.Contain("readme.md"));
        Assert.That(names, Does.Contain("report.md"), "descends into nested directories");
    }

    [Test]
    public async Task Empty_Query_With_Extension_Filter_Returns_All_Of_That_Type()
    {
        // Extension-only search: no name query, just a type filter.
        var names = await CollectAsync("", ["md"]);

        Assert.That(names, Is.EquivalentTo(new[] { "readme.md", "report.md" }));
        Assert.That(names, Does.Not.Contain("notes.txt"));
        Assert.That(names, Does.Not.Contain("image.png"));
    }

    [Test]
    public async Task Extension_Filter_Limits_Results_To_Files()
    {
        var names = await CollectAsync("", ["png"]);

        Assert.That(names, Is.EquivalentTo(new[] { "image.png" }));
    }

    [Test]
    public async Task Query_Fuzzy_Matches_Names()
    {
        var names = await CollectAsync("rprt"); // subsequence of "report"

        Assert.That(names, Does.Contain("report.md"));
        Assert.That(names, Does.Not.Contain("notes.txt"));
    }

    [Test]
    public void Search_Honours_Cancellation()
    {
        Assert.CatchAsync<OperationCanceledException>(async () =>
        {
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();
            await foreach (var _ in _search.SearchAsync(_root, "md", null, cts.Token))
            {
            }
        });
    }

    [Test]
    public async Task Missing_Root_Yields_Nothing()
    {
        var names = new List<string>();
        await foreach (var hit in _search.SearchAsync(Path.Combine(_root, "nope"), "x"))
            names.Add(hit.Name);

        Assert.That(names, Is.Empty);
    }
}
