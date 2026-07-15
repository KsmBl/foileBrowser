using FoileBrowser.Services;

namespace FoileBrowser.Tests;

[TestFixture]
public class FuzzyMatcherTests
{
    [Test]
    public void Empty_Pattern_Matches_Everything()
    {
        Assert.That(FuzzyMatcher.IsMatch("", "anything.txt"), Is.True);
    }

    [TestCase("rdm", "readme.md", true)]
    [TestCase("rme", "readme.md", true)]
    [TestCase("xyz", "readme.md", false)]
    [TestCase("readme", "readme.md", true)]
    [TestCase("mdreadme", "readme.md", false)] // wrong order
    public void Subsequence_Matching(string pattern, string candidate, bool expected)
    {
        Assert.That(FuzzyMatcher.IsMatch(pattern, candidate), Is.EqualTo(expected));
    }

    [Test]
    public void Is_Case_Insensitive()
    {
        Assert.That(FuzzyMatcher.IsMatch("RM", "readme.md"), Is.True);
    }

    [Test]
    public void Contiguous_Match_Scores_Higher_Than_Scattered()
    {
        FuzzyMatcher.TryMatch("abc", "abcxyz", out var contiguous);
        FuzzyMatcher.TryMatch("abc", "axbxc", out var scattered);

        Assert.That(contiguous, Is.GreaterThan(scattered));
    }

    [Test]
    public void Word_Boundary_Match_Scores_Higher()
    {
        // "op" hitting the start of a word ("open_file") beats a mid-word hit ("loophole").
        FuzzyMatcher.TryMatch("op", "open_file", out var boundary);
        FuzzyMatcher.TryMatch("op", "xloophole", out var midword);

        Assert.That(boundary, Is.GreaterThan(midword));
    }

    [Test]
    public void Pattern_Longer_Than_Candidate_Never_Matches()
    {
        Assert.That(FuzzyMatcher.IsMatch("abcdef", "abc"), Is.False);
    }
}
