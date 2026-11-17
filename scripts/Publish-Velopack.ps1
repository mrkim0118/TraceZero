param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$PackId = "TechArchive.TraceZero",
    [string]$PackTitle = "TraceZero",
    [string]$Channel = "win",
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [string]$MainExe = "TraceZero.exe",
    [string]$Authors = "Manggdy",
    [string]$DefaultUpdateSource = "https://tech.trendarchive.io/Utility/TraceZero/",
    [string]$PublishDir = "",
    [string]$OutputDir = "",
    [string]$ReleaseNotes = "",
    # Code signing: subject of a code-signing certificate in Cert:\CurrentUser\My. Empty string skips signing.
    [string]$SignSubject = "CN=Manggdy",
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Resolve-FullPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BasePath,
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw "Path value cannot be empty."
    }

    if ([System.IO.Path]::IsPathRooted($Value)) {
        return [System.IO.Path]::GetFullPath($Value)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $BasePath $Value))
}

function Convert-ToAssemblyVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SemanticVersion
    )

    $coreVersion = $SemanticVersion.Split('-', 2)[0]
    $parts = @($coreVersion.Split('.', [System.StringSplitOptions]::RemoveEmptyEntries))

    if ($parts.Count -eq 0) {
        throw "Version '$SemanticVersion' is not valid."
    }

    while ($parts.Count -lt 4) {
        $parts += "0"
    }

    if ($parts.Count -gt 4) {
        $parts = $parts[0..3]
    }

    foreach ($part in $parts) {
        if ($part -notmatch '^\d+$') {
            throw "Version '$SemanticVersion' contains a non-numeric core segment."
        }
    }

    return ($parts -join '.')
}

function Convert-ToFileSafeName {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $safe = $Value -replace '[^A-Za-z0-9]+', '-'
    return $safe.Trim('-')
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-FullPath -BasePath $scriptRoot -Value ".."
$projectPath = Resolve-FullPath -BasePath $repoRoot -Value "TraceZero.csproj"

if ([string]::IsNullOrWhiteSpace($PublishDir)) {
    $PublishDir = "artifacts\publish\$Version\$Runtime"
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = "artifacts\velopack\Releases"
}

if ([string]::IsNullOrWhiteSpace($ReleaseNotes)) {
    $ReleaseNotes = "artifacts\velopack\release-notes-$Version.md"
}

$publishDirPath = Resolve-FullPath -BasePath $repoRoot -Value $PublishDir
$outputDirPath = Resolve-FullPath -BasePath $repoRoot -Value $OutputDir
$releaseNotesPath = Resolve-FullPath -BasePath $repoRoot -Value $ReleaseNotes
$assemblyVersion = Convert-ToAssemblyVersion -SemanticVersion $Version
$defaultsFilePath = Join-Path $publishDirPath "app-defaults.json"

New-Item -ItemType Directory -Force -Path $publishDirPath | Out-Null
New-Item -ItemType Directory -Force -Path $outputDirPath | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $releaseNotesPath) | Out-Null

if (Test-Path $publishDirPath) {
    Get-ChildItem -LiteralPath $publishDirPath -Force | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

if (-not (Test-Path $releaseNotesPath)) {
    @(
        "# TraceZero $Version"
        ""
        "- Initial packaged release."
    ) | Set-Content -Path $releaseNotesPath -Encoding UTF8
}

Write-Host "1/4 dotnet tool restore"
Push-Location $repoRoot
try {
    dotnet tool restore --ignore-failed-sources
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet tool restore failed."
    }

    Write-Host "2/4 dotnet restore"
    dotnet restore $projectPath -r $Runtime --ignore-failed-sources
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed."
    }

    Write-Host "3/4 dotnet publish"
    dotnet publish $projectPath `
        -c $Configuration `
        -r $Runtime `
        --self-contained false `
        --no-restore `
        -p:Version=$Version `
        -p:AssemblyVersion=$assemblyVersion `
        -p:FileVersion=$assemblyVersion `
        -p:InformationalVersion=$Version `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -p:PublishSingleFile=false `
        -o $publishDirPath
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed."
    }

    if ([string]::IsNullOrWhiteSpace($DefaultUpdateSource)) {
        $DefaultUpdateSource = $outputDirPath
    }

    @{
        DefaultUpdateSource = $DefaultUpdateSource
    } | ConvertTo-Json | Set-Content -Path $defaultsFilePath -Encoding UTF8

    Get-ChildItem -LiteralPath $publishDirPath -Filter "*.pdb" -File -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue

    $signArgs = @()
    if (-not [string]::IsNullOrWhiteSpace($SignSubject)) {
        $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
            Where-Object { $_.Subject -eq $SignSubject -and $_.NotAfter -gt (Get-Date) } |
            Sort-Object NotAfter -Descending |
            Select-Object -First 1
        if (-not $cert) {
            throw "No valid code-signing certificate '$SignSubject' in Cert:\CurrentUser\My. Pass -SignSubject '' to build unsigned."
        }
        $signArgs = @("--signParams", "/fd sha256 /sha1 $($cert.Thumbprint) /s My /tr $TimestampUrl /td sha256")
        Write-Host "Signing with $SignSubject ($($cert.Thumbprint))"
    }

    Write-Host "4/4 vpk pack"
    dotnet tool run vpk pack @signArgs `
        --packId $PackId `
        --packVersion $Version `
        --packDir $publishDirPath `
        --mainExe $MainExe `
        --packAuthors $Authors `
        --packTitle $PackTitle `
        --channel $Channel `
        --runtime $Runtime `
        --releaseNotes $releaseNotesPath `
        --outputDir $outputDirPath `
        --icon (Join-Path $repoRoot "Assets\TraceZero.ico") `
        --shortcuts "Desktop,StartMenuRoot"
    if ($LASTEXITCODE -ne 0) {
        throw "vpk pack failed."
    }
}
finally {
    Pop-Location
}

$setupFileName = "$PackId-$Channel-Setup.exe"
$setupFilePath = Join-Path $outputDirPath $setupFileName
if (Test-Path $setupFilePath) {
    $versionedSetupName = "{0}-Setup-{1}.exe" -f (Convert-ToFileSafeName -Value $PackTitle), $Version
    $versionedSetupPath = Join-Path $outputDirPath $versionedSetupName
    Copy-Item -LiteralPath $setupFilePath -Destination $versionedSetupPath -Force
    Write-Host "Versioned setup : $versionedSetupPath"
}

Write-Host ""
Write-Host "Velopack release complete."
Write-Host "Publish files : $publishDirPath"
Write-Host "Release feed  : $outputDirPath"
Write-Host "Update source : $DefaultUpdateSource"
