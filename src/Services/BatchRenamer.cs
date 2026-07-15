using System.Text.RegularExpressions;
using FoileBrowser.Models;

namespace FoileBrowser.Services;

/// <summary>
/// Pure batch-rename computation (PRD §6.3). Produces the proposed names for a set of entries;
/// applying them is left to the file-operation service. Side-effect free and unit-testable.
/// </summary>
public static class BatchRenamer
{
    private static readonly Regex TokenPattern =
        new(@"\{(name|ext|n|date(?::(?<fmt>[^}]+))?)\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Computes a proposal per entry. Throws <see cref="RegexParseException"/> if the rule uses an
    /// invalid regex, so callers can surface it to the user.
    /// </summary>
    public static IReadOnlyList<RenameProposal> Preview(
        IReadOnlyList<FileSystemEntry> entries, BatchRenameRule rule)
    {
        var proposals = new List<RenameProposal>(entries.Count);
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var counter = rule.CounterStart + i * rule.CounterStep;
            var proposed = ComputeName(entry, rule, counter);

            // Disambiguate collisions within the batch by appending the counter.
            if (!string.IsNullOrEmpty(proposed) && seen.TryGetValue(proposed, out var n))
            {
                seen[proposed] = n + 1;
                proposed = AppendBeforeExtension(proposed, $" ({n + 1})");
            }
            else if (!string.IsNullOrEmpty(proposed))
            {
                seen[proposed] = 1;
            }

            proposals.Add(new RenameProposal(entry, entry.Name, proposed));
        }

        return proposals;
    }

    private static string ComputeName(FileSystemEntry entry, BatchRenameRule rule, int counter)
    {
        var replacement = ExpandTokens(rule.Replace, entry, rule, counter);

        // Empty Find → the replacement is the entire new name (template mode).
        if (string.IsNullOrEmpty(rule.Find))
            return string.IsNullOrEmpty(replacement) ? entry.Name : replacement;

        var options = rule.CaseInsensitive ? RegexOptions.IgnoreCase : RegexOptions.None;
        if (rule.UseRegex)
            return Regex.Replace(entry.Name, rule.Find, replacement, options);

        // Literal find/replace (all occurrences).
        var comparison = rule.CaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return ReplaceLiteral(entry.Name, rule.Find, replacement, comparison);
    }

    private static string ExpandTokens(string template, FileSystemEntry entry, BatchRenameRule rule, int counter)
    {
        var stem = Path.GetFileNameWithoutExtension(entry.Name);
        var ext = Path.GetExtension(entry.Name); // includes the dot, or ""

        return TokenPattern.Replace(template, match =>
        {
            var token = match.Groups[1].Value.ToLowerInvariant();
            if (token.StartsWith("date", StringComparison.Ordinal))
            {
                var fmt = match.Groups["fmt"].Success ? match.Groups["fmt"].Value : "yyyy-MM-dd";
                var date = entry.Modified?.LocalDateTime ?? DateTime.Now;
                return date.ToString(fmt);
            }

            return token switch
            {
                "name" => stem,
                "ext" => ext,
                "n" => counter.ToString().PadLeft(rule.CounterPadding, '0'),
                _ => match.Value,
            };
        });
    }

    private static string ReplaceLiteral(string input, string find, string replace, StringComparison comparison)
    {
        if (string.IsNullOrEmpty(find))
            return input;

        var result = new System.Text.StringBuilder();
        var index = 0;
        while (true)
        {
            var found = input.IndexOf(find, index, comparison);
            if (found < 0)
            {
                result.Append(input, index, input.Length - index);
                break;
            }
            result.Append(input, index, found - index);
            result.Append(replace);
            index = found + find.Length;
        }
        return result.ToString();
    }

    private static string AppendBeforeExtension(string name, string suffix)
    {
        var ext = Path.GetExtension(name);
        var stem = Path.GetFileNameWithoutExtension(name);
        return stem + suffix + ext;
    }
}
