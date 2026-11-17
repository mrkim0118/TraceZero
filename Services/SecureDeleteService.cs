using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using TraceZero.Models;

namespace TraceZero.Services;

public sealed class SecureDeleteService
{
    private const int BufferSize = 1024 * 1024;
    private const int RenameRounds = 3;
    private const int MaxStreamPathLength = 32767;
    private static readonly DateTime MetadataBaseUtc = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // Compressed, encrypted and sparse NTFS files write new data to new clusters, so the old clusters keep the original.
    private const FileAttributes RelocatingAttributes = FileAttributes.Compressed | FileAttributes.Encrypted | FileAttributes.SparseFile;

    // Below one MFT record the data may live inside the record itself; extending it would move it out and leave the original bytes behind.
    private const long ResidentDataLimit = 1024;

    public async Task<SecureDeleteOutcome> DeleteFileAsync(
        string filePath,
        int passCount,
        IProgress<SecureDeleteProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (passCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(passCount), "Pass count must be at least 1.");
        }

        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("The file to erase could not be found.", filePath);
        }

        progress?.Report(new SecureDeleteProgress(SecureDeleteStage.Preparing, 2));

        // Opening a symbolic link for writing would overwrite the file it points to, which the user never queued.
        if (fileInfo.LinkTarget is not null)
        {
            File.Delete(fileInfo.FullName);
            progress?.Report(new SecureDeleteProgress(SecureDeleteStage.Completed, 100));
            return SecureDeleteOutcome.LinkRemoved;
        }

        var mayLeaveResidue = (fileInfo.Attributes & RelocatingAttributes) != 0;
        File.SetAttributes(fileInfo.FullName, FileAttributes.Normal);

        var alternateStreams = EnumerateAlternateDataStreams(fileInfo.FullName).ToList();
        for (var streamIndex = 0; streamIndex < alternateStreams.Count; streamIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stream = alternateStreams[streamIndex];

            if (stream.Size > 0)
            {
                var streamStartPercent = 4 + ((streamIndex / (double)alternateStreams.Count) * 8d);
                var streamEndPercent = 4 + (((streamIndex + 1d) / alternateStreams.Count) * 8d);

                await OverwritePathAsync(
                    stream.Path,
                    stream.Size,
                    passCount,
                    progress,
                    cancellationToken,
                    SecureDeleteStage.AlternateStreams,
                    streamStartPercent,
                    streamEndPercent,
                    streamIndex + 1,
                    alternateStreams.Count);
            }

            File.Delete(stream.Path);
        }

        if (fileInfo.Length > 0)
        {
            await OverwritePathAsync(
                fileInfo.FullName,
                RoundUpToClusterEnd(fileInfo.FullName, fileInfo.Length),
                passCount,
                progress,
                cancellationToken,
                SecureDeleteStage.OverwritePass,
                14,
                76);
        }

        TruncateFile(fileInfo.FullName, progress);
        ScrubFileMetadata(fileInfo.FullName, progress);
        var obfuscatedPath = ObfuscateFileName(fileInfo.FullName, progress);

        progress?.Report(new SecureDeleteProgress(SecureDeleteStage.Deleting, 96));
        File.Delete(obfuscatedPath);
        progress?.Report(new SecureDeleteProgress(SecureDeleteStage.Completed, 100));
        return mayLeaveResidue ? SecureDeleteOutcome.ErasedWithResidueRisk : SecureDeleteOutcome.Erased;
    }

    // The last cluster past end-of-file can still hold older content of this file, so overwrite up to the cluster boundary.
    private static long RoundUpToClusterEnd(string filePath, long length)
    {
        var root = Path.GetPathRoot(filePath);
        if (length < ResidentDataLimit || string.IsNullOrEmpty(root))
        {
            return length;
        }

        if (!root.EndsWith('\\'))
        {
            root += '\\';
        }

        if (!GetDiskFreeSpaceW(root, out var sectorsPerCluster, out var bytesPerSector, out _, out _))
        {
            return length;
        }

        long clusterSize = (long)sectorsPerCluster * bytesPerSector;
        return clusterSize <= 0 ? length : (length + clusterSize - 1) / clusterSize * clusterSize;
    }

    private static async Task OverwritePathAsync(
        string path,
        long length,
        int passCount,
        IProgress<SecureDeleteProgress>? progress,
        CancellationToken cancellationToken,
        SecureDeleteStage stage,
        double startPercent,
        double endPercent,
        int streamIndex = 0,
        int streamCount = 0)
    {
        if (length <= 0)
        {
            return;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);

            for (var passIndex = 0; passIndex < passCount; passIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                stream.Position = 0;

                long remaining = length;
                while (remaining > 0)
                {
                    var currentChunkSize = (int)Math.Min(buffer.Length, remaining);
                    FillBuffer(buffer.AsSpan(0, currentChunkSize), passIndex);
                    await stream.WriteAsync(buffer.AsMemory(0, currentChunkSize), cancellationToken);
                    remaining -= currentChunkSize;
                }

                await stream.FlushAsync(cancellationToken);
                var passPercent = (passIndex + 1d) / passCount;
                var percent = startPercent + (passPercent * (endPercent - startPercent));
                progress?.Report(new SecureDeleteProgress(
                    stage,
                    percent,
                    streamCount > 0 ? streamIndex : passIndex + 1,
                    streamCount > 0 ? streamCount : passCount));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private static void TruncateFile(
        string filePath,
        IProgress<SecureDeleteProgress>? progress)
    {
        progress?.Report(new SecureDeleteProgress(SecureDeleteStage.Truncating, 80));

        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.WriteThrough);

        stream.SetLength(0);
        stream.Flush(flushToDisk: true);
    }

    private static void ScrubFileMetadata(
        string filePath,
        IProgress<SecureDeleteProgress>? progress)
    {
        progress?.Report(new SecureDeleteProgress(SecureDeleteStage.MetadataCleanup, 84));

        var randomDays = RandomNumberGenerator.GetInt32(0, 9000);
        var randomSeconds = RandomNumberGenerator.GetInt32(0, 86400);
        var timestamp = MetadataBaseUtc
            .AddDays(randomDays)
            .AddSeconds(randomSeconds);

        File.SetCreationTimeUtc(filePath, timestamp);
        File.SetLastAccessTimeUtc(filePath, timestamp);
        File.SetLastWriteTimeUtc(filePath, timestamp);
    }

    private static void FillBuffer(Span<byte> buffer, int passIndex)
    {
        switch (passIndex % 3)
        {
            case 0:
                RandomNumberGenerator.Fill(buffer);
                break;
            case 1:
                buffer.Fill(0x00);
                break;
            default:
                buffer.Fill(0xFF);
                break;
        }
    }

    private static string ObfuscateFileName(
        string originalPath,
        IProgress<SecureDeleteProgress>? progress)
    {
        var currentPath = originalPath;
        var directoryPath = Path.GetDirectoryName(originalPath);

        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new InvalidOperationException("The file parent directory could not be resolved.");
        }

        for (var round = 0; round < RenameRounds; round++)
        {
            string nextPath;
            do
            {
                nextPath = Path.Combine(directoryPath, Path.GetRandomFileName().Replace(".", string.Empty));
            }
            while (File.Exists(nextPath) || Directory.Exists(nextPath));

            File.Move(currentPath, nextPath);
            currentPath = nextPath;

            var percent = 88 + (((round + 1d) / RenameRounds) * 6d);
            progress?.Report(new SecureDeleteProgress(
                SecureDeleteStage.Renaming,
                percent,
                round + 1,
                RenameRounds));
        }

        return currentPath;
    }

    private static IEnumerable<AlternateDataStreamInfo> EnumerateAlternateDataStreams(string filePath)
    {
        var handle = FindFirstStreamW(
            filePath,
            StreamInfoLevels.FindStreamInfoStandard,
            out var data,
            0);

        if (handle == InvalidHandleValue)
        {
            yield break;
        }

        try
        {
            do
            {
                var streamName = data.StreamName;
                if (string.IsNullOrWhiteSpace(streamName) ||
                    string.Equals(streamName, "::$DATA", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var alternatePath = BuildAlternateStreamPath(filePath, streamName);
                if (!string.IsNullOrWhiteSpace(alternatePath))
                {
                    yield return new AlternateDataStreamInfo(alternatePath, data.StreamSize);
                }
            }
            while (FindNextStreamW(handle, out data));
        }
        finally
        {
            FindClose(handle);
        }
    }

    private static string BuildAlternateStreamPath(string filePath, string streamName)
    {
        var dataSuffixIndex = streamName.LastIndexOf(":$DATA", StringComparison.OrdinalIgnoreCase);
        var alternateName = dataSuffixIndex > 0
            ? streamName[..dataSuffixIndex]
            : streamName;

        return alternateName.StartsWith(':') && filePath.Length + alternateName.Length < MaxStreamPathLength
            ? filePath + alternateName
            : string.Empty;
    }

    private sealed record AlternateDataStreamInfo(string Path, long Size);

    private static readonly IntPtr InvalidHandleValue = new(-1);

    private enum StreamInfoLevels
    {
        FindStreamInfoStandard = 0,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Win32FindStreamData
    {
        public long StreamSize;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 296)]
        public string StreamName;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstStreamW(
        string fileName,
        StreamInfoLevels infoLevel,
        out Win32FindStreamData findStreamData,
        uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool FindNextStreamW(
        IntPtr findStream,
        out Win32FindStreamData findStreamData);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FindClose(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetDiskFreeSpaceW(
        string rootPathName,
        out uint sectorsPerCluster,
        out uint bytesPerSector,
        out uint numberOfFreeClusters,
        out uint totalNumberOfClusters);
}
