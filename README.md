<p align="center">
  <img src="Assets/TraceZero.png" width="128" alt="TraceZero icon">
</p>

<h1 align="center">TraceZero</h1>

<p align="center">Windows utility that securely erases files so they cannot be recovered.</p>

<p align="center">Author: <b>Manggdy</b></p>

<p align="center"><b>English</b> | <a href="README.ko.md">한국어</a></p>

TraceZero overwrites a file's contents, cleans its metadata and name, then deletes it. Optional trace cleanup and free-space wiping remove what normal deletion leaves behind.

## Features

- **Add files**: Select multiple files at once, or drag and drop files and folders into the window.
- **Overwrite before delete**: Overwrite file contents (up to the end of the last cluster) with one random pass before deletion (NIST SP 800-88).
- **Metadata cleanup**: Clean NTFS alternate data streams, timestamps, and file size metadata.
- **Name obfuscation**: Rename the file multiple times before the final delete.
- **Safe link handling**: Remove only the link for symbolic links; never follow junctions when scanning dropped folders.
- **Risk flags**: Flag compressed, encrypted, and sparse files whose old data may remain in free space.
- **Trace cleanup** (optional, one UAC prompt): Delete shadow copies, delete the USN journal, and TRIM the SSD.
- **Recent items and cloud folders**: Erase the matching Explorer Recent shortcut; warn about cloud-synced folders.
- **Free-space wipe**: Wipe free space on a selected drive, including free NTFS MFT records.
- **Progress and log**: Compact window with live progress, status per file, and an exportable log.
- **Korean / English**: Switch the interface language.
- **Update check**: On launch, checks the latest GitHub release and asks whether to download and install the new version.

### Verification

A 2 MB test file filled with a unique marker was erased, then the clusters it used were read directly from the raw volume (bypassing every cache), the same way recovery tools look for data.

| Marker copies found | HDD | SSD (NVMe) |
|---|---|---|
| Before deletion | 48,770 | 48,770 |
| After normal Windows delete | 48,770 (fully recoverable) | 48,770 (fully recoverable) |
| After TraceZero overwrite, before delete | 0 | 0 |
| After TraceZero erase | 0 | 0 |

This checks what software can read from the drive. It does not cover chip-level SSD forensics; use trace cleanup (TRIM) and full-disk encryption for that.

## Requirements

- Windows 10 or 11 (x64)
- .NET 8 Desktop Runtime x64 (the installer downloads and installs it when it is missing)
- Administrator rights only for the optional trace cleanup (one UAC prompt)

## Usage

1. Add files or drop folders, then click **Start erase**.
2. Answer **Yes** to trace cleanup to remove shadow copies, the USN journal, and run SSD TRIM. This deletes all System Restore points on that drive.
3. Run **Wipe free** on the same drive to overwrite traces of earlier deletions. Close other programs first: the drive is briefly full.
4. On SSDs, keep BitLocker (or other full-disk encryption) on for a strong guarantee.

## Data & Settings

| Data | Location |
|---|---|
| Startup log | `%LocalAppData%\TechArchive\TraceZero\startup.log` |

## Limitations

TraceZero improves greatly on normal deletion, but no app can promise a strict forensic guarantee of "unrecoverable forever":

- SSDs remap writes. Run trace cleanup (TRIM) after erasing; full-disk encryption such as BitLocker is the only strong guarantee.
- Cloud-sync folders keep copies in the service's recycle bin and version history.
- Not cleaned: Explorer thumbnail cache (`thumbcache_*.db`, locked by Explorer; use Disk Cleanup > Thumbnails), the Windows Search index, `$LogFile`, pagefile and hiberfil.
- The free-space MFT pass stops early if a directory index cannot grow while the drive is full.

## Build from Source

Requires the .NET 8 SDK.

### Build

```powershell
dotnet run --project TraceZero.csproj
```

### Test

```powershell
dotnet test tests\TraceZero.Tests
```

### Release

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Build-Installer.ps1
```

The script publishes the app (framework-dependent, without PDB files) to `artifacts\publish`, signs it with the `CN=Manggdy` certificate, and builds `artifacts\installer\TraceZero-Setup-<version>.exe` with [Inno Setup 6](https://jrsoftware.org/isinfo.php). The version comes from `<Version>` in `TraceZero.csproj`. The installer installs to `C:\Program Files\TraceZero` and installs the .NET 8 Desktop Runtime when it is missing. Pass `-NoSign` to build without a certificate.

## Project Layout

| Path | Contents |
|---|---|
| `MainWindow.xaml` | Main window (WPF) |
| `Services/SecureDeleteService.cs` | Overwrite, metadata cleanup and delete |
| `Services/TraceCleanupService.cs` | Shadow copies, USN journal and TRIM |
| `Services/FreeSpaceWipeService.cs` | Free-space and MFT wipe |
| `Services/GitHubUpdater.cs` | GitHub release update check |
| `Services/UiTextProvider.cs` | Korean / English UI text |
| `Models/` | Queue items and progress models |
| `tests/TraceZero.Tests/` | Tests |
| `installer/TraceZero.iss` | Inno Setup installer script |
| `scripts/Build-Installer.ps1` | Release build and installer packaging |

## Libraries

- [Inno Setup](https://jrsoftware.org/isinfo.php): installer
