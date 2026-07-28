using System.Drawing;
using FoileBrowser.ViewModels;
using Hawkynt.NativeForms;

namespace FoileBrowser.Views;

/// <summary>
/// The fuzzy command palette (PRD §6.6). It was an overlay inside the shell window; the toolkit has
/// no z-ordered layer above a form's content, so it is its own small tool window — the "type, arrow,
/// Enter" flow is unchanged.
/// </summary>
public sealed class CommandPaletteDialog : Form
{
    private readonly CommandPaletteViewModel _palette;
    private readonly TextBox _query = new() { PlaceholderText = "Type a command…", Bounds = new(10, 10, 540, 26) };
    private readonly ListBox _results = new() { Bounds = new(10, 44, 540, 330) };

    private bool _suppress;

    public CommandPaletteDialog(CommandPaletteViewModel palette)
    {
        _palette = palette;

        this.Text = "Command Palette";
        this.Bounds = new(0, 0, 560, 390);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MinimizeBox = false;
        this.MaximizeBox = false;

        Ui.Outline(this);

        this.Controls.AddRange(_query, _results);
        this.ActiveControl = _query;

        _query.TextChanged += (_, _) =>
        {
            if (!_suppress)
                _palette.Query = _query.Text;
        };
        _query.KeyDown += this.OnQueryKeyDown;

        _results.SelectedIndexChanged += (_, _) =>
        {
            if (_suppress || _results.SelectedIndex < 0 || _results.SelectedIndex >= _palette.Results.Count)
                return;
            _palette.Selected = _palette.Results[_results.SelectedIndex];
        };
        _results.DoubleClick += (_, _) => this.Run();

        this.Load += (_, _) =>
        {
            _suppress = true;
            _query.Text = string.Empty;
            _suppress = false;
            _palette.Query = string.Empty;
            this.SyncResults();
        };

        Ui.ObserveList(_palette.Results, this.SyncResults);
        Ui.Watch(_palette, () =>
        {
            if (!_palette.IsOpen && this.Visible)
                this.Close();
        }, nameof(CommandPaletteViewModel.IsOpen));

        this.FormClosed += (_, _) => _palette.Close();
    }

    private void SyncResults()
    {
        _suppress = true;
        _results.Items.Clear();
        foreach (var command in _palette.Results)
            _results.Items.Add(Describe(command));

        var index = _palette.Selected is null ? -1 : _palette.Results.IndexOf(_palette.Selected);
        _results.SelectedIndex = index >= 0 ? index : _palette.Results.Count > 0 ? 0 : -1;
        _suppress = false;
    }

    private static string Describe(CommandItem command) =>
        string.IsNullOrEmpty(command.Gesture)
            ? $"{command.Category}   {command.Title}"
            : $"{command.Category}   {command.Title}   [{command.Gesture}]";

    private void OnQueryKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Down:
                _palette.MoveSelection(1);
                this.SyncSelectionToList();
                e.Handled = true;
                break;
            case Keys.Up:
                _palette.MoveSelection(-1);
                this.SyncSelectionToList();
                e.Handled = true;
                break;
            case Keys.Enter:
                this.Run();
                e.Handled = true;
                break;
            case Keys.Escape:
                _palette.Close();
                this.Close();
                e.Handled = true;
                break;
        }
    }

    private void SyncSelectionToList()
    {
        if (_palette.Selected is not { } selected)
            return;

        _suppress = true;
        _results.SelectedIndex = _palette.Results.IndexOf(selected);
        _suppress = false;
    }

    private void Run()
    {
        this.Close();
        _palette.ExecuteSelectedCommand.Execute(null);
    }
}
