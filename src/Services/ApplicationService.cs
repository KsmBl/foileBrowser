using System.Diagnostics;

namespace FoileBrowser.Services;

/// <summary>
/// XDG-based implementation of <see cref="IApplicationService"/> (PRD §6.9).
///
/// On Linux the MIME type comes from <c>xdg-mime query filetype</c>, candidates from the
/// <c>MimeType=</c> lines of the installed <c>.desktop</c> files, and the default is read/written with
/// <c>xdg-mime query/default</c>. Everything is plain process + file parsing — no reflection, so it
/// stays trim/AOT-safe.
///
/// Windows and macOS don't expose an equivalent query interface without native interop, so there
/// <see cref="SupportsAssociations"/> is false and the caller falls back to the OS shell handler.
/// </summary>
public sealed class ApplicationService : IApplicationService
{
    /// <summary>Cached scan of the installed .desktop files; built once and reused.</summary>
    private IReadOnlyList<DesktopEntry>? _entries;

    public bool SupportsAssociations => OperatingSystem.IsLinux();

    public Task<string> GetTypeAsync(string path) => Task.Run(() =>
    {
        if (!OperatingSystem.IsLinux())
            return Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return Run("xdg-mime", ["query", "filetype", path]).Trim();
    });

    public async Task<IReadOnlyList<DesktopApp>> GetCandidatesAsync(string path)
    {
        if (!OperatingSystem.IsLinux())
            return [];

        var mime = await GetTypeAsync(path);
        if (mime.Length == 0)
            return [];

        return await Task.Run(IReadOnlyList<DesktopApp> () =>
        {
            var entries = LoadEntries();
            var preferred = Run("xdg-mime", ["query", "default", mime]).Trim();

            // Exact MIME matches first, then the wildcard handlers (e.g. text/* for a text editor),
            // with the registered default hoisted to the top.
            var exact = entries.Where(e => e.MimeTypes.Contains(mime));
            var wildcard = entries.Where(e => !e.MimeTypes.Contains(mime) && e.MatchesWildcard(mime));
            return [.. exact.Concat(wildcard)
                .OrderByDescending(e => e.Id == preferred)
                .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(e => new DesktopApp(e.Id, e.Name))];
        });
    }

    public async Task<DesktopApp?> GetDefaultAsync(string path)
    {
        if (!OperatingSystem.IsLinux())
            return null;

        var mime = await GetTypeAsync(path);
        if (mime.Length == 0)
            return null;

        return await Task.Run(() =>
        {
            var id = Run("xdg-mime", ["query", "default", mime]).Trim();
            if (id.Length == 0)
                return null;
            var entry = LoadEntries().FirstOrDefault(e => e.Id == id);
            return new DesktopApp(id, entry?.Name ?? id);
        });
    }

    public async Task SetDefaultAsync(string path, DesktopApp app)
    {
        if (!OperatingSystem.IsLinux())
            return;
        var mime = await GetTypeAsync(path);
        if (mime.Length == 0)
            return;
        await Task.Run(() => Run("xdg-mime", ["default", app.Id, mime]));
    }

    public Task LaunchAsync(DesktopApp app, string path) => Task.Run(() =>
    {
        if (!OperatingSystem.IsLinux())
        {
            // Elsewhere the Id is the executable itself.
            Start(app.Id, [path]);
            return;
        }

        // gio/gtk-launch resolve the .desktop entry and honour its Exec field-codes and Terminal flag;
        // fall back to running the Exec line ourselves if neither launcher is installed.
        if (Start("gio", ["launch", ResolveDesktopFile(app.Id) ?? app.Id, path]))
            return;
        if (Start("gtk-launch", [app.Id, path]))
            return;

        if (LoadEntries().FirstOrDefault(e => e.Id == app.Id) is { } entry)
        {
            var argv = entry.BuildCommand(path);
            if (argv.Count > 0)
                Start(argv[0], [.. argv.Skip(1)]);
        }
    });

    // ---- .desktop database ----

    private IReadOnlyList<DesktopEntry> LoadEntries() => _entries ??= ScanEntries();

    /// <summary>The XDG application directories, in increasing priority (user last).</summary>
    private static IEnumerable<string> ApplicationDirs()
    {
        var dirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS") is { Length: > 0 } d
            ? d.Split(':')
            : ["/usr/local/share", "/usr/share"];
        foreach (var dir in dirs)
            yield return Path.Combine(dir, "applications");

        var home = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } h
            ? h
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        yield return Path.Combine(home, "applications");
    }

    private static List<DesktopEntry> ScanEntries()
    {
        Dictionary<string, DesktopEntry> byId = [];
        foreach (var dir in ApplicationDirs())
        {
            if (!Directory.Exists(dir))
                continue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir, "*.desktop", SearchOption.AllDirectories); }
            catch (Exception) { continue; } // unreadable directory — skip it
            foreach (var file in files)
            {
                if (DesktopEntry.TryParse(file) is { } entry)
                    byId[entry.Id] = entry; // later dirs (user) override earlier ones
            }
        }
        return [.. byId.Values];
    }

    private static string? ResolveDesktopFile(string id) =>
        ApplicationDirs().Select(d => Path.Combine(d, id)).FirstOrDefault(File.Exists);

    // ---- process helpers ----

    private static string Run(string exe, string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var a in args)
                psi.ArgumentList.Add(a);
            using var proc = Process.Start(psi);
            if (proc is null)
                return string.Empty;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);
            return output;
        }
        catch (Exception)
        {
            return string.Empty; // tool not installed
        }
    }

    private static bool Start(string exe, string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
            foreach (var a in args)
                psi.ArgumentList.Add(a);
            return Process.Start(psi) is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>One parsed <c>.desktop</c> file — just the fields "Open with" needs (PRD §6.9).</summary>
internal sealed record DesktopEntry(string Id, string Name, string Exec, IReadOnlyList<string> MimeTypes)
{
    /// <summary>Whether a <c>type/*</c> registration of this entry covers <paramref name="mime"/>.</summary>
    public bool MatchesWildcard(string mime)
    {
        var slash = mime.IndexOf('/');
        if (slash <= 0)
            return false;
        var prefix = mime[..slash] + "/*";
        return MimeTypes.Contains(prefix);
    }

    /// <summary>
    /// Expands the Exec line into an argv, substituting the file into the first %f/%F/%u/%U field code
    /// (appending it when the entry declares none) and dropping the codes we don't support.
    /// </summary>
    public List<string> BuildCommand(string path)
    {
        List<string> argv = [];
        var substituted = false;
        foreach (var token in Exec.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (token)
            {
                case "%f" or "%F" or "%u" or "%U":
                    argv.Add(path);
                    substituted = true;
                    break;
                case "%i" or "%c" or "%k" or "%d" or "%D" or "%n" or "%N" or "%v" or "%m":
                    break; // deprecated / unsupported field codes
                default:
                    argv.Add(token.Trim('"'));
                    break;
            }
        }
        if (!substituted)
            argv.Add(path);
        return argv;
    }

    /// <summary>
    /// Reads the <c>[Desktop Entry]</c> group. Returns null for anything not launchable from a file
    /// manager: non-Application types, and entries flagged NoDisplay or Hidden.
    /// </summary>
    public static DesktopEntry? TryParse(string file)
    {
        string[] lines;
        try { lines = File.ReadAllLines(file); }
        catch (Exception) { return null; }

        string? name = null, exec = null, type = null;
        IReadOnlyList<string> mimes = [];
        var inEntry = false;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                if (inEntry)
                    break; // past [Desktop Entry] — later groups are actions, not the entry itself
                inEntry = line.Equals("[Desktop Entry]", StringComparison.Ordinal);
                continue;
            }
            if (!inEntry || line.Length == 0 || line.StartsWith('#'))
                continue;

            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();

            switch (key)
            {
                case "Name" when name is null: name = value; break; // plain key only, not Name[de]
                case "Exec": exec = value; break;
                case "Type": type = value; break;
                case "NoDisplay" or "Hidden" when value.Equals("true", StringComparison.OrdinalIgnoreCase):
                    return null;
                case "MimeType":
                    mimes = [.. value.Split(';', StringSplitOptions.RemoveEmptyEntries)];
                    break;
            }
        }

        if (type != "Application" || name is null || exec is null)
            return null;
        return new DesktopEntry(Path.GetFileName(file), name, exec, mimes);
    }
}
