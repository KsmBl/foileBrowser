namespace FoileBrowser.Services;

/// <summary>
/// What to offer while a path is being typed into the path bar (PRD §6.1, File Pilot's "GoTo").
/// </summary>
/// <remarks>
/// Two sources, in that order of usefulness. <b>Recently visited folders</b> come first and are
/// matched anywhere in the path, because the folder someone wants is usually one they have already
/// been in, and they rarely remember which of six levels it hung off — typing "inv" should find
/// <c>~/work/clients/acme/2026/invoices</c>. <b>Then the filesystem</b>, which only ever completes
/// the segment being typed, because that is the part the caret is in.
/// </remarks>
public static class PathCompletion
{
    /// <summary>The most candidates to return; the bar shows a handful and a longer list is noise.</summary>
    public const int Limit = 12;

    /// <summary>
    /// Candidate paths for <paramref name="typed"/>.
    /// </summary>
    /// <param name="typed">What is in the field right now.</param>
    /// <param name="recents">Recently visited folders, most recent first.</param>
    /// <param name="childrenOf">Lists a directory's subdirectories; returns empty for one that
    /// cannot be read, which is the ordinary case while a path is half-typed.</param>
    public static IReadOnlyList<string> Complete(
        string typed,
        IEnumerable<string> recents,
        Func<string, IEnumerable<string>> childrenOf)
    {
        if (string.IsNullOrWhiteSpace(typed))
            return [];

        var result = new List<string>(Limit);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var recent in recents)
        {
            if (result.Count >= Limit)
                break;

            if (recent.Contains(typed, StringComparison.OrdinalIgnoreCase))
                Offer(recent);
        }

        foreach (var child in FilesystemMatches(typed, childrenOf))
        {
            if (result.Count >= Limit)
                break;

            Offer(child);
        }

        return result;

        // Offering exactly what is already in the field is a row that does nothing, whichever source
        // produced it — so the check belongs here rather than on one of them.
        void Offer(string candidate)
        {
            if (!PathsEqual(candidate, typed) && seen.Add(candidate))
                result.Add(candidate);
        }
    }

    /// <summary>
    /// Subdirectories matching the segment being typed. A path ending in a separator is asking for
    /// everything inside it; anything else is a prefix of a name in its parent.
    /// </summary>
    private static IEnumerable<string> FilesystemMatches(string typed, Func<string, IEnumerable<string>> childrenOf)
    {
        var endsWithSeparator = typed[^1] is '/' or '\\';
        var directory = endsWithSeparator ? Trim(typed) : Directory(typed);
        if (directory.Length == 0)
            return [];

        var prefix = endsWithSeparator ? string.Empty : LastSegment(typed);

        return childrenOf(directory)
            .Where(child => prefix.Length == 0
                || System.IO.Path.GetFileName(child).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(System.IO.Path.GetFileName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The part of a typed path before its last separator, keeping a root's own slash.</summary>
    private static string Directory(string typed)
    {
        var cut = typed.LastIndexOfAny(['/', '\\']);
        return cut switch
        {
            < 0 => string.Empty,
            0 => typed[..1], // "/etc" hangs off the root itself
            _ => typed[..cut],
        };
    }

    /// <summary>Drops a trailing separator, except from a root, which is nothing but one.</summary>
    private static string Trim(string path)
    {
        var trimmed = path.TrimEnd('/', '\\');
        return trimmed.Length == 0 ? path[..1] : trimmed;
    }

    /// <summary>The part after the last separator — the segment the caret is in.</summary>
    private static string LastSegment(string typed)
    {
        var cut = typed.LastIndexOfAny(['/', '\\']);
        return cut < 0 ? typed : typed[(cut + 1)..];
    }

    /// <summary>Whether a candidate is just what was typed, separator differences aside — offering
    /// the folder you are already in as a suggestion is noise.</summary>
    private static bool PathsEqual(string a, string b)
        => string.Equals(a.TrimEnd('/', '\\'), b.TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase);
}
