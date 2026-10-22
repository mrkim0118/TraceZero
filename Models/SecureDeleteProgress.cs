namespace SecureFileEraser.Models;

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

public sealed record SecureDeleteProgress(
    SecureDeleteStage Stage,
    double Percent,
    int CurrentStep = 0,
    int TotalSteps = 0);
