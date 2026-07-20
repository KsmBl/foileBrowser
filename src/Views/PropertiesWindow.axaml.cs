using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FoileBrowser.Models;
using FoileBrowser.Services;

namespace FoileBrowser.Views;

/// <summary>Shows details about the selected item — opened with Alt+Enter (PRD §6.1).</summary>
public partial class PropertiesWindow : Window
{
    private readonly CancellationTokenSource _cts = new();

    public PropertiesWindow()
    {
        InitializeComponent();
    }

    private readonly IApplicationService? _apps;
    private readonly string _path = string.Empty;

    public PropertiesWindow(FileSystemEntry entry, IDirectorySizeService sizes, IApplicationService? apps = null)
    {
        InitializeComponent();
        _apps = apps;
        _path = entry.FullPath;

        var isDir = entry.IsDirectory;
        GlyphText.Text = entry.Kind switch
        {
            FileSystemEntryKind.Drive => "\U0001F5B4",     // 🖴
            FileSystemEntryKind.Directory => "\U0001F4C1",  // 📁
            _ => "\U0001F4C4",                              // 📄
        };
        NameText.Text = entry.Name;
        TypeText.Text = entry.Kind switch
        {
            FileSystemEntryKind.Drive => "Drive",
            FileSystemEntryKind.Directory => "Folder",
            _ => string.IsNullOrEmpty(entry.Extension) ? "File" : $"{entry.Extension.ToUpperInvariant()} file",
        };
        LocationText.Text = Path.GetDirectoryName(entry.FullPath) is { Length: > 0 } dir ? dir : "—";
        PathText.Text = entry.FullPath;
        ModifiedText.Text = entry.Modified?.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss") ?? "—";

        PopulateFilesystemFacts(entry, isDir);

        if (!isDir)
            SizeText.Text = entry.Size is { } s ? $"{ValueFormat.Size(s, SizeUnit.Binary)} ({s:N0} bytes)" : "—";
        else if (Directory.Exists(entry.FullPath))
        {
            SizeText.Text = "computing…";
            _ = ComputeFolderSizeAsync(entry.FullPath, sizes);
        }
        else
            SizeText.Text = "—";

        if (!isDir && _apps is { SupportsAssociations: true })
            _ = LoadDefaultAppAsync();
    }

    /// <summary>
    /// Fills the "Opens with" picker with the applications registered for this file's type and
    /// preselects the current default (PRD §6.9). Runs off the constructor so the window shows at once.
    /// </summary>
    private async Task LoadDefaultAppAsync()
    {
        if (_apps is null)
            return;

        var candidates = await _apps.GetCandidatesAsync(_path);
        if (_cts.IsCancellationRequested)
            return;

        var mime = await _apps.GetTypeAsync(_path);
        if (_cts.IsCancellationRequested)
            return;

        if (candidates.Count == 0)
        {
            DefaultAppStatus.Text = mime.Length > 0
                ? $"No installed application is registered for {mime}."
                : "This file's type could not be determined.";
            DefaultAppStatus.IsVisible = true;
            return;
        }

        var current = await _apps.GetDefaultAsync(_path);
        if (_cts.IsCancellationRequested)
            return;

        DefaultAppBox.ItemsSource = candidates;
        DefaultAppBox.SelectedItem = candidates.FirstOrDefault(a => a.Id == current?.Id) ?? candidates[0];
        DefaultAppRow.IsVisible = true;
        SetDefaultButton.IsEnabled = true;
        if (mime.Length > 0)
        {
            DefaultAppStatus.Text = $"Applies to every {mime} file.";
            DefaultAppStatus.IsVisible = true;
        }
    }

    private async void OnSetDefaultApp(object? sender, RoutedEventArgs e)
    {
        if (_apps is null || DefaultAppBox.SelectedItem is not DesktopApp app)
            return;

        SetDefaultButton.IsEnabled = false;
        await _apps.SetDefaultAsync(_path, app);

        // Report what actually stuck rather than assuming the write took effect.
        var now = await _apps.GetDefaultAsync(_path);
        if (_cts.IsCancellationRequested)
            return;
        DefaultAppStatus.Text = now?.Id == app.Id
            ? $"{app.Name} is now the default for this file type."
            : $"Could not make {app.Name} the default.";
        DefaultAppStatus.IsVisible = true;
        SetDefaultButton.IsEnabled = true;
    }

    private void PopulateFilesystemFacts(FileSystemEntry entry, bool isDir)
    {
        try
        {
            FileSystemInfo info = isDir ? new DirectoryInfo(entry.FullPath) : new FileInfo(entry.FullPath);
            CreatedText.Text = info.Exists ? info.CreationTime.ToString("yyyy-MM-dd HH:mm:ss") : "—";

            if (info.Exists && OperatingSystem.IsLinux())
                PermsText.Text = FormatUnixMode(File.GetUnixFileMode(entry.FullPath));
            else
                HidePermissions();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            CreatedText.Text = "—";
            HidePermissions();
        }
    }

    private void HidePermissions()
    {
        PermsLabel.IsVisible = false;
        PermsText.IsVisible = false;
    }

    private async Task ComputeFolderSizeAsync(string path, IDirectorySizeService sizes)
    {
        try
        {
            var progress = new Progress<long>(running =>
                SizeText.Text = $"{ValueFormat.Size(running, SizeUnit.Binary)} (counting…)");
            var total = await sizes.GetSizeAsync(path, progress, _cts.Token);
            if (!_cts.IsCancellationRequested)
                SizeText.Text = $"{ValueFormat.Size(total, SizeUnit.Binary)} ({total:N0} bytes)";
        }
        catch (OperationCanceledException)
        {
            // Window closed before the walk finished.
        }
    }

    private static string FormatUnixMode(UnixFileMode mode)
    {
        Span<char> chars = stackalloc char[9];
        var bits = new[]
        {
            UnixFileMode.UserRead, UnixFileMode.UserWrite, UnixFileMode.UserExecute,
            UnixFileMode.GroupRead, UnixFileMode.GroupWrite, UnixFileMode.GroupExecute,
            UnixFileMode.OtherRead, UnixFileMode.OtherWrite, UnixFileMode.OtherExecute,
        };
        const string letters = "rwxrwxrwx";
        for (var i = 0; i < 9; i++)
            chars[i] = (mode & bits[i]) != 0 ? letters[i] : '-';
        var octal = Convert.ToString((int)mode & 0x1FF, 8).PadLeft(3, '0');
        return $"{new string(chars)}  ({octal} octal)";
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _cts.Cancel();
        _cts.Dispose();
        base.OnClosed(e);
    }
}
