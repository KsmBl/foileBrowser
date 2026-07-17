using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FoileBrowser.ViewModels;

/// <summary>
/// One directory node in the sidebar folder-tree navigator (PRD §6.2). Children load lazily the first
/// time the node is expanded, so the tree only touches the disk for branches the user actually opens.
/// A placeholder child is shown up-front so the expander arrow appears before loading; if a branch
/// turns out to be empty it collapses away after expansion.
/// </summary>
public sealed partial class FolderNodeViewModel : ObservableObject
{
    private readonly SynchronizationContext? _sync = SynchronizationContext.Current;
    private static readonly FolderNodeViewModel Placeholder = new();
    private bool _loaded;

    public string Name { get; }
    public string Path { get; }
    public ObservableCollection<FolderNodeViewModel> Children { get; } = [];

    [ObservableProperty]
    private bool _isExpanded;

    private FolderNodeViewModel()
    {
        Name = "…";
        Path = string.Empty;
    }

    public FolderNodeViewModel(string name, string path)
    {
        Name = name;
        Path = path;
        Children.Add(Placeholder); // makes the expander appear; real children load on first expand
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value)
            Load();
    }

    private void Load()
    {
        if (_loaded)
            return;
        _loaded = true;

        _ = Task.Run(() =>
        {
            var kids = EnumerateChildren(Path);
            Post(() =>
            {
                Children.Clear();
                foreach (var (name, path) in kids)
                    Children.Add(new FolderNodeViewModel(name, path));
            });
        });
    }

    private static List<(string Name, string Path)> EnumerateChildren(string path)
    {
        var result = new List<(string, string)>();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(path))
            {
                var name = System.IO.Path.GetFileName(dir);
                result.Add((string.IsNullOrEmpty(name) ? dir : name, dir));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable branch — show it as empty rather than failing.
        }
        result.Sort((a, b) => string.Compare(a.Item1, b.Item1, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    private void Post(Action action)
    {
        if (_sync is not null)
            _sync.Post(_ => action(), null);
        else
            action();
    }
}
