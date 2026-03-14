param(
    [string]$Configuration = $(if ($env:CONFIGURATION) { $env:CONFIGURATION } else { "Release" }),
    [string]$Rid = $(if ($env:RID) { $env:RID } else { "win-x64" }),
    [string]$AppName = $(if ($env:APP_NAME) { $env:APP_NAME } else { "PulseAPK" })
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectPath = Join-Path $repoRoot "src/PulseAPK.Avalonia/PulseAPK.Avalonia.csproj"

$appExe = "$AppName.exe"

$outRoot = Join-Path $repoRoot "artifacts/windows/$Rid"
$publishDir = Join-Path $outRoot "publish"
$zipPath = Join-Path $outRoot "PulseAPK-$Rid.zip"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "dotnet is required but was not found in PATH."
    exit 1
}

if ($Rid -notlike "win-*") {
    Write-Error "RID must target Windows (for example 'win-x64'). Received '$Rid'."
    exit 1
}

if (Test-Path $publishDir) {
    Remove-Item -Path $publishDir -Recurse -Force
}
New-Item -Path $publishDir -ItemType Directory -Force | Out-Null

& dotnet publish $projectPath `
    -c $Configuration `
    -r $Rid `
    --self-contained true `
    /p:UseAppHost=true `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:EnableCompressionInSingleFile=true `
    -o $publishDir

$targetExePath = Join-Path $publishDir $appExe

if (-not (Test-Path $targetExePath)) {
    $exeCandidates = Get-ChildItem -Path $publishDir -Filter "*.exe" -File

    if ($exeCandidates.Count -ne 1) {
        Write-Error "Expected executable '$AppName.exe' was not found in $publishDir."
        Write-Host "Detected executables:"
        $exeCandidates | ForEach-Object { Write-Host $_.FullName }
        exit 1
    }

    $appExe = $exeCandidates[0].Name
    $targetExePath = $exeCandidates[0].FullName
    Write-Host "Expected '$AppName.exe' was not found; using discovered executable '$appExe'."
}

$fs = [System.IO.File]::OpenRead($targetExePath)
try {
    $header = New-Object byte[] 2
    $bytesRead = $fs.Read($header, 0, 2)
} finally {
    $fs.Dispose()
}

if ($bytesRead -lt 2 -or $header[0] -ne 0x4D -or $header[1] -ne 0x5A) {
    Write-Error "Published file '$appExe' is not a valid Windows executable (MZ/PE header missing)."
    exit 1
}

if (Get-Command zip -ErrorAction SilentlyContinue) {
    if (Test-Path $zipPath) {
        Remove-Item -Path $zipPath -Force
    }

    Push-Location $publishDir
    try {
        & zip -r $zipPath .
    } finally {
        Pop-Location
    }

    Write-Host "Windows package created: $zipPath"
} elseif (Get-Command Compress-Archive -ErrorAction SilentlyContinue) {
    if (Test-Path $zipPath) {
        Remove-Item -Path $zipPath -Force
    }

    Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force
    Write-Host "Windows package created: $zipPath"
} else {
    Write-Host "No ZIP utility found (zip/Compress-Archive). Skipping archive creation."
}

Write-Host "Windows executable created: $(Join-Path $publishDir $appExe)"
