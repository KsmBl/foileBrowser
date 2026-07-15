namespace FoileBrowser.ViewModels;

/// <summary>
/// Back/forward navigation stack (PRD §6.1). A linear history with a cursor: navigating to a
/// new location truncates any forward entries, mirroring a web browser. Pure and UI-independent
/// so the traversal logic can be unit tested without a running app.
/// </summary>
public sealed class NavigationHistory
{
    private readonly List<string> _entries = [];
    private int _cursor = -1;

    public bool CanGoBack => _cursor > 0;

    public bool CanGoForward => _cursor >= 0 && _cursor < _entries.Count - 1;

    public string? Current => _cursor >= 0 && _cursor < _entries.Count ? _entries[_cursor] : null;

    /// <summary>
    /// Records a visit to <paramref name="path"/>. A no-op when it equals the current location
    /// (e.g. a refresh), otherwise it becomes the new head and clears forward history.
    /// </summary>
    public void Visit(string path)
    {
        if (string.Equals(path, Current, StringComparison.Ordinal))
            return;

        // Drop any forward history before appending the new location.
        if (_cursor < _entries.Count - 1)
            _entries.RemoveRange(_cursor + 1, _entries.Count - _cursor - 1);

        _entries.Add(path);
        _cursor = _entries.Count - 1;
    }

    public string? GoBack()
    {
        if (!CanGoBack)
            return null;

        _cursor--;
        return Current;
    }

    public string? GoForward()
    {
        if (!CanGoForward)
            return null;

        _cursor++;
        return Current;
    }
}
