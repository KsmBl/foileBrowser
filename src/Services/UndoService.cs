namespace FoileBrowser.Services;

/// <summary>One reversible thing that happened, and how to put it back or do it again.</summary>
/// <param name="Description">What to show in the menu, e.g. "Rename notes.txt".</param>
public sealed record UndoStep(string Description, Func<Task> Undo, Func<Task> Redo);

/// <summary>
/// The undo/redo history (PRD §6.3). Deliberately a list of closures rather than a command pattern
/// over the file services: what has to be reversed is always "put this path back", and the caller
/// already knows both paths at the moment it acts.
///
/// Only operations that are genuinely reversible go in. Sending a file to the trash does not: no
/// platform here exposes a supported "restore that item", and an undo that silently half-worked
/// would be worse than not offering one.
/// </summary>
public sealed class UndoService
{
    /// <summary>How many steps are kept. Deep enough to cover a session's mistakes, bounded so the
    /// history cannot pin arbitrary amounts of state.</summary>
    public const int Depth = 50;

    private readonly List<UndoStep> _undo = [];
    private readonly List<UndoStep> _redo = [];

    /// <summary>Raised after either stack changes, so menus can re-label and enable.</summary>
    public event EventHandler? Changed;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>What undo would reverse, for the menu caption.</summary>
    public string? UndoDescription => _undo.Count > 0 ? _undo[^1].Description : null;

    /// <summary>What redo would repeat, for the menu caption.</summary>
    public string? RedoDescription => _redo.Count > 0 ? _redo[^1].Description : null;

    /// <summary>Records something that just happened. Doing anything new drops the redo history.</summary>
    public void Record(UndoStep step)
    {
        _undo.Add(step);
        if (_undo.Count > Depth)
            _undo.RemoveAt(0);
        _redo.Clear();
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reverses the last step; false when there was nothing to reverse or it failed.</summary>
    public async Task<bool> UndoAsync()
    {
        if (_undo.Count == 0)
            return false;

        var step = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        try
        {
            await step.Undo();
            _redo.Add(step);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The file moved or vanished behind our back; the step is gone either way rather than
            // left to fail again on the next press.
            return false;
        }
        finally
        {
            this.Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Repeats the last undone step; false when there was nothing to repeat or it failed.</summary>
    public async Task<bool> RedoAsync()
    {
        if (_redo.Count == 0)
            return false;

        var step = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        try
        {
            await step.Redo();
            _undo.Add(step);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            this.Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Forgets everything — used when the history can no longer be trusted.</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        this.Changed?.Invoke(this, EventArgs.Empty);
    }
}
