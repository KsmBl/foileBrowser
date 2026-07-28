using FoileBrowser.Services;

namespace FoileBrowser.Tests;

/// <summary>The undo/redo history (PRD §6.3).</summary>
[TestFixture]
public class UndoServiceTests
{
    private static UndoStep Step(string name, List<string> log) => new(
        name,
        () => { log.Add($"undo {name}"); return Task.CompletedTask; },
        () => { log.Add($"redo {name}"); return Task.CompletedTask; });

    [Test]
    public void A_Fresh_History_Can_Do_Neither()
    {
        var history = new UndoService();

        Assert.That(history.CanUndo, Is.False);
        Assert.That(history.CanRedo, Is.False);
        Assert.That(history.UndoDescription, Is.Null);
    }

    [Test]
    public async Task Undo_Reverses_The_Last_Step_And_Redo_Repeats_It()
    {
        var log = new List<string>();
        var history = new UndoService();
        history.Record(Step("one", log));
        history.Record(Step("two", log));

        Assert.That(history.UndoDescription, Is.EqualTo("two"));
        Assert.That(await history.UndoAsync(), Is.True);
        Assert.That(await history.UndoAsync(), Is.True);
        Assert.That(await history.RedoAsync(), Is.True);

        Assert.That(log, Is.EqualTo(new[] { "undo two", "undo one", "redo one" }));
    }

    [Test]
    public async Task Doing_Something_New_Drops_What_Was_Undone()
    {
        var log = new List<string>();
        var history = new UndoService();
        history.Record(Step("one", log));
        await history.UndoAsync();

        history.Record(Step("two", log));

        Assert.That(history.CanRedo, Is.False, "the undone step is not waiting to be redone");
    }

    [Test]
    public async Task A_Step_That_Cannot_Be_Reversed_Is_Dropped_Rather_Than_Retried()
    {
        var history = new UndoService();
        history.Record(new UndoStep("gone", () => throw new IOException("no such file"), () => Task.CompletedTask));

        Assert.That(await history.UndoAsync(), Is.False);
        Assert.That(history.CanUndo, Is.False, "it is not left to fail again on the next press");
    }

    [Test]
    public void The_History_Is_Bounded()
    {
        var log = new List<string>();
        var history = new UndoService();
        for (var i = 0; i < UndoService.Depth + 10; ++i)
            history.Record(Step($"step{i}", log));

        Assert.That(history.UndoDescription, Is.EqualTo($"step{UndoService.Depth + 9}"));
    }

    [Test]
    public async Task Clearing_Forgets_Both_Directions()
    {
        var log = new List<string>();
        var history = new UndoService();
        history.Record(Step("one", log));
        await history.UndoAsync();

        history.Clear();

        Assert.That(history.CanUndo, Is.False);
        Assert.That(history.CanRedo, Is.False);
    }
}
