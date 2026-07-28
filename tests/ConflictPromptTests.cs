using FoileBrowser.Models;
using FoileBrowser.Views;

namespace FoileBrowser.Tests;

/// <summary>
/// The conflict prompt's memory (PRD §6.3). The dialog itself needs a screen; what matters here is
/// that "apply to all" stops asking and that it is forgotten between operations — getting either
/// wrong means a queue that either nags on every file or silently overwrites the next batch.
/// </summary>
[TestFixture]
public class ConflictPromptTests
{
    private static ConflictRequest Request(string name) => new($"/from/{name}", $"/to/{name}");

    [Test]
    public void Asks_Every_Time_Until_Told_To_Apply_To_All()
    {
        var asked = 0;
        var prompt = new ConflictDialog.Prompt(_ => { ++asked; return (ConflictResolution.Skip, false); });

        prompt.Resolve(Request("a"));
        prompt.Resolve(Request("b"));

        Assert.That(asked, Is.EqualTo(2));
    }

    [Test]
    public void Apply_To_All_Answers_The_Rest_Without_Asking()
    {
        var asked = 0;
        var prompt = new ConflictDialog.Prompt(_ => { ++asked; return (ConflictResolution.Overwrite, true); });

        var first = prompt.Resolve(Request("a"));
        var second = prompt.Resolve(Request("b"));
        var third = prompt.Resolve(Request("c"));

        Assert.Multiple(() =>
        {
            Assert.That(asked, Is.EqualTo(1), "only the first collision asked");
            Assert.That(first, Is.EqualTo(ConflictResolution.Overwrite));
            Assert.That(second, Is.EqualTo(ConflictResolution.Overwrite));
            Assert.That(third, Is.EqualTo(ConflictResolution.Overwrite));
        });
    }

    [Test]
    public void The_Next_Operation_Asks_Again()
    {
        var asked = 0;
        var prompt = new ConflictDialog.Prompt(_ => { ++asked; return (ConflictResolution.Overwrite, true); });
        prompt.Resolve(Request("a"));

        prompt.Reset();
        prompt.Resolve(Request("b"));

        Assert.That(asked, Is.EqualTo(2), "a remembered decision does not leak into the next operation");
    }
}
