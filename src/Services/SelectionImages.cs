using FoileBrowser.Models;

namespace FoileBrowser.Services;

/// <summary>
/// Gathers the pictures a multi-item selection is asking to be shown (PRD §6.5).
/// </summary>
/// <remarks>
/// A selection of several things used to produce one slab of statistics whatever was in it, so
/// picking a handful of photographs — or the folders holding them — showed counts and byte totals and
/// no photographs at all. Files chosen directly come first, in the order they were given; folders are
/// then walked, so selecting an album shows the album.
///
/// The walk is bounded on both counts: no more than <see cref="MaxImages"/> pictures in total and no
/// deeper than <see cref="MaxDepth"/>, because a selection can just as easily be the root of a disk,
/// and a preview panel is not worth a full-tree enumeration.
/// </remarks>
public static class SelectionImages
{
    /// <summary>As many pictures as a strip can usefully step through.</summary>
    public const int MaxImages = 500;

    /// <summary>
    /// How far into a selected folder to look: its own contents, and no further.
    /// </summary>
    /// <remarks>
    /// Descending made selecting a handful of folders slow enough to notice, and for no benefit
    /// anyone asked for — what a person means by selecting a folder is that folder's pictures, not
    /// every picture anywhere beneath it. A subtree walk is also unbounded in a way a preview panel
    /// cannot afford: the same gesture on a checkout or a home directory reads tens of thousands of
    /// entries to fill a strip that shows a few.
    /// </remarks>
    public const int MaxDepth = 0;

    /// <summary>What a selection turned out to hold.</summary>
    /// <param name="Paths">The images found, directly-selected files first.</param>
    /// <param name="Folders">How many folders were selected.</param>
    /// <param name="FolderFiles">How many files those folders hold, as far as the walk went.</param>
    /// <param name="Truncated">Whether the walk stopped early on <see cref="MaxImages"/>.</param>
    public readonly record struct Result(
        IReadOnlyList<string> Paths,
        int Folders,
        int FolderFiles,
        bool Truncated);

    /// <summary>Nothing found, for a selection with no pictures anywhere in it.</summary>
    public static Result Empty { get; } = new([], 0, 0, false);

    /// <summary>Collects the previewable images in a selection.</summary>
    /// <param name="entries">What the user picked.</param>
    /// <param name="list">
    /// Lists a directory's children. Injected so this stays testable, and so the caller decides what
    /// a directory means — inside an archive it is not the filesystem.
    /// </param>
    /// <param name="cancellationToken">Abandons the walk when the selection moves on.</param>
    public static async Task<Result> CollectAsync(
        IReadOnlyList<FileSystemEntry> entries,
        Func<string, CancellationToken, Task<IReadOnlyList<FileSystemEntry>>> list,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(list);

        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var folders = 0;
        var folderFiles = 0;
        var truncated = false;

        void Take(string path)
        {
            if (paths.Count >= MaxImages)
            {
                truncated = true;
                return;
            }

            if (ImageSupport.NameAloneSaysPicture(path) && seen.Add(path))
                paths.Add(path);
        }

        // Directly-picked files first: what was clicked is what should come up.
        foreach (var entry in entries)
            if (!entry.IsDirectory)
                Take(entry.FullPath);

        async Task WalkAsync(string directory, int depth)
        {
            if (depth > MaxDepth || paths.Count >= MaxImages)
                return;

            IReadOnlyList<FileSystemEntry> children;
            try
            {
                children = await list(directory, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // A folder that cannot be listed — permissions, a device that went away — contributes
                // nothing rather than taking the whole preview down with it.
                return;
            }

            foreach (var child in children)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (paths.Count >= MaxImages)
                {
                    truncated = true;
                    return;
                }

                if (child.IsDirectory)
                {
                    if (depth < MaxDepth)
                        await WalkAsync(child.FullPath, depth + 1);
                    continue;
                }

                ++folderFiles;
                Take(child.FullPath);
            }
        }

        foreach (var entry in entries)
        {
            if (!entry.IsDirectory)
                continue;

            ++folders;
            await WalkAsync(entry.FullPath, 0);
        }

        return new(paths, folders, folderFiles, truncated);
    }
}
