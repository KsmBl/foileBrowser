using Avalonia;
using Avalonia.Input;

namespace FoileBrowser.Views;

/// <summary>
/// Small helper shared by the toolbar and sidebar for drag-to-reorder: it arms on a left-button press
/// and only begins a drag once the pointer moves past a threshold, so plain clicks still activate the
/// control (PRD §6.2/§6.8). The payload is the reordered item's stable id. The (currently deprecated
/// but functional) drag/drop data API is used only here, so callers stay clean.
/// </summary>
public sealed class DragReorder
{
    private const string Format = "application/x-foile-reorder-id";
    private const double Threshold = 6;

    private string? _armedId;
    private Point _start;

    /// <summary>Records a potential drag from a left-button press on an item carrying <paramref name="id"/>.</summary>
    public void Arm(string id, PointerPressedEventArgs e, Visual reference)
    {
        if (e.GetCurrentPoint(reference).Properties.IsLeftButtonPressed)
        {
            _armedId = id;
            _start = e.GetPosition(reference);
        }
    }

    /// <summary>On movement past the threshold with the button still held, begins the drag.</summary>
    public async Task MaybeStartAsync(PointerEventArgs e, Visual reference)
    {
        if (_armedId is null)
            return;
        if (!e.GetCurrentPoint(reference).Properties.IsLeftButtonPressed)
        {
            _armedId = null;
            return;
        }

        var pos = e.GetPosition(reference);
        if (Math.Abs(pos.X - _start.X) < Threshold && Math.Abs(pos.Y - _start.Y) < Threshold)
            return;

        var id = _armedId;
        _armedId = null;
        await StartDragAsync(e, id);
    }

    public void Cancel() => _armedId = null;

#pragma warning disable CS0618 // DataObject/DoDragDrop are deprecated in 11.3 but remain functional
    private static async Task StartDragAsync(PointerEventArgs e, string id)
    {
        var data = new DataObject();
        data.Set(Format, id);
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
    }

    /// <summary>True when the drag carries a reorder payload; also sets the Move effect.</summary>
    public static bool Accept(DragEventArgs e)
    {
        var ok = e.Data.Contains(Format);
        e.DragEffects = ok ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
        return ok;
    }

    /// <summary>The dragged item's id from a drop, or null.</summary>
    public static string? DroppedId(DragEventArgs e) => e.Data.Get(Format) as string;
#pragma warning restore CS0618
}
