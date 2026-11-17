using System.Diagnostics;
using System.IO;
using TraceZero.Models;
using TraceZero.Services;
using Xunit;

namespace TraceZero.Tests;

public sealed class SecureDeleteServiceTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("sfe-test-").FullName;
    private readonly SecureDeleteService _service = new();

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task ErasesFileAndAlternateStreamsWithoutLeavingRenamedFiles()
    {
        var path = Path.Combine(_folder, "secret.txt");
        await File.WriteAllTextAsync(path, new string('x', 5000));
        await File.WriteAllTextAsync(path + ":hidden", "alternate stream secret");

        var outcome = await _service.DeleteFileAsync(path, 1, null, CancellationToken.None);

        Assert.Equal(SecureDeleteOutcome.Erased, outcome);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_folder));
    }

    [Fact]
    public async Task RemovesOnlyTheLinkAndLeavesItsTargetIntact()
    {
        var target = Path.Combine(_folder, "target.txt");
        var link = Path.Combine(_folder, "link.txt");
        await File.WriteAllTextAsync(target, "keep me");
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return; // shortcut: symlinks need Developer Mode or admin; this case is not checked without them.
        }

        var outcome = await _service.DeleteFileAsync(link, 1, null, CancellationToken.None);

        Assert.Equal(SecureDeleteOutcome.LinkRemoved, outcome);
        Assert.False(File.Exists(link));
        Assert.Equal("keep me", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task FlagsCompressedFileBecauseOldClustersMayRemain()
    {
        var path = Path.Combine(_folder, "compressed.txt");
        await File.WriteAllTextAsync(path, new string('y', 100_000));
        using (var compact = Process.Start(new ProcessStartInfo("compact.exe", $"/c \"{path}\"") { CreateNoWindow = true })!)
        {
            await compact.WaitForExitAsync();
        }

        if (!File.GetAttributes(path).HasFlag(FileAttributes.Compressed))
        {
            return; // The temp volume does not support NTFS compression.
        }

        var outcome = await _service.DeleteFileAsync(path, 1, null, CancellationToken.None);

        Assert.Equal(SecureDeleteOutcome.ErasedWithResidueRisk, outcome);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_folder));
    }
}
