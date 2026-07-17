using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FoileBrowser.ViewModels;

/// <summary>
/// A named, runnable action surfaced in the command palette, menus and hotkey bindings (PRD §6.6).
/// <see cref="Gesture"/> is the live, rebindable hotkey; <see cref="DefaultGesture"/> is the shipped
/// default it resets to. <see cref="Global"/> commands are bound as window-wide shortcuts (and are the
/// ones exposed in the keybind editor); list-scoped keys like F2/Delete are handled in the file list.
/// </summary>
public sealed partial class CommandItem : ObservableObject
{
    private readonly Func<Task> _run;
    private ICommand? _command;

    public CommandItem(string id, string title, string category, string? gesture, Func<Task> run, bool global = false)
    {
        Id = id;
        Title = title;
        Category = category;
        DefaultGesture = gesture;
        _gesture = gesture;
        _run = run;
        Global = global;
    }

    public string Id { get; }
    public string Title { get; }
    public string Category { get; }

    /// <summary>The shipped default hotkey; a rebind can be reset back to this.</summary>
    public string? DefaultGesture { get; }

    /// <summary>The live hotkey string (e.g. "Ctrl+P"); rebindable via Settings.</summary>
    [ObservableProperty]
    private string? _gesture;

    /// <summary>True when bound as a window-wide shortcut (vs. list-scoped keys handled in the file list).</summary>
    public bool Global { get; }

    public string DisplayCategory => $"{Category}";

    /// <summary>ICommand wrapper so the action can drive key bindings and menu items directly.</summary>
    public ICommand Command => _command ??= new AsyncRelayCommand(_run);

    public Task ExecuteAsync() => _run();
}
