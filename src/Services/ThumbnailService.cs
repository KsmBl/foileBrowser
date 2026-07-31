using System.Collections.Concurrent;
using FoileBrowser.Views;
using Hawkynt.NativeForms.Drawing;

namespace FoileBrowser.Services;

/// <summary>
/// Thumbnails for the gallery view (PRD §6.2), decoded off the UI thread and cached.
///
/// Each one is letterboxed into a square of <see cref="Edge"/> pixels so every cell is the same
/// size whatever the picture's aspect ratio, and so the toolkit can draw it at its natural size.
/// Work is bounded twice over: at most a handful of decodes run at once, so scrolling a folder of
/// thousands of photographs cannot saturate the disk, and the cache is an LRU so a long session
/// cannot grow without limit.
/// </summary>
public sealed class ThumbnailService
{
    /// <summary>The side of a thumbnail in pixels. Also the gallery's cell size.</summary>
    public const int Edge = 128;

    /// <summary>How many thumbnails are kept. At 128², each is 64 KB, so this is ~25 MB.</summary>
    private const int Capacity = 400;

    /// <summary>How many decodes run at once — enough to keep a disk busy, not enough to thrash it.</summary>
    private const int Parallelism = 3;

    private readonly LruCache<string, IImage> _cache = new(Capacity);
    private readonly ConcurrentDictionary<string, byte> _inFlight = new();
    private readonly SemaphoreSlim _slots = new(Parallelism, Parallelism);

    /// <summary>Raised on a worker thread once a thumbnail is ready; the view marshals it.</summary>
    public event EventHandler<string>? Ready;

    /// <summary>Whether this file is worth asking about at all — every format the library reads.</summary>
    public static bool CanRender(string path) => ImageSupport.CanDecode(path);

    /// <summary>
    /// The thumbnail if it is already decoded; otherwise null, and a decode is started unless one is
    /// already running for this path. <see cref="Ready"/> fires when it arrives.
    /// </summary>
    public IImage? Get(string path)
    {
        if (_cache.TryGet(path, out var cached))
            return cached;

        if (!CanRender(path) || !_inFlight.TryAdd(path, 0))
            return null;

        _ = Task.Run(() => this.DecodeAsync(path));
        return null;
    }

    private async Task DecodeAsync(string path)
    {
        await _slots.WaitAsync();
        try
        {
            var image = PreviewImage.Thumbnail(path, Edge);
            if (image is not null)
            {
                _cache.Set(path, image);
                this.Ready?.Invoke(this, path);
            }
        }
        finally
        {
            _slots.Release();
            _inFlight.TryRemove(path, out _);
        }
    }

    /// <summary>Forgets everything — used when the gallery is closed.</summary>
    public void Clear() => _cache.Clear();
}
