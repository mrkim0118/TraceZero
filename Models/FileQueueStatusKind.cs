namespace SecureFileEraser.Models;

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
    Cancelled,
    Failed,
}
