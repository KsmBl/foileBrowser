namespace FoileBrowser.Models;

public enum FileOperationKind
{
    Copy,
    Move,
}

/// <summary>How to resolve a name collision at the destination (PRD §6.3 conflict dialog).</summary>
public enum ConflictResolution
{
    Overwrite,
    Skip,
    Rename,
    Cancel,
}

/// <summary>A pending collision handed to the conflict resolver.</summary>
public sealed record ConflictRequest(string SourcePath, string DestinationPath);

/// <summary>Snapshot of an in-flight transfer's progress, reported to the UI.</summary>
public sealed record OperationProgress(long BytesTotal, long BytesDone, string CurrentItem)
{
    public double Fraction => BytesTotal <= 0 ? 0 : Math.Clamp((double)BytesDone / BytesTotal, 0, 1);
}

/// <summary>
/// How the copy engine moves bytes for a file (PRD §6.3 "blazing fast" transfers).
/// </summary>
public enum CopyStrategy
{
    /// <summary>Pick per transfer from the source/destination device characteristics.</summary>
    Auto,

    /// <summary>Double-buffered: read the next block while writing the current one. Best for SSDs
    /// and cross-device transfers where concurrent read+write does not contend.</summary>
    Overlapped,

    /// <summary>Read a large block fully, then write it — no interleaving. Best for a single
    /// mechanical/optical spindle, where alternating read/write seeks thrash the head.</summary>
    Sequential,
}

/// <summary>
/// Tunables for the copy engine (PRD §6.3). Buffer sizes are configurable and the strategy can be
/// forced; <see cref="CopyStrategy.Auto"/> profiles the drives per transfer.
/// </summary>
public sealed record CopyOptions
{
    /// <summary>Chunk size for the overlapped (double-buffered) path.</summary>
    public int BufferSize { get; init; } = 1 << 20; // 1 MiB

    /// <summary>Block size for the sequential-slurp path on mechanical/optical media.</summary>
    public int SequentialBufferSize { get; init; } = 8 << 20; // 8 MiB

    public CopyStrategy Strategy { get; init; } = CopyStrategy.Auto;

    public static CopyOptions Default { get; } = new();
}
