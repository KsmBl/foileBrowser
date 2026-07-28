using System.Drawing;
using FoileBrowser.Models;
using FoileBrowser.ViewModels;
using Hawkynt.NativeForms;

namespace FoileBrowser.Views;

/// <summary>Batch-rename dialog with a live preview (PRD §6.3). Returns the proposals, or null when cancelled.</summary>
public sealed class BatchRenameDialog : Form
{
    private readonly BatchRenameViewModel _vm;

    private readonly TextBox _find = new() { Bounds = new(100, 14, 190, 26), PlaceholderText = "(empty = template mode)" };
    private readonly TextBox _replace = new() { Bounds = new(370, 14, 240, 26) };
    private readonly CheckBox _regex = new() { Text = "Regex", Bounds = new(16, 50, 80, 24) };
    private readonly CheckBox _ignoreCase = new() { Text = "Ignore case", Bounds = new(102, 50, 110, 24) };
    private readonly NumericUpDown _start = new() { Bounds = new(300, 50, 70, 26), Minimum = 0, Maximum = 100000 };
    private readonly NumericUpDown _step = new() { Bounds = new(410, 50, 60, 26), Minimum = 1, Maximum = 1000 };
    private readonly NumericUpDown _pad = new() { Bounds = new(516, 50, 60, 26), Minimum = 1, Maximum = 8 };
    private readonly Label _error = new() { Bounds = new(16, 106, 600, 20), ForeColor = Color.FromArgb(0xE5, 0x48, 0x4D) };
    private readonly ListBox _preview = new() { Bounds = new(16, 132, 600, 340) };

    private bool _suppress;

    public BatchRenameDialog(IReadOnlyList<FileSystemEntry> entries)
    {
        _vm = new BatchRenameViewModel(entries);

        this.Text = "Batch Rename";
        this.Bounds = new(0, 0, 640, 560);
        this.StartPosition = FormStartPosition.CenterParent;

        _preview.DisplaySelector = static o =>
        {
            var proposal = (RenameProposal)o!;
            return $"{proposal.OriginalName}   ->   {proposal.ProposedName}{(proposal.Changed ? string.Empty : "   (unchanged)")}";
        };

        var apply = new Button { Text = "Apply", Bounds = new(526, 486, 90, 30), DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Bounds = new(428, 486, 90, 30) };

        this.Controls.AddRange(
            new Label { Text = "Find", Bounds = new(16, 16, 80, 22) },
            _find,
            new Label { Text = "Replace", Bounds = new(300, 16, 66, 22) },
            _replace,
            _regex,
            _ignoreCase,
            new Label { Text = "Counter start", Bounds = new(220, 52, 80, 22) },
            _start,
            new Label { Text = "Step", Bounds = new(376, 52, 34, 22) },
            _step,
            new Label { Text = "Pad", Bounds = new(482, 52, 32, 22) },
            _pad,
            new Label
            {
                Text = "Tokens: {name} {ext} {n} {date} {date:yyyy-MM}",
                Bounds = new(16, 82, 600, 20),
                ForeColor = Color.Gray,
            },
            _error,
            _preview,
            apply,
            cancel);

        this.AcceptButton = apply;
        this.CancelButton = cancel;

        this.PushToViewModel();
        this.Wire();
        Ui.Watch(_vm, this.PullFromViewModel);
        Ui.WatchList(_vm.Proposals, this.SyncPreview);
    }

    private void Wire()
    {
        _find.TextChanged += (_, _) => this.PushToViewModel();
        _replace.TextChanged += (_, _) => this.PushToViewModel();
        _regex.CheckedChanged += (_, _) => this.PushToViewModel();
        _ignoreCase.CheckedChanged += (_, _) => this.PushToViewModel();
        _start.ValueChanged += (_, _) => this.PushToViewModel();
        _step.ValueChanged += (_, _) => this.PushToViewModel();
        _pad.ValueChanged += (_, _) => this.PushToViewModel();
    }

    private void PushToViewModel()
    {
        if (_suppress)
            return;

        _vm.Find = _find.Text;
        _vm.Replace = _replace.Text;
        _vm.UseRegex = _regex.Checked;
        _vm.CaseInsensitive = _ignoreCase.Checked;
        _vm.CounterStart = (int)_start.Value;
        _vm.CounterStep = (int)_step.Value;
        _vm.CounterPadding = (int)_pad.Value;
    }

    private void PullFromViewModel()
    {
        _suppress = true;
        if (_find.Text != _vm.Find)
            _find.Text = _vm.Find;
        if (_replace.Text != _vm.Replace)
            _replace.Text = _vm.Replace;
        _regex.Checked = _vm.UseRegex;
        _ignoreCase.Checked = _vm.CaseInsensitive;
        _start.Value = _vm.CounterStart;
        _step.Value = _vm.CounterStep;
        _pad.Value = Math.Clamp(_vm.CounterPadding, 1, 8);
        _error.Text = _vm.Error ?? string.Empty;
        _suppress = false;
    }

    private void SyncPreview()
    {
        _preview.Items.Clear();
        foreach (var proposal in _vm.Proposals)
            _preview.Items.Add(proposal);
    }

    public static Task<IReadOnlyList<RenameProposal>?> RequestAsync(Form owner, IReadOnlyList<FileSystemEntry> entries)
    {
        var dialog = new BatchRenameDialog(entries);
        return Task.FromResult(dialog.ShowDialog(owner) == DialogResult.OK ? dialog._vm.AcceptedProposals : null);
    }
}
