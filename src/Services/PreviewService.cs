using FoileBrowser.Models;

namespace FoileBrowser.Services;

/// <inheritdoc />
public sealed class PreviewService : IPreviewService
{
    private const int MaxTextBytes = 64 * 1024;
    private const int FolderListLimit = 60;

    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { "png", "jpg", "jpeg", "gif", "bmp", "webp", "ico" };

    // Extensions we always treat as text even if large; other files are sniffed for binary bytes.
    private static readonly HashSet<string> TextExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "txt", "md", "log", "json", "xml", "yaml", "yml", "csv", "ini", "cfg", "conf",
            "cs", "js", "ts", "py", "java", "c", "cpp", "h", "hpp", "go", "rs", "rb", "php",
            "html", "css", "sh", "bat", "ps1", "sql", "toml", "gitignore", "editorconfig",
        };

    public Task<PreviewResult> CreateAsync(FileSystemEntry entry, CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            if (entry.IsDirectory)
                return PreviewFolder(entry, cancellationToken);
            return PreviewFile(entry);
        }, cancellationToken);

    private static PreviewResult PreviewFolder(FileSystemEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            var names = new List<string>();
            int files = 0, folders = 0;
            foreach (var info in new DirectoryInfo(entry.FullPath).EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var isDir = (info.Attributes & FileAttributes.Directory) == FileAttributes.Directory;
                if (isDir) folders++; else files++;
                if (names.Count < FolderListLimit)
                    names.Add((isDir ? "📁 " : "📄 ") + info.Name);
            }

            return new PreviewResult
            {
                Kind = PreviewKind.Folder,
                Title = entry.Name,
                Info = $"{folders + files} items · {folders} folders · {files} files",
                Text = string.Join('\n', names),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new PreviewResult { Kind = PreviewKind.None, Title = entry.Name, Info = ex.Message };
        }
    }

    private static PreviewResult PreviewFile(FileSystemEntry entry)
    {
        var ext = entry.Extension;
        var info = DescribeFile(entry);

        if (ImageExtensions.Contains(ext))
            return new PreviewResult { Kind = PreviewKind.Image, Title = entry.Name, Info = info, ImagePath = entry.FullPath };

        var text = TryReadText(entry.FullPath, ext);
        return text is not null
            ? new PreviewResult { Kind = PreviewKind.Text, Title = entry.Name, Info = info, Text = text }
            : new PreviewResult { Kind = PreviewKind.None, Title = entry.Name, Info = info };
    }

    private static string DescribeFile(FileSystemEntry entry)
    {
        var size = entry.Size is { } s ? FormatSize(s) : "unknown size";
        var type = string.IsNullOrEmpty(entry.Extension) ? "file" : entry.Extension.ToUpperInvariant() + " file";
        var when = entry.Modified?.LocalDateTime.ToString("yyyy-MM-dd HH:mm") ?? "";
        return $"{size} · {type}{(when.Length > 0 ? " · " + when : "")}";
    }

    private static string? TryReadText(string path, string ext)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[Math.Min(MaxTextBytes, (int)Math.Min(stream.Length, MaxTextBytes))];
            var read = stream.Read(buffer, 0, buffer.Length);

            // A NUL byte is a strong signal the content is binary; skip unless a known text type.
            var known = TextExtensions.Contains(ext);
            if (!known)
            {
                for (var i = 0; i < read; i++)
                    if (buffer[i] == 0)
                        return null;
            }

            var content = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
            if (stream.Length > MaxTextBytes)
                content += "\n\n… (truncated)";
            return content;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {Units[unit]}";
    }
}
