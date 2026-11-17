namespace TraceZero.Models;

public enum FileQueueStatusKind
{
    Queued,
    Missing,
    Preparing,
    AlternateStreams,
    OverwritePass,
    Truncating,
    MetadataCleanup,
    Renaming,
    Deleting,
    Completed,
    CompletedWithResidue,
    Cancelled,
    Failed,
}
