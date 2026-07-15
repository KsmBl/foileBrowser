namespace FoileBrowser.Services;

/// <summary>
/// Case-insensitive subsequence fuzzy matching with scoring, shared by the as-you-type
/// filter, recursive search ranking and the command palette (PRD §6.4, §6.6). Pure and
/// allocation-light so it can run over large lists and be unit tested directly.
/// </summary>
public static class FuzzyMatcher
{
    /// <summary>
    /// Returns true if every character of <paramref name="pattern"/> appears in
    /// <paramref name="candidate"/> in order (not necessarily contiguously). An empty
    /// pattern matches everything. <paramref name="score"/> ranks match quality: higher is
    /// better, rewarding contiguous runs and word-boundary starts.
    /// </summary>
    public static bool TryMatch(string pattern, string candidate, out int score)
    {
        score = 0;
        if (string.IsNullOrEmpty(pattern))
            return true;
        if (string.IsNullOrEmpty(candidate) || pattern.Length > candidate.Length)
            return false;

        var patternIndex = 0;
        var runLength = 0;
        var matched = 0;

        for (var i = 0; i < candidate.Length && patternIndex < pattern.Length; i++)
        {
            if (char.ToLowerInvariant(candidate[i]) != char.ToLowerInvariant(pattern[patternIndex]))
            {
                runLength = 0;
                continue;
            }

            // Base point per matched char, with bonuses for context.
            var points = 1;
            runLength++;
            points += runLength; // consecutive-run bonus

            if (i == 0 || IsBoundary(candidate, i))
                points += 4; // matched at a word boundary
            if (i == patternIndex)
                points += 1; // prefix alignment

            score += points;
            patternIndex++;
            matched++;
        }

        if (patternIndex != pattern.Length)
            return false;

        // Prefer shorter candidates and full-length matches.
        score += matched * 2;
        score -= candidate.Length / 4;
        return true;
    }

    /// <summary>Convenience overload discarding the score.</summary>
    public static bool IsMatch(string pattern, string candidate) => TryMatch(pattern, candidate, out _);

    private static bool IsBoundary(string text, int index)
    {
        if (index <= 0)
            return true;
        var prev = text[index - 1];
        var cur = text[index];
        // Separators, or a lowercase→uppercase transition (camelCase).
        return prev is ' ' or '_' or '-' or '.' or '/' or '\\'
            || (char.IsLower(prev) && char.IsUpper(cur));
    }
}
