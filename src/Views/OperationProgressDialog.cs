using System.Drawing;
using FoileBrowser.Models;
using FoileBrowser.ViewModels;
using Hawkynt.NativeForms;

namespace FoileBrowser.Views;

/// <summary>
/// The detailed view of one running transfer (PRD §6.3): both progress scales, what it is moving
/// right now, its current and average speed with the recent history as a graph, and how much longer
/// it has to go.
/// </summary>
/// <remarks>
/// Deliberately not modal, and it does not own the transfer: the operation runs in the queue whether
/// this window is open, closed or reopened, and browsing carries on behind it. Closing the window
/// stops watching; only Cancel stops the work.
/// </remarks>
public sealed class OperationProgressDialog : Form
{
    private const int Pad = 16;
    private const int Width_ = 520;

    private readonly FileOperationViewModel _operation;
    private readonly DisplayOptions _display;
    private readonly List<Action> _cleanup = [];

    private readonly Label _description = new() { Bounds = new(Pad, Pad, Width_ - (2 * Pad), 22) };
    private readonly Label _overallCaption = new() { Bounds = new(Pad, 46, 200, 18), ForeColor = Color.Gray };
    private readonly ProgressBar _overall = new() { Bounds = new(Pad, 68, Width_ - (2 * Pad), 20), Minimum = 0, Maximum = 100 };

    private readonly Label _itemCaption = new() { Bounds = new(Pad, 98, Width_ - (2 * Pad), 18), ForeColor = Color.Gray };
    private readonly ProgressBar _item = new() { Bounds = new(Pad, 120, Width_ - (2 * Pad), 14), Minimum = 0, Maximum = 100 };

    private readonly SpeedGraph _graph = new() { Bounds = new(Pad, 150, Width_ - (2 * Pad), 96) };

    private readonly Label _speed = new() { Bounds = new(Pad, 256, 240, 20) };
    private readonly Label _average = new() { Bounds = new(Pad, 278, 240, 20), ForeColor = Color.Gray };
    private readonly Label _eta = new() { Bounds = new(Pad + 250, 256, 220, 20) };
    private readonly Label _status = new() { Bounds = new(Pad + 250, 278, 220, 20), ForeColor = Color.Gray };

    private readonly Button _cancel = new() { Text = "Cancel", Bounds = new(Width_ - Pad - 110, 310, 110, 28) };
    private readonly Button _close = new() { Text = "Close", Bounds = new(Width_ - Pad - 228, 310, 110, 28) };

    public OperationProgressDialog(FileOperationViewModel operation, DisplayOptions display)
    {
        _operation = operation;
        _display = display;

        this.Text = operation.Description;
        this.Bounds = new(0, 0, Width_, 388);
        this.StartPosition = FormStartPosition.CenterParent;
        Ui.Outline(this);

        _description.Text = operation.Description;
        _cancel.Command = operation.CancelCommand;
        _close.Click += (_, _) => this.Close();

        this.Controls.AddRange(
            _description, _overallCaption, _overall, _itemCaption, _item,
            _graph, _speed, _average, _eta, _status, _cancel, _close);

        _cleanup.Add(Ui.Watch(operation, this.Sync));
        this.Sync();

        // The tracker is handed over when the operation starts, which may be after this opened.
        this.FormClosed += (_, _) =>
        {
            foreach (var undo in _cleanup)
                undo();
            _cleanup.Clear();
        };
    }

    private void Sync()
    {
        _graph.Rate = _operation.Rate;

        _overall.Value = Percent(_operation.Progress);
        _overallCaption.Text = _operation.BytesTotal > 0
            ? $"{Bytes(_operation.BytesDone)} of {Bytes(_operation.BytesTotal)}  ({_operation.Progress:P0})"
            : $"{_operation.Progress:P0}";

        _itemCaption.Text = _operation.CurrentItem.Length > 0 ? _operation.CurrentItem : "—";
        _item.Value = Percent(_operation.ItemProgress);

        _speed.Text = _operation.Speed > 0 ? $"Speed: {Bytes((long)_operation.Speed)}/s" : "Speed: —";
        _average.Text = _operation.AverageSpeed > 0 ? $"Average: {Bytes((long)_operation.AverageSpeed)}/s" : "Average: —";
        _eta.Text = _operation.Eta is { } eta ? $"Time left: {Remaining(eta)}" : "Time left: —";
        _status.Text = _operation.ErrorMessage ?? _operation.Status.ToString();

        _cancel.Enabled = _operation.IsActive;
        _graph.Invalidate();
    }

    private static int Percent(double fraction) => Math.Clamp((int)Math.Round(fraction * 100), 0, 100);

    private string Bytes(long bytes) => ValueFormat.Size(bytes, _display.SizeUnit);

    /// <summary>An estimate read the way a person says it, and never with more precision than it has.</summary>
    private static string Remaining(TimeSpan span) => span switch
    {
        { TotalSeconds: < 1 } => "less than a second",
        { TotalMinutes: < 1 } => $"{span.Seconds} s",
        { TotalHours: < 1 } => $"{span.Minutes} min {span.Seconds} s",
        { TotalDays: < 1 } => $"{(int)span.TotalHours} h {span.Minutes} min",
        _ => "more than a day",
    };
}
