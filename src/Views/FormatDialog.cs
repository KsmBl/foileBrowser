using System.Drawing;
using FoileBrowser.Models;
using FoileBrowser.Services;
using FoileBrowser.ViewModels;
using Hawkynt.NativeForms;

namespace FoileBrowser.Views;

/// <summary>
/// Confirms and performs a destructive format of a block device (PRD §6.10). The user must retype
/// the device leaf name (e.g. "sdb1") before the Format button enables; the work runs as root via
/// pkexec.
/// </summary>
public sealed class FormatDialog : Form
{
    private readonly IDiskService _disk;
    private readonly string _device;
    private readonly string _leaf;
    private readonly List<FilesystemType> _filesystems;

    private readonly ComboBox _filesystem = new() { Bounds = new(120, 96, 200, 26) };
    private readonly TextBox _label = new() { Bounds = new(120, 130, 200, 26), PlaceholderText = "(optional)" };
    private readonly TextBox _confirm = new() { Bounds = new(16, 196, 200, 26) };
    private readonly Label _status = new() { Bounds = new(16, 232, 440, 40), ForeColor = Color.FromArgb(0xE5, 0x48, 0x4D) };
    private readonly Button _format = new() { Text = "Format", Bounds = new(366, 288, 90, 30), Enabled = false };
    private readonly Button _cancel = new() { Text = "Cancel", Bounds = new(268, 288, 90, 30) };

    public FormatDialog(SidebarItemViewModel target, IDiskService disk, IReadOnlyList<FilesystemType> allowed)
    {
        _disk = disk;
        _device = target.Device ?? string.Empty;
        _leaf = Path.GetFileName(_device);
        _filesystems = allowed.ToList();

        this.Text = "Format device";
        this.Bounds = new(0, 0, 480, 340);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MinimizeBox = false;
        this.MaximizeBox = false;

        Ui.Outline(this);

        var size = target.TotalBytes is { } total ? $", {ValueFormat.Size(total, SizeUnit.Binary)}" : string.Empty;
        var current = string.IsNullOrEmpty(target.FileSystem) ? string.Empty : $", currently {target.FileSystem}";

        foreach (var filesystem in _filesystems)
            _filesystem.Items.Add(filesystem.Display);

        if (_filesystems.Count > 0)
        {
            // Default to the device's current filesystem where we can still create it, else the first.
            var match = _filesystems.FindIndex(f =>
                string.Equals(f.Id, target.FileSystem, StringComparison.OrdinalIgnoreCase)
                || string.Equals(f.Display, target.FileSystem, StringComparison.OrdinalIgnoreCase));
            _filesystem.SelectedIndex = match >= 0 ? match : 0;
        }
        else
        {
            _status.Text = "No mkfs tools are installed, so no filesystem can be created.";
        }

        _confirm.TextChanged += (_, _) => this.UpdateEnabled();
        _filesystem.SelectedIndexChanged += (_, _) => this.UpdateEnabled();
        _format.Click += this.OnFormat;

        this.Controls.AddRange(
            new Label
            {
                Text = $"Format {_device} ({target.Name}{current}{size})",
                Bounds = new(16, 16, 440, 22),
            },
            new Label
            {
                Text = "Everything on this device will be erased. This cannot be undone.",
                Bounds = new(16, 42, 440, 40),
                ForeColor = Color.Gray,
            },
            new Label { Text = "Filesystem", Bounds = new(16, 98, 100, 22) },
            _filesystem,
            new Label { Text = "Label", Bounds = new(16, 132, 100, 22) },
            _label,
            new Label { Text = $"Type “{_leaf}” to confirm you want to erase this device:", Bounds = new(16, 170, 440, 22) },
            _confirm,
            _status,
            _format,
            _cancel);

        this.CancelButton = _cancel;
    }

    private void UpdateEnabled() =>
        _format.Enabled = _filesystems.Count > 0
            && _filesystem.SelectedIndex >= 0
            && string.Equals(_confirm.Text.Trim(), _leaf, StringComparison.Ordinal);

    private async void OnFormat(object? sender, EventArgs e)
    {
        if (_filesystem.SelectedIndex < 0 || _filesystem.SelectedIndex >= _filesystems.Count)
            return;

        var filesystem = _filesystems[_filesystem.SelectedIndex];
        this.SetBusy(true);

        var result = await _disk.FormatAsync(_device, filesystem.Id, _label.Text);
        if (result.Success)
        {
            this.DialogResult = DialogResult.OK;
            return;
        }

        _status.Text = result.Message;
        this.SetBusy(false);
    }

    private void SetBusy(bool busy)
    {
        _confirm.Enabled = _filesystem.Enabled = _label.Enabled = !busy;
        if (busy)
        {
            _format.Enabled = false;
            _status.Text = "Formatting…";
        }
        else
        {
            this.UpdateEnabled();
        }
    }

    public static Task<bool> RequestAsync(
        Form owner, SidebarItemViewModel target, IDiskService disk, IReadOnlyList<FilesystemType> allowed) =>
        Task.FromResult(new FormatDialog(target, disk, allowed).ShowDialog(owner) == DialogResult.OK);
}
