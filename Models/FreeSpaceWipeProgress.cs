namespace SecureFileEraser.Models;

public sealed record FreeSpaceWipeProgress(
    double Percent,
    long WrittenBytes,
    long TargetBytes,
    string RootPath);
