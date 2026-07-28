using FoileBrowser.Models;
using FoileBrowser.ViewModels;
using Hawkynt.NativeForms;

namespace FoileBrowser.Views;

/// <summary>What a drag carries: the paths being dragged and where they came from.</summary>
/// <param name="Paths">The selected files and folders, as absolute paths.</param>
/// <param name="SourceFolder">The folder they are in, so a drop onto it can be ignored.</param>
public sealed record FileDrag(IReadOnlyList<string> Paths, string SourceFolder);

/// <summary>
/// The drop half of dragging files between panes and onto sidebar places (PRD §6.3).
///
/// The drop asks whether to copy or move rather than guessing. Every other file manager decides it
/// from a modifier or from whether the two sides share a volume; the toolkit's drag events carry no
/// modifier state, and silently moving a file because it happened to be on the same disk is the kind
/// of surprise that loses work. A three-item menu at the pointer costs one click and is never wrong.
/// </summary>
internal static class FileDrop
{
    /// <summary>Makes <paramref name="control"/> accept dropped files into <paramref name="folder"/>.</summary>
    /// <param name="folder">Where a drop lands, evaluated at drop time so it follows navigation.</param>
    public static void Accept(Control control, MainWindowViewModel shell, Func<string?> folder)
    {
        control.AllowDrop = true;

        control.DragEnter += (_, e) => e.Effect = Allowed(e.Data as FileDrag, folder());
        control.DragOver += (_, e) => e.Effect = Allowed(e.Data as FileDrag, folder());
        control.DragDrop += (_, e) =>
        {
            if (e.Data is not FileDrag drag || folder() is not { Length: > 0 } destination)
                return;
            if (Allowed(drag, destination) == DragDropEffects.None)
                return;

            Ask(control, e.X, e.Y, kind => shell.OperationQueue.Enqueue(kind, drag.Paths, destination));
        };
    }

    /// <summary>A drag is welcome unless it would drop a folder onto itself, or where it already is.</summary>
    internal static DragDropEffects Allowed(FileDrag? drag, string? destination)
    {
        if (drag is null || drag.Paths.Count == 0 || string.IsNullOrEmpty(destination))
            return DragDropEffects.None;
        if (string.Equals(drag.SourceFolder, destination, StringComparison.Ordinal))
            return DragDropEffects.None;

        // Dropping a folder inside itself would recurse; the engine would refuse, but so should the
        // pointer, so the user knows before letting go.
        foreach (var path in drag.Paths)
            if (destination.StartsWith(path + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return DragDropEffects.None;

        return DragDropEffects.Copy | DragDropEffects.Move;
    }

    private static void Ask(Control control, int x, int y, Action<FileOperationKind> chosen)
    {
        var menu = new ContextMenuStrip();
        var copy = new ToolStripMenuItem("Copy here");
        copy.Click += (_, _) => chosen(FileOperationKind.Copy);
        var move = new ToolStripMenuItem("Move here");
        move.Click += (_, _) => chosen(FileOperationKind.Move);

        menu.Items.AddRange(copy, move, new ToolStripSeparator(), new ToolStripMenuItem("Cancel"));
        menu.Show(control, new(x, y));
    }
}
