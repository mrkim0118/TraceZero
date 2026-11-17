namespace TraceZero.Models;

public enum SecureDeleteStage
{
    Preparing,
    AlternateStreams,
    OverwritePass,
    Truncating,
    MetadataCleanup,
    Renaming,
    Deleting,
    Completed,
}

public enum SecureDeleteOutcome
{
    Erased,

    // Compressed, encrypted or sparse file: the original data may still sit in free space until it is wiped.
    ErasedWithResidueRisk,

    // A symbolic link: only the link was removed, the file it points to was not touched.
    LinkRemoved,
}

public sealed record SecureDeleteProgress(
    SecureDeleteStage Stage,
    double Percent,
    int CurrentStep = 0,
    int TotalSteps = 0);
