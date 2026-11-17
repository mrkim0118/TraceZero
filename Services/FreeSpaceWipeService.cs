using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using TraceZero.Models;

namespace TraceZero.Services;

public sealed class FreeSpaceWipeService
{
    private const int BufferSize = 4 * 1024 * 1024;
    private const int MinimumChunkSize = 512;

    // Stays under FAT32's 4 GB per-file limit.
    private const long MaxFillFileBytes = 1024L * 1024L * 1024L;

    // shortcut: stops after this many tiny files even if free MFT records remain; raise it for volumes with millions of deleted files.
    private const int MaxMftFillFiles = 1_000_000;

    // Sizes that fit inside a 1 KB MFT record; tried largest first so each record is overwritten as fully as possible.
    private static readonly int[] ResidentFileSizes = [700, 512, 256, 1];

    private const int ErrorHandleDiskFull = unchecked((int)0x80070027);
    private const int ErrorDiskFull = unchecked((int)0x80070070);

    public async Task WipeAsync(
        string folderPath,
        IProgress<FreeSpaceWipeProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var targetFolder = Path.GetFullPath(folderPath);
        if (!Directory.Exists(targetFolder))
        {
            throw new DirectoryNotFoundException("The selected folder could not be found.");
        }

        var rootPath = Path.GetPathRoot(targetFolder);
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new InvalidOperationException("The selected drive could not be resolved.");
        }

        var drive = new DriveInfo(rootPath);
        if (!drive.IsReady)
        {
            throw new IOException("The selected drive is not ready.");
        }

        var targetBytes = Math.Max(1, drive.AvailableFreeSpace);
        var workFolder = Path.Combine(targetFolder, $".sfe-free-space-{Guid.NewGuid():N}");
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        long writtenBytes = 0;

        try
        {
            Directory.CreateDirectory(workFolder);
            File.SetAttributes(workFolder, FileAttributes.Directory | FileAttributes.Hidden);

            // Phase 1: fill every free cluster with random data until the drive reports it is full.
            var chunkSize = BufferSize;
            for (var fileIndex = 0; chunkSize >= MinimumChunkSize; fileIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Microsoft.Win32.SafeHandles.SafeFileHandle handle;
                try
                {
                    handle = File.OpenHandle(
                        Path.Combine(workFolder, $"fill-{fileIndex}.tmp"),
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        FileOptions.Asynchronous | FileOptions.WriteThrough);
                }
                catch (IOException ex) when (IsDiskFull(ex))
                {
                    break;
                }

                using (handle)
                {
                    long fileBytes = 0;
                    while (fileBytes < MaxFillFileBytes && chunkSize >= MinimumChunkSize)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var chunk = buffer.AsMemory(0, chunkSize);
                        RandomNumberGenerator.Fill(chunk.Span);
                        try
                        {
                            await RandomAccess.WriteAsync(handle, chunk, fileBytes, cancellationToken);
                        }
                        catch (IOException ex) when (IsDiskFull(ex))
                        {
                            // Less than one chunk is left: retry smaller until even the smallest write fails.
                            chunkSize /= 2;
                            continue;
                        }

                        fileBytes += chunkSize;
                        writtenBytes += chunkSize;
                        progress?.Report(new FreeSpaceWipeProgress(
                            Math.Min(95, (writtenBytes / (double)targetBytes) * 95d),
                            writtenBytes,
                            targetBytes,
                            rootPath));
                    }
                }
            }

            // Phase 2 (NTFS): small deleted files live inside MFT records, not in clusters. With the drive full the MFT
            // cannot grow, so new tiny files must reuse, and overwrite, the free records.
            if (string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
            {
                progress?.Report(new FreeSpaceWipeProgress(96, writtenBytes, targetBytes, rootPath));
                for (var index = 0; index < MaxMftFillFiles; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!TryCreateResidentFile(Path.Combine(workFolder, $"m{index}")))
                    {
                        break;
                    }
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);

            // Always free the space again, even after a failure or cancel, so the drive is not left full.
            if (Directory.Exists(workFolder))
            {
                Directory.Delete(workFolder, recursive: true);
            }
        }

        progress?.Report(new FreeSpaceWipeProgress(100, writtenBytes, targetBytes, rootPath));
    }

    private static bool TryCreateResidentFile(string path)
    {
        foreach (var size in ResidentFileSizes)
        {
            try
            {
                File.WriteAllBytes(path, RandomNumberGenerator.GetBytes(size));
                return true;
            }
            catch (IOException ex) when (IsDiskFull(ex))
            {
            }
        }

        return false;
    }

    private static bool IsDiskFull(IOException ex) => ex.HResult is ErrorDiskFull or ErrorHandleDiskFull;
}
