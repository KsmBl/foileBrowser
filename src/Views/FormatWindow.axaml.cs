using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FoileBrowser.Models;
using FoileBrowser.Services;
using FoileBrowser.ViewModels;

namespace FoileBrowser.Views;

/// <summary>
/// Confirms and performs a destructive format of a block device (PRD §6.10). The user must retype the
/// device leaf name (e.g. "sdb1") to enable the Format button; the work runs as root via pkexec.
/// </summary>
public partial class FormatWindow : Window
{
    private readonly IDiskService _disk = null!;
    private readonly string _device = string.Empty;
    private readonly string _leaf = string.Empty;
    private readonly List<FilesystemType> _filesystems = [];

    public FormatWindow()
    {
        InitializeComponent();
    }

    public FormatWindow(SidebarItemViewModel target, IDiskService disk, IReadOnlyList<FilesystemType> allowed)
    {
        InitializeComponent();
        _disk = disk;
        _device = target.Device ?? string.Empty;
        _leaf = Path.GetFileName(_device);
        _filesystems = allowed.ToList();

        var size = target.TotalBytes is { } t ? $", {ValueFormat.Size(t, SizeUnit.Binary)}" : string.Empty;
        var current = string.IsNullOrEmpty(target.FileSystem) ? string.Empty : $", currently {target.FileSystem}";
        TargetText.Text = $"Format {_device} ({target.Name}{current}{size})";
        ConfirmPrompt.Text = $"Type “{_leaf}” to confirm you want to erase this device:";

        foreach (var fs in _filesystems)
            FsBox.Items.Add(fs.Display);
        if (_filesystems.Count > 0)
        {
            // Default to the device's current filesystem where we can still create it, else the first.
            var match = _filesystems.FindIndex(f =>
                string.Equals(f.Id, target.FileSystem, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(f.Display, target.FileSystem, System.StringComparison.OrdinalIgnoreCase));
            FsBox.SelectedIndex = match >= 0 ? match : 0;
        }
        else
        {
            StatusText.Text = "No mkfs tools are installed, so no filesystem can be created.";
            StatusText.IsVisible = true;
        }
    }

    private void OnConfirmChanged(object? sender, TextChangedEventArgs e) => UpdateFormatEnabled();

    private void OnFsChanged(object? sender, SelectionChangedEventArgs e) => UpdateFormatEnabled();

    private void UpdateFormatEnabled() =>
        FormatButton.IsEnabled = _filesystems.Count > 0
            && FsBox.SelectedIndex >= 0
            && string.Equals(ConfirmBox.Text?.Trim(), _leaf, System.StringComparison.Ordinal);

    private async void OnFormat(object? sender, RoutedEventArgs e)
    {
        if (FsBox.SelectedIndex < 0 || FsBox.SelectedIndex >= _filesystems.Count)
            return;

        var fs = _filesystems[FsBox.SelectedIndex];
        SetBusy(true);
        var result = await _disk.FormatAsync(_device, fs.Id, LabelBox.Text);
        if (result.Success)
        {
            Close(true);
            return;
        }

        StatusText.Text = result.Message;
        StatusText.IsVisible = true;
        SetBusy(false);
    }

    private void SetBusy(bool busy)
    {
        Busy.IsVisible = busy;
        ConfirmBox.IsEnabled = FsBox.IsEnabled = LabelBox.IsEnabled = !busy;
        if (busy)
        {
            FormatButton.IsEnabled = false;
            StatusText.IsVisible = false;
        }
        else
        {
            UpdateFormatEnabled();
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
