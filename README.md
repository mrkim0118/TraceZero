# Secure File Eraser

Windows WPF utility for best-effort secure deletion of multiple files.

## Features

- Select multiple files at once with the file picker.
- Drag and drop files or folders into the window.
- Overwrite file contents with 7 passes before deletion.
- Clean NTFS alternate data streams, timestamps, and file size metadata.
- Wipe free space on a selected drive.
- Obfuscate the file name multiple times before the final delete.
- Check for Velopack updates from the installed app.
- Review live progress and export the deletion log.

## Important note

This app improves on normal deletion, but SSDs, cloud-sync folders, RAID, snapshots, and journaling file systems can still prevent a strict forensic guarantee of "unrecoverable forever."

## Run

```powershell
dotnet run --project C:\Users\mrkim\Desktop\Program\SecureFileEraser\SecureFileEraser.csproj
```

## Build installer

```powershell
powershell -ExecutionPolicy Bypass -File C:\Users\mrkim\Desktop\Program\SecureFileEraser\scripts\Publish-Velopack.ps1 -Version 1.1.0
```

The installer is framework-dependent and requires the .NET 8 Desktop Runtime x64 on the target PC. Release packages do not include PDB debug symbols.

The script creates:

- `artifacts\publish\<version>\win-x64`
- `artifacts\velopack\Releases`
- a `Setup.exe` installer
- an `app-defaults.json` pointing to `https://tech.trendarchive.io/Utility/SecureFileEraser/` by default
