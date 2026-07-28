using System.Drawing;
using FoileBrowser.ViewModels;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Drawing;

namespace FoileBrowser.Views;

/// <summary>
/// The background copy/move queue strip (PRD §6.3): one row per operation with its description,
/// live progress, status and a cancel button while it is still running.
/// </summary>
public sealed class OperationsView : Panel
{
    /// <summary>Kept in step with the strip height the shell reserves per operation.</summary>
    private const int RowHeight = 24;

    private readonly OperationQueueViewModel _queue;
    private readonly List<Action> _cleanup = [];

    public OperationsView(OperationQueueViewModel queue)
    {
        _queue = queue;
        this.AutoScroll = true;
    }

    /// <summary>Rebuilds the rows after the queue changed.</summary>
    public void Sync()
    {
        foreach (var undo in _cleanup)
            undo();
        _cleanup.Clear();
        this.Controls.Clear();

        // Rows are docked so they span the strip and re-flow when the window resizes; docked
        // siblings claim their edge in reverse order, so adding backwards keeps queue order.
        foreach (var operation in _queue.Operations.Reverse())
            this.Controls.Add(this.BuildRow(operation));
    }

    private Control BuildRow(FileOperationViewModel operation)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Bounds = new Rectangle(0, 0, 0, RowHeight),
            ColumnCount = 4,
            RowCount = 1,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));

        var description = new Label
        {
            Text = operation.Description,
            Margin = new(4, 2, 4, 2),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        var bar = new ProgressBar { Margin = new(2, 7, 2, 7), Minimum = 0, Maximum = 100 };
        var status = new Label { Margin = new(4, 2, 4, 2), ForeColor = Color.Gray, TextAlign = ContentAlignment.MiddleLeft };
        var cancel = new Button { Image = Icons.CloseIcon, Margin = new(2), Command = operation.CancelCommand };

        _cleanup.Add(Ui.Watch(operation, () =>
        {
            bar.Value = Math.Clamp((int)Math.Round(operation.Progress * 100), 0, 100);
            status.Text = operation.ErrorMessage ?? operation.Status.ToString();
            cancel.Enabled = operation.IsActive;
        }));

        row.Controls.AddRange(description, bar, status, cancel);
        return row;
    }
}
