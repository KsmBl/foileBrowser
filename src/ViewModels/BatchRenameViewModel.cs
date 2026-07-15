using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using FoileBrowser.Models;
using FoileBrowser.Services;

namespace FoileBrowser.ViewModels;

/// <summary>Backs the batch-rename dialog: edits a rule and live-previews the result (PRD §6.3).</summary>
public partial class BatchRenameViewModel : ViewModelBase
{
    private readonly IReadOnlyList<FileSystemEntry> _entries;

    [ObservableProperty] private string _find = string.Empty;
    [ObservableProperty] private string _replace = "{name}{ext}";
    [ObservableProperty] private bool _useRegex;
    [ObservableProperty] private bool _caseInsensitive;
    [ObservableProperty] private int _counterStart = 1;
    [ObservableProperty] private int _counterStep = 1;
    [ObservableProperty] private int _counterPadding = 1;
    [ObservableProperty] private string? _error;

    public ObservableCollection<RenameProposal> Proposals { get; } = [];

    public BatchRenameViewModel(IReadOnlyList<FileSystemEntry> entries)
    {
        _entries = entries;
        Recompute();
    }

    public IReadOnlyList<RenameProposal> AcceptedProposals => Proposals.ToList();

    private void Recompute()
    {
        var rule = new BatchRenameRule
        {
            Find = Find,
            Replace = Replace,
            UseRegex = UseRegex,
            CaseInsensitive = CaseInsensitive,
            CounterStart = CounterStart,
            CounterStep = CounterStep,
            CounterPadding = Math.Max(1, CounterPadding),
        };

        try
        {
            var results = BatchRenamer.Preview(_entries, rule);
            Proposals.Clear();
            foreach (var p in results)
                Proposals.Add(p);
            Error = null;
        }
        catch (RegexParseException ex)
        {
            Error = "Invalid regex: " + ex.Message;
        }
    }

    partial void OnFindChanged(string value) => Recompute();
    partial void OnReplaceChanged(string value) => Recompute();
    partial void OnUseRegexChanged(bool value) => Recompute();
    partial void OnCaseInsensitiveChanged(bool value) => Recompute();
    partial void OnCounterStartChanged(int value) => Recompute();
    partial void OnCounterStepChanged(int value) => Recompute();
    partial void OnCounterPaddingChanged(int value) => Recompute();
}
