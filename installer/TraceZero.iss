; TraceZero installer (Inno Setup 6). Built by scripts\Build-Installer.ps1, which passes
; AppVersion, SourceDir (the publish folder) and OutputDir with /D.

#ifndef AppVersion
  #error AppVersion is not defined. Run scripts\Build-Installer.ps1.
#endif

[Setup]
AppId={{1B60E44A-A68C-4FF9-94A2-D1D7D0D2013E}
AppName=TraceZero
AppVersion={#AppVersion}
AppVerName=TraceZero {#AppVersion}
AppPublisher=Manggdy
DefaultDirName={autopf}\TraceZero
DefaultGroupName=TraceZero
DisableProgramGroupPage=yes
; Machine-wide install in Program Files.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
SetupIconFile=..\Assets\TraceZero.ico
UninstallDisplayIcon={app}\TraceZero.exe
OutputDir={#OutputDir}
OutputBaseFilename=TraceZero-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
#ifdef Sign
; Build-Installer.ps1 defines the "manggdysign" tool (installer\sign.ps1, "Manggdy" certificate).
SignTool=manggdysign
SignedUninstaller=yes
#endif

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\TraceZero"; Filename: "{app}\TraceZero.exe"
Name: "{autodesktop}\TraceZero"; Filename: "{app}\TraceZero.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\TraceZero.exe"; Description: "{cm:LaunchProgram,TraceZero}"; Flags: nowait postinstall skipifsilent runasoriginaluser

; Installs the .NET 8 Desktop Runtime when it is missing (the app is framework-dependent).
#define DotNetVersion "8"
#include "WindowsDesktopRuntime.iss"
