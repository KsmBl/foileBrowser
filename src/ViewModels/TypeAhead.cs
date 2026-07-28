namespace FoileBrowser.ViewModels;

/// <summary>
/// Type-to-select for the file list (PRD §6.6): typing letters jumps to the next entry starting with
/// what has been typed. Kept as pure state so the matching is testable without a list in front of it.
///
/// The rules are the ones every file list has settled on: keystrokes inside the timeout extend the
/// search, a pause starts a new one, the search begins *after* the current row so repeating a letter
/// walks through the entries starting with it, and it wraps.
/// </summary>
public sealed class TypeAhead
{
    /// <summary>How long a typed prefix stays live. A pause longer than this starts a new search.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    private string _prefix = string.Empty;
    private DateTime _typedAt = DateTime.MinValue;

    /// <summary>What is currently being searched for; empty once it has timed out.</summary>
    public string Prefix => _prefix;

    /// <summary>
    /// Folds a keystroke into the search and returns the index to select, or -1 when nothing matches.
    /// </summary>
    /// <param name="typed">The character typed.</param>
    /// <param name="names">The entries in display order.</param>
    /// <param name="current">The row selected now, or -1.</param>
    /// <param name="now">The time of the keystroke, so the timeout is testable.</param>
    public int Next(char typed, IReadOnlyList<string> names, int current, DateTime now)
    {
        var expired = now - _typedAt > Timeout;
        _typedAt = now;

        // Repeating one letter steps through the entries beginning with it, rather than searching
        // for a doubled letter — the behaviour every file list has.
        var repeating = !expired && _prefix.Length == 1 && char.ToUpperInvariant(_prefix[0]) == char.ToUpperInvariant(typed);
        _prefix = expired || repeating ? typed.ToString() : _prefix + typed;

        if (names.Count == 0)
            return -1;

        // A grown prefix re-tests the current row first, so typing "re" after "r" stays put when the
        // row already matches; a fresh or repeated letter always moves on.
        var start = expired || repeating || _prefix.Length == 1 ? current + 1 : Math.Max(current, 0);

        for (var offset = 0; offset < names.Count; ++offset)
        {
            var index = ((start + offset) % names.Count + names.Count) % names.Count;
            if (names[index].StartsWith(_prefix, StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return -1;
    }

    /// <summary>Forgets the search — after a navigation, or when focus leaves the list.</summary>
    public void Reset()
    {
        _prefix = string.Empty;
        _typedAt = DateTime.MinValue;
    }
}
