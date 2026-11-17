# TraceZero

Windows WPF utility for best-effort secure deletion of multiple files.

**Author:** Manggdy

## Requirements

- Windows 10 or 11 (x64)
- .NET 8 Desktop Runtime x64
- Administrator rights only for the optional trace cleanup (one UAC prompt)

## Recommended use

1. Add files or drop folders, then click **Start erase**.
2. Answer **Yes** to trace cleanup to remove shadow copies, the USN journal, and run SSD TRIM. This deletes all System Restore points on that drive.
3. Run **Wipe free** on the same drive to overwrite traces of earlier deletions. Close other programs first: the drive is briefly full.
4. On SSDs, keep BitLocker (or other full-disk encryption) on for a strong guarantee.

## Features

- Select multiple files at once with the file picker.
- Drag and drop files or folders into the window.
- Overwrite file contents (up to the end of the last cluster) with one random pass before deletion (NIST SP 800-88).
- Clean NTFS alternate data streams, timestamps, and file size metadata.
- Remove only the link for symbolic links; never follow junctions when scanning dropped folders.
- Flag compressed, encrypted, and sparse files whose old data may remain in free space.
- Optional trace cleanup with one UAC prompt: delete shadow copies, delete the USN journal, and TRIM the SSD.
- Erase the matching Explorer Recent shortcut; warn about cloud-synced folders.
- Wipe free space on a selected drive, including free NTFS MFT records.
- Obfuscate the file name multiple times before the final delete.
- Check for Velopack updates from the installed app.
- Review live progress and export the deletion log.

## Important note

This app improves on normal deletion, but it cannot promise a strict forensic guarantee of "unrecoverable forever":

- SSDs remap writes. Run trace cleanup (TRIM) after erasing; full-disk encryption such as BitLocker is the only strong guarantee.
- Cloud-sync folders keep copies in the service's recycle bin and version history.
- Not cleaned: Explorer thumbnail cache (`thumbcache_*.db`, locked by Explorer; use Disk Cleanup > Thumbnails), the Windows Search index, `$LogFile`, pagefile and hiberfil.
- The free-space MFT pass stops early if a directory index cannot grow while the drive is full.

## Run

```powershell
dotnet run --project TraceZero.csproj
```

## Test

```powershell
dotnet test tests\TraceZero.Tests
```

## Build installer

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Publish-Velopack.ps1 -Version 1.1.6
```

The installer is framework-dependent and requires the .NET 8 Desktop Runtime x64 on the target PC. Release packages do not include PDB debug symbols.

The script creates:

- `artifacts\publish\<version>\win-x64`
- `artifacts\velopack\Releases`
- a `Setup.exe` installer
- an `app-defaults.json` pointing to `https://tech.trendarchive.io/Utility/TraceZero/` by default
