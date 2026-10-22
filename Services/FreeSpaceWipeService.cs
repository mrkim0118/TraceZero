using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using SecureFileEraser.Models;

namespace SecureFileEraser.Services;

public sealed class FreeSpaceWipeService
{
    private const int BufferSize = 4 * 1024 * 1024;
    private const long MinimumReserveBytes = 256L * 1024L * 1024L;

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

        var initialFreeBytes = drive.AvailableFreeSpace;
        var reserveBytes = Math.Max(MinimumReserveBytes, initialFreeBytes / 100);
        var targetBytes = Math.Max(0, initialFreeBytes - reserveBytes);

        if (targetBytes <= 0)
        {
            throw new IOException("There is not enough free space to wipe safely.");
        }

        var tempFilePath = Path.Combine(targetFolder, $".sfe-free-space-{Guid.NewGuid():N}.tmp");
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        long writtenBytes = 0;

        try
        {
            await using (var stream = new FileStream(
                tempFilePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough))
            {
                File.SetAttributes(tempFilePath, FileAttributes.Hidden | FileAttributes.Temporary);

                while (writtenBytes < targetBytes)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var currentFreeBytes = new DriveInfo(rootPath).AvailableFreeSpace;
                    if (currentFreeBytes <= reserveBytes)
                    {
                        break;
                    }

                    var remainingTargetBytes = targetBytes - writtenBytes;
                    var remainingSafeBytes = currentFreeBytes - reserveBytes;
                    var chunkSize = (int)Math.Min(buffer.Length, Math.Min(remainingTargetBytes, remainingSafeBytes));
                    if (chunkSize <= 0)
                    {
                        break;
                    }

                    RandomNumberGenerator.Fill(buffer.AsSpan(0, chunkSize));
                    await stream.WriteAsync(buffer.AsMemory(0, chunkSize), cancellationToken);
                    writtenBytes += chunkSize;

                    progress?.Report(new FreeSpaceWipeProgress(
                        Math.Min(99, (writtenBytes / (double)targetBytes) * 100d),
                        writtenBytes,
                        targetBytes,
                        rootPath));
                }

                await stream.FlushAsync(cancellationToken);
            }
        }
        catch (IOException) when (writtenBytes > 0)
        {
            // A full-drive write commonly ends with an IOException; cleanup below is the important part.
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);

            if (File.Exists(tempFilePath))
            {
                File.SetAttributes(tempFilePath, FileAttributes.Normal);
                File.Delete(tempFilePath);
            }
        }

        progress?.Report(new FreeSpaceWipeProgress(100, writtenBytes, targetBytes, rootPath));
    }
}
