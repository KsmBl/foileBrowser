using System.Drawing;
using FoileBrowser.Models;
using FoileBrowser.Services;
using Hawkynt.NativeForms;

namespace FoileBrowser.Views;

/// <summary>Details about the selected item — opened with Alt+Enter (PRD §6.1/§6.9).</summary>
public sealed class PropertiesDialog : Form
{
    private const int LabelWidth = 110;
    private const int ValueLeft = 130;
    private const int ValueWidth = 340;
    private const int RowStep = 26;

    private readonly CancellationTokenSource _cts = new();
    private readonly IApplicationService? _apps;
    private readonly string _path;

    private readonly Label _size = Value();
    private readonly Label _permissions = Value();
    private readonly Label _permissionsLabel = Caption("Permissions");

    private readonly ComboBox _defaultApp = new() { Bounds = new(ValueLeft, 0, 220, 26), Visible = false };
    private readonly Button _setDefault = new() { Text = "Set default", Bounds = new(ValueLeft + 228, 0, 110, 26), Visible = false, Enabled = false };
    private readonly Label _defaultStatus = new() { Bounds = new(16, 0, 460, 40), ForeColor = Color.Gray };

    public PropertiesDialog(FileSystemEntry entry, IDirectorySizeService sizes, IApplicationService? apps)
    {
        _apps = apps;
        _path = entry.FullPath;

        this.Text = "Properties";
        this.Bounds = new(0, 0, 500, 470);
        this.StartPosition = FormStartPosition.CenterParent;

        var type = entry.Kind switch
        {
            FileSystemEntryKind.Drive => "Drive",
            FileSystemEntryKind.Directory => "Folder",
            _ => string.IsNullOrEmpty(entry.Extension) ? "File" : $"{entry.Extension.ToUpperInvariant()} file",
        };

        Ui.Outline(this);

        this.Controls.Add(new PictureBox
        {
            Image = Icons.For(entry.Kind),
            Bounds = new(16, 14, Icons.Size, Icons.Size),
            SizeMode = PictureBoxSizeMode.CenterImage,
        });
        this.Controls.Add(new Label
        {
            Text = entry.Name,
            Bounds = new(16 + Icons.Size + 8, 14, 430, 26),
        });

        var y = 50;
        this.AddRow("Type", type, ref y);
        this.AddRow("Location", Path.GetDirectoryName(entry.FullPath) is { Length: > 0 } dir ? dir : "—", ref y);
        this.AddRow("Full path", entry.FullPath, ref y);
        this.AddRow("Modified", entry.Modified?.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss") ?? "—", ref y);
        this.AddRow("Created", ReadCreated(entry), ref y);

        this.Place(Caption("Size"), _size, ref y);
        this.Place(_permissionsLabel, _permissions, ref y);

        _permissions.Text = ReadPermissions(entry, out var hasPermissions);
        _permissionsLabel.Visible = _permissions.Visible = hasPermissions;

        y += 8;
        this.Place(Caption("Opens with"), _defaultApp, ref y);
        _setDefault.Bounds = new(ValueLeft + 228, _defaultApp.Top, 110, 26);
        _defaultStatus.Bounds = new(16, y, 460, 40);
        this.Controls.AddRange(_setDefault, _defaultStatus);

        var close = new Button { Text = "Close", Bounds = new(386, 396, 90, 30), DialogResult = DialogResult.OK };
        this.Controls.Add(close);
        this.AcceptButton = close;
        this.CancelButton = close;

        _setDefault.Click += this.OnSetDefault;

        this.ShowSize(entry, sizes);
        if (!entry.IsDirectory && _apps is { SupportsAssociations: true })
            _ = this.LoadDefaultAppAsync();

        this.FormClosed += (_, _) =>
        {
            _cts.Cancel();
            _cts.Dispose();
        };
    }

    // ---- row helpers ----

    private static Label Caption(string text) => new()
    {
        Text = text,
        Bounds = new(16, 0, LabelWidth, 22),
        ForeColor = Color.Gray,
    };

    private static Label Value() => new() { Bounds = new(ValueLeft, 0, ValueWidth, 22) };

    private void AddRow(string caption, string text, ref int y)
    {
        var value = Value();
        value.Text = text;
        this.Place(Caption(caption), value, ref y);
    }

    private void Place(Control caption, Control value, ref int y)
    {
        caption.Bounds = new Rectangle(caption.Left, y, caption.Width, caption.Height);
        value.Bounds = new Rectangle(value.Left, y, value.Width, value.Height);
        this.Controls.AddRange(caption, value);
        y += RowStep;
    }

    // ---- size ----

    private void ShowSize(FileSystemEntry entry, IDirectorySizeService sizes)
    {
        if (!entry.IsDirectory)
        {
            _size.Text = entry.Size is { } bytes
                ? $"{ValueFormat.Size(bytes, SizeUnit.Binary)} ({bytes:N0} bytes)"
                : "—";
            return;
        }

        if (!Directory.Exists(entry.FullPath))
        {
            _size.Text = "—";
            return;
        }

        _size.Text = "computing…";
        _ = this.ComputeFolderSizeAsync(entry.FullPath, sizes);
    }

    private async Task ComputeFolderSizeAsync(string path, IDirectorySizeService sizes)
    {
        try
        {
            var progress = new Progress<long>(running =>
                _size.Text = $"{ValueFormat.Size(running, SizeUnit.Binary)} (counting…)");
            var total = await sizes.GetSizeAsync(path, progress, _cts.Token);
            if (!_cts.IsCancellationRequested)
                _size.Text = $"{ValueFormat.Size(total, SizeUnit.Binary)} ({total:N0} bytes)";
        }
        catch (OperationCanceledException)
        {
            // The window closed before the walk finished.
        }
    }

    // ---- filesystem facts ----

    private static string ReadCreated(FileSystemEntry entry)
    {
        try
        {
            FileSystemInfo info = entry.IsDirectory
                ? new DirectoryInfo(entry.FullPath)
                : new FileInfo(entry.FullPath);
            return info.Exists ? info.CreationTime.ToString("yyyy-MM-dd HH:mm:ss") : "—";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return "—";
        }
    }

    private static string ReadPermissions(FileSystemEntry entry, out bool available)
    {
        available = false;
        if (!OperatingSystem.IsLinux())
            return string.Empty;

        try
        {
            if (!File.Exists(entry.FullPath) && !Directory.Exists(entry.FullPath))
                return string.Empty;
            available = true;
            return FormatUnixMode(File.GetUnixFileMode(entry.FullPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            available = false;
            return string.Empty;
        }
    }

    private static string FormatUnixMode(UnixFileMode mode)
    {
        Span<char> chars = stackalloc char[9];
        ReadOnlySpan<UnixFileMode> bits =
        [
            UnixFileMode.UserRead, UnixFileMode.UserWrite, UnixFileMode.UserExecute,
            UnixFileMode.GroupRead, UnixFileMode.GroupWrite, UnixFileMode.GroupExecute,
            UnixFileMode.OtherRead, UnixFileMode.OtherWrite, UnixFileMode.OtherExecute,
        ];
        const string letters = "rwxrwxrwx";
        for (var i = 0; i < 9; ++i)
            chars[i] = (mode & bits[i]) != 0 ? letters[i] : '-';
        var octal = Convert.ToString((int)mode & 0x1FF, 8).PadLeft(3, '0');
        return $"{new string(chars)}  ({octal} octal)";
    }

    // ---- default application (PRD §6.9) ----

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
            _defaultStatus.Text = mime.Length > 0
                ? $"No installed application is registered for {mime}."
                : "This file's type could not be determined.";
            return;
        }

        var current = await _apps.GetDefaultAsync(_path);
        if (_cts.IsCancellationRequested)
            return;

        _defaultApp.DisplaySelector = static o => ((DesktopApp)o!).Name;
        _defaultApp.Items.Clear();
        foreach (var app in candidates)
            _defaultApp.Items.Add(app);
        _defaultApp.SelectedItem = candidates.FirstOrDefault(a => a.Id == current?.Id) ?? candidates[0];

        _defaultApp.Visible = true;
        _setDefault.Visible = true;
        _setDefault.Enabled = true;
        if (mime.Length > 0)
            _defaultStatus.Text = $"Applies to every {mime} file.";
    }

    private async void OnSetDefault(object? sender, EventArgs e)
    {
        if (_apps is null || _defaultApp.SelectedItem is not DesktopApp app)
            return;

        _setDefault.Enabled = false;
        await _apps.SetDefaultAsync(_path, app);

        // Report what actually stuck rather than assuming the write took effect.
        var now = await _apps.GetDefaultAsync(_path);
        if (_cts.IsCancellationRequested)
            return;

        _defaultStatus.Text = now?.Id == app.Id
            ? $"{app.Name} is now the default for this file type."
            : $"Could not make {app.Name} the default.";
        _setDefault.Enabled = true;
    }

    public static Task ShowAsync(Form owner, FileSystemEntry entry, IDirectorySizeService sizes, IApplicationService? apps)
    {
        new PropertiesDialog(entry, sizes, apps).ShowDialog(owner);
        return Task.CompletedTask;
    }
}
