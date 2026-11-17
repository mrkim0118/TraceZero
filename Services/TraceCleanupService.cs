using System.Diagnostics;
using System.IO;

namespace TraceZero.Services;

// Removes copies and traces of erased files that live outside the file itself. Needs administrator rights,
// so it runs the built-in Windows tools in one elevated cmd (one UAC prompt).
public sealed class TraceCleanupService
{
    private const int ErrorCancelled = 1223;

    private static readonly string[] CloudFolderMarkers = [@"\Dropbox\", @"\Google Drive\", @"\iCloudDrive\", @"\Box\"];

    /// <returns>The tools' combined output, or null when the user declined the UAC prompt.</returns>
    public async Task<string?> RunElevatedAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken)
    {
        var drives = filePaths
            .Select(Path.GetPathRoot)
            .Where(root => !string.IsNullOrEmpty(root) && root.Length >= 2 && root[1] == ':')
            .Select(root => root![..2].ToUpperInvariant())
            .Distinct()
            .Where(drive => new DriveInfo(drive).DriveType is DriveType.Fixed or DriveType.Removable)
            .ToList();

        if (drives.Count == 0)
        {
            return string.Empty;
        }

        // Order matters: snapshots hold full old copies, the USN journal holds old file names, and TRIM last tells an SSD
        // that every freed block (including the two above) may be erased.
        var commands = drives.SelectMany(drive => new[]
        {
            $"echo [{drive}] vssadmin delete shadows",
            $"vssadmin delete shadows /for={drive} /all /quiet",
            $"echo [{drive}] fsutil usn deletejournal",
            $"fsutil usn deletejournal /d {drive}",
            $"echo [{drive}] defrag /L (TRIM)",
            $"defrag {drive} /L",
        });

        var outputPath = Path.Combine(Path.GetTempPath(), $"sfe-cleanup-{Guid.NewGuid():N}.log");
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c ({string.Join(" & ", commands)}) > \"{outputPath}\" 2>&1",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The cleanup process could not be started.");
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return null;
        }

        try
        {
            return File.Exists(outputPath) ? await File.ReadAllTextAsync(outputPath, cancellationToken) : string.Empty;
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    public static bool IsInCloudSyncFolder(string path)
    {
        var oneDriveRoots = new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" }
            .Select(Environment.GetEnvironmentVariable)
            .Where(root => !string.IsNullOrEmpty(root));

        return oneDriveRoots.Any(root => path.StartsWith(root!.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
            || CloudFolderMarkers.Any(marker => path.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    // Explorer's Recent folder keeps a shortcut named after the file, which records its full path.
    public static string GetRecentShortcutPath(string filePath) =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Recent),
            Path.GetFileName(filePath) + ".lnk");
}
