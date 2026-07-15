using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FoileBrowser.Services;

namespace FoileBrowser.ViewModels;

/// <summary>
/// Fuzzy-searchable overlay listing every registered command (PRD §6.6). Purely a view model:
/// the hosting view shows/hides itself off <see cref="IsOpen"/> and forwards key input.
/// </summary>
public partial class CommandPaletteViewModel : ViewModelBase
{
    private readonly List<CommandItem> _all;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private CommandItem? _selected;

    public ObservableCollection<CommandItem> Results { get; } = [];

    public CommandPaletteViewModel(IEnumerable<CommandItem> commands)
    {
        _all = commands.ToList();
        Filter();
    }

    public void Open()
    {
        Query = string.Empty;
        Filter();
        IsOpen = true;
    }

    [RelayCommand]
    public void Close() => IsOpen = false;

    [RelayCommand]
    private async Task ExecuteSelected()
    {
        var item = Selected;
        IsOpen = false;
        if (item is not null)
            await item.ExecuteAsync();
    }

    /// <summary>Moves the highlight by <paramref name="delta"/> rows, clamped to the result list.</summary>
    public void MoveSelection(int delta)
    {
        if (Results.Count == 0)
            return;
        var index = Selected is null ? 0 : Results.IndexOf(Selected);
        index = Math.Clamp(index + delta, 0, Results.Count - 1);
        Selected = Results[index];
    }

    partial void OnQueryChanged(string value) => Filter();

    private void Filter()
    {
        var ranked = new List<(CommandItem item, int score)>();
        foreach (var command in _all)
        {
            if (FuzzyMatcher.TryMatch(Query, command.Title, out var titleScore))
                ranked.Add((command, titleScore));
            else if (FuzzyMatcher.TryMatch(Query, command.Category, out var catScore))
                ranked.Add((command, catScore - 10)); // category hits rank below title hits
        }

        ranked.Sort((a, b) => b.score.CompareTo(a.score));

        Results.Clear();
        foreach (var (item, _) in ranked)
            Results.Add(item);

        Selected = Results.Count > 0 ? Results[0] : null;
    }
}
