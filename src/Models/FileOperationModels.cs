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
