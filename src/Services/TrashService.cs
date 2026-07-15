using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace FoileBrowser.Services;

/// <summary>
/// Cross-platform OS-trash implementation: Recycle Bin (Windows), <c>gio trash</c> with an
/// XDG spec fallback (Linux), and Finder trash via <c>osascript</c> (macOS). See PRD §6.3.
/// </summary>
public sealed class TrashService : ITrashService
{
    public Task TrashAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            throw new FileNotFoundException("Nothing to trash at path.", path);

        return Task.Run(() =>
        {
            if (OperatingSystem.IsWindows())
                TrashWindows(path);
            else if (OperatingSystem.IsMacOS())
                TrashMac(path);
            else
                TrashLinux(path);
        }, cancellationToken);
    }

    // ---- Windows: SHFileOperation with FOF_ALLOWUNDO sends to the Recycle Bin ----

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShFileOpStruct fileOp);

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_SILENT = 0x0004;

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void TrashWindows(string path)
    {
        var op = new ShFileOpStruct
        {
            wFunc = FO_DELETE,
            pFrom = path + '\0' + '\0', // pFrom must be double-null terminated
            fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT),
        };

        var result = SHFileOperation(ref op);
        if (result != 0)
            throw new IOException($"Recycle Bin operation failed (code {result}) for “{path}”.");
    }

    // ---- macOS: ask Finder to move the item to the trash ----

    private static void TrashMac(string path)
    {
        var script = $"tell application \"Finder\" to delete POSIX file \"{path.Replace("\"", "\\\"")}\"";
        RunOrThrow("osascript", ["-e", script]);
    }

    // ---- Linux: prefer gio, otherwise implement the XDG trash spec directly ----

    private static void TrashLinux(string path)
    {
        if (TryRun("gio", ["trash", "--", path]))
            return;

        TrashXdg(path);
    }

    private static void TrashXdg(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(dataHome))
            dataHome = Path.Combine(home, ".local", "share");

        var trashDir = Path.Combine(dataHome, "Trash");
        var filesDir = Path.Combine(trashDir, "files");
        var infoDir = Path.Combine(trashDir, "info");
        Directory.CreateDirectory(filesDir);
        Directory.CreateDirectory(infoDir);

        // Resolve a non-colliding name in the trash.
        var baseName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        var targetName = baseName;
        var counter = 1;
        while (File.Exists(Path.Combine(filesDir, targetName))
               || Directory.Exists(Path.Combine(filesDir, targetName))
               || File.Exists(Path.Combine(infoDir, targetName + ".trashinfo")))
        {
            targetName = $"{Path.GetFileNameWithoutExtension(baseName)}.{counter++}{Path.GetExtension(baseName)}";
        }

        var infoBuilder = new StringBuilder();
        infoBuilder.AppendLine("[Trash Info]");
        infoBuilder.AppendLine($"Path={Uri.EscapeDataString(Path.GetFullPath(path)).Replace("%2F", "/")}");
        infoBuilder.AppendLine($"DeletionDate={DateTime.Now:yyyy-MM-ddTHH:mm:ss}");
        File.WriteAllText(Path.Combine(infoDir, targetName + ".trashinfo"), infoBuilder.ToString());

        // Move the payload last so a failure leaves no dangling info file behind pointing nowhere.
        if (Directory.Exists(path))
            Directory.Move(path, Path.Combine(filesDir, targetName));
        else
            File.Move(path, Path.Combine(filesDir, targetName));
    }

    private static bool TryRun(string fileName, string[] args)
    {
        try
        {
            return RunOrThrow(fileName, args, throwOnMissing: false);
        }
        catch
        {
            return false;
        }
    }

    private static bool RunOrThrow(string fileName, string[] args, bool throwOnMissing = true)
    {
        var psi = new ProcessStartInfo(fileName) { RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Exception) when (!throwOnMissing)
        {
            return false; // tool not installed
        }

        if (process is null)
            return !throwOnMissing ? false : throw new IOException($"Could not start {fileName}.");

        process.WaitForExit();
        if (process.ExitCode == 0)
            return true;

        if (throwOnMissing)
            throw new IOException($"{fileName} failed: {process.StandardError.ReadToEnd()}".Trim());
        return false;
    }
}
