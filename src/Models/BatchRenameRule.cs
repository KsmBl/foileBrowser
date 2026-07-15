namespace FoileBrowser.Models;

/// <summary>
/// A batch-rename specification (PRD §6.3, OneCommander File Automator style). Either a
/// find/replace over each name, or — when <see cref="Find"/> is empty — a full-name template.
/// Replacement text supports tokens: {name} (stem), {ext} (incl. dot), {n} (counter),
/// {date} / {date:format} (modified date).
/// </summary>
public sealed class BatchRenameRule
{
    public string Find { get; set; } = string.Empty;
    public string Replace { get; set; } = string.Empty;
    public bool UseRegex { get; set; }
    public bool CaseInsensitive { get; set; }

    public int CounterStart { get; set; } = 1;
    public int CounterStep { get; set; } = 1;
    public int CounterPadding { get; set; } = 1;
}

/// <summary>One row of a batch-rename preview.</summary>
public sealed record RenameProposal(FileSystemEntry Entry, string OriginalName, string ProposedName)
{
    public bool Changed => !string.Equals(OriginalName, ProposedName, StringComparison.Ordinal);
}
