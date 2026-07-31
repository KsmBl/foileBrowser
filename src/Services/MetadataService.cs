using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace FoileBrowser.Services;

/// <inheritdoc />
public sealed class MetadataService : IMetadataService
{
    private readonly IProvider[] _providers;
    private readonly Dictionary<string, IProvider> _byColumn;
    private readonly ConcurrentDictionary<string, string> _cache = new();
    private readonly ConcurrentDictionary<string, byte> _inflight = new();
    private readonly SemaphoreSlim _throttle = new(4);

    public MetadataService()
    {
        _providers = [new ImageHeaderProvider(), new ImageColorsProvider(), new FfprobeProvider()];
        _byColumn = _providers
            .SelectMany(p => p.ColumnIds.Select(id => (id, p)))
            .ToDictionary(x => x.id, x => x.p);
    }

    public IReadOnlyList<MetadataColumnInfo> Columns { get; } =
    [
        new("img.dimensions", "Dimensions", "Image", false, 110),
        new("img.megapixels", "MP", "Image", true, 60),
        new("img.channels", "Channels", "Image", false, 90),
        new("img.depth", "Depth", "Image", false, 70),
        new("img.colors", "Colors", "Image", true, 90),
        new("av.resolution", "Resolution", "Media", false, 110),
        new("av.fps", "FPS", "Media", true, 60),
        new("av.duration", "Duration", "Media", true, 90),
        new("av.channels", "Audio ch.", "Media", true, 80),
        new("av.bitrate", "Bitrate", "Media", true, 90),
        new("av.codec", "Codec", "Media", false, 120),
    ];

    public string Get(string path, string columnId, Action onReady)
    {
        if (!_byColumn.TryGetValue(columnId, out var provider))
            return string.Empty;

        var key = Key(path, columnId);
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        if (!provider.CanHandle(Ext(path)))
        {
            _cache[key] = string.Empty;
            return string.Empty;
        }

        Schedule(path, provider, onReady);
        return "…";
    }

    private void Schedule(string path, IProvider provider, Action onReady)
    {
        var flight = path + "\0" + provider.Category;
        if (!_inflight.TryAdd(flight, 0))
            return; // this file's category is already being read

        _ = Task.Run(async () =>
        {
            await _throttle.WaitAsync().ConfigureAwait(false);
            Dictionary<string, string> values;
            try { values = provider.Read(path); }
            catch { values = []; } // a bad/partial file simply yields blanks
            finally { _throttle.Release(); }

            foreach (var id in provider.ColumnIds)
                _cache[Key(path, id)] = values.GetValueOrDefault(id, string.Empty);
            _inflight.TryRemove(flight, out _);
            onReady();
        });
    }

    private static string Key(string path, string columnId) => path + "\0" + columnId;

    private static string Ext(string path) =>
        Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

    // ---- providers ----

    private interface IProvider
    {
        string Category { get; }
        IReadOnlyList<string> ColumnIds { get; }
        bool CanHandle(string ext);
        Dictionary<string, string> Read(string path);
    }


    /// <summary>
    /// Cheap image header read (dimensions/channels/depth) — the format's own header parser, with no
    /// full decode, so listing a folder of photographs costs kilobytes rather than megabytes.
    /// </summary>
    private sealed class ImageHeaderProvider : IProvider
    {
        /// <summary>Enough of the file for any of these formats to describe itself.</summary>
        private const int HeaderBytes = 64 * 1024;

        public string Category => "ImageHeader";
        public IReadOnlyList<string> ColumnIds { get; } = ["img.dimensions", "img.megapixels", "img.channels", "img.depth"];
        public bool CanHandle(string ext) => ImageSupport.CanDecodeExtension(ext);

        public Dictionary<string, string> Read(string path)
        {
            if (ReadHeader(path) is not { } head)
                return [];

            var format = FormatRegistry.DetectFromBytes(head);
            if (format == ImageFormat.Unknown)
                format = FormatRegistry.DetectFromExtension(Path.GetExtension(path));

            // A header reader is an optional augmentation — plenty of formats (PNG among them) do not
            // register one, so where it is missing the picture is decoded and measured. That costs a
            // decode this column was meant to avoid, which is why the answer is cached per file like
            // every other metadata value: it is paid once, off the UI thread.
            var info = FormatRegistry.GetEntry(format)?.ReadImageInfo?.Invoke(head) ?? Measure(path);
            if (info is not { } known)
                return [];

            // ColorMode is the format's own word for its layout ("RGBA", "Grayscale", "Indexed", …),
            // so it is reported as given rather than mapped onto a fixed vocabulary that would have
            // to grow a case for every one of ~580 formats.
            return new Dictionary<string, string>
            {
                ["img.dimensions"] = $"{known.Width}×{known.Height}",
                ["img.megapixels"] = ((double)known.Width * known.Height / 1_000_000).ToString("0.0"),
                ["img.channels"] = known.ColorMode ?? string.Empty,
                ["img.depth"] = known.BitsPerPixel > 0 ? $"{known.BitsPerPixel}-bit" : string.Empty,
            };
        }

        /// <summary>Decodes far enough to measure, for a format with no cheap header reader.</summary>
        private static ImageInfo? Measure(string path)
        {
            try
            {
                var raw = FormatRegistry.Read(new FileInfo(path));
                return raw is null
                    ? null
                    : new ImageInfo(raw.Width, raw.Height, BitsPerPixel(raw.Format), raw.Format.ToString());
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static int BitsPerPixel(PixelFormat format) => format switch
        {
            PixelFormat.Rgba64 or PixelFormat.Rgb48 => 48,
            PixelFormat.Gray16 => 16,
            PixelFormat.Gray8 or PixelFormat.Indexed8 => 8,
            PixelFormat.Indexed4 => 4,
            PixelFormat.Indexed1 => 1,
            PixelFormat.Rgb24 or PixelFormat.Bgr24 => 24,
            _ => 32,
        };

        /// <summary>The first chunk of a file, or null when it cannot be read.</summary>
        private static byte[]? ReadHeader(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                var buffer = new byte[(int)Math.Min(HeaderBytes, stream.Length)];
                return stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) > 0 ? buffer : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return null;
            }
        }
    }

    /// <summary>Distinct-colour count — needs a full decode, so it's a separate category (only runs when
    /// the Colors column is shown) and is capped so a huge image can't blow up memory/CPU.</summary>
    private sealed class ImageColorsProvider : IProvider
    {
        private const long MaxPixels = 4_000_000;
        private const int MaxColors = 200_000;

        public string Category => "ImageColors";
        public IReadOnlyList<string> ColumnIds { get; } = ["img.colors"];
        public bool CanHandle(string ext) => ImageSupport.CanDecodeExtension(ext);

        public Dictionary<string, string> Read(string path)
        {
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return [];
            }

            var format = FormatRegistry.DetectFromBytes(bytes);
            if (format == ImageFormat.Unknown)
                format = FormatRegistry.DetectFromExtension(Path.GetExtension(path));

            // Checked from the header first: the decode below is full-size, so a picture too big to
            // count has to be refused before its pixels exist rather than after.
            if (FormatRegistry.GetEntry(format)?.ReadImageInfo?.Invoke(bytes) is { } info
                && (long)info.Width * info.Height > MaxPixels)
                return new() { ["img.colors"] = "—" };

            RawImage? raw;
            try
            {
                raw = FormatRegistry.Read(bytes);
            }
            catch (Exception)
            {
                return [];
            }

            if (raw is null)
                return [];
            if ((long)raw.Width * raw.Height > MaxPixels)
                return new() { ["img.colors"] = "—" };

            var bgra = raw.Format == PixelFormat.Bgra32 ? raw : PixelConverter.Convert(raw, PixelFormat.Bgra32);
            var seen = new HashSet<uint>();
            foreach (var color in System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(bgra.PixelData))
            {
                seen.Add(color);
                if (seen.Count > MaxColors)
                    return new() { ["img.colors"] = $">{MaxColors:N0}" };
            }

            return new() { ["img.colors"] = seen.Count.ToString("N0") };
        }
    }

    private static readonly HashSet<string> MediaExts = new(StringComparer.Ordinal)
    {
        "mp4", "m4v", "mkv", "webm", "avi", "mov", "wmv", "flv", "mpg", "mpeg", "ts", "m2ts",
        "mp3", "flac", "wav", "aac", "ogg", "oga", "opus", "m4a", "wma", "aiff", "aif", "ape", "wv",
    };

    /// <summary>Audio/video metadata via a single ffprobe call (when ffprobe is on PATH).</summary>
    private sealed class FfprobeProvider : IProvider
    {
        private static readonly bool Available = Which("ffprobe") is not null;

        public string Category => "Media";
        public IReadOnlyList<string> ColumnIds { get; } =
            ["av.resolution", "av.fps", "av.duration", "av.channels", "av.bitrate", "av.codec"];
        public bool CanHandle(string ext) => Available && MediaExts.Contains(ext);

        public Dictionary<string, string> Read(string path)
        {
            var json = RunFfprobe(path);
            if (string.IsNullOrEmpty(json))
                return [];

            var result = new Dictionary<string, string>();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("streams", out var streams))
            {
                foreach (var s in streams.EnumerateArray())
                {
                    var codecType = Str(s, "codec_type");
                    if (codecType == "video" && !result.ContainsKey("av.resolution"))
                    {
                        var w = Str(s, "width");
                        var h = Str(s, "height");
                        if (w.Length > 0 && h.Length > 0)
                            result["av.resolution"] = $"{w}×{h}";
                        if (Fps(Str(s, "r_frame_rate")) is { } fps)
                            result["av.fps"] = fps;
                        result["av.codec"] = Str(s, "codec_name").ToUpperInvariant();
                    }
                    else if (codecType == "audio")
                    {
                        if (Str(s, "channels") is { Length: > 0 } ch)
                            result["av.channels"] = ch;
                        if (!result.ContainsKey("av.codec"))
                            result["av.codec"] = Str(s, "codec_name").ToUpperInvariant();
                    }
                }
            }

            if (root.TryGetProperty("format", out var format))
            {
                if (Duration(Str(format, "duration")) is { } dur)
                    result["av.duration"] = dur;
                if (long.TryParse(Str(format, "bit_rate"), out var br) && br > 0)
                    result["av.bitrate"] = $"{br / 1000:N0} kbps";
            }
            return result;
        }

        private static string RunFfprobe(string path)
        {
            try
            {
                var psi = new ProcessStartInfo("ffprobe")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                foreach (var arg in new[] { "-v", "quiet", "-print_format", "json", "-show_format", "-show_streams", path })
                    psi.ArgumentList.Add(arg);
                using var process = Process.Start(psi);
                if (process is null)
                    return string.Empty;
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(10_000);
                return output;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static string Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v)
                ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString()
                : string.Empty;

        private static string? Fps(string rate)
        {
            var parts = rate.Split('/');
            if (parts.Length == 2 && double.TryParse(parts[0], out var num) && double.TryParse(parts[1], out var den) && den > 0)
            {
                var fps = num / den;
                return fps > 0 ? fps.ToString(fps % 1 == 0 ? "0" : "0.00") : null;
            }
            return null;
        }

        private static string? Duration(string seconds)
        {
            if (!double.TryParse(seconds, System.Globalization.CultureInfo.InvariantCulture, out var s) || s <= 0)
                return null;
            var t = TimeSpan.FromSeconds(s);
            return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
        }
    }

    /// <summary>Locates an executable on PATH (used to detect ffprobe); null if absent.</summary>
    private static string? Which(string command)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, command);
            if (File.Exists(candidate) || File.Exists(candidate + ".exe"))
                return candidate;
        }
        return null;
    }
}
