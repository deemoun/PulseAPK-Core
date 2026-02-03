$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$avaloniaCsprojPath = Join-Path $repoRoot "src/PulseAPK.Avalonia/PulseAPK.Avalonia.csproj"
$coreCsprojPath = Join-Path $repoRoot "src/PulseAPK.Core/PulseAPK.Core.csproj"
$aboutPath = Join-Path $repoRoot "src/PulseAPK.Core/ViewModels/AboutViewModel.cs"

$csprojContent = Get-Content -Raw -Path $avaloniaCsprojPath
$versionMatch = [regex]::Match($csprojContent, "<Version>([^<]+)</Version>")

if (-not $versionMatch.Success) {
    Write-Error "Unable to determine current version from $avaloniaCsprojPath."
    exit 1
}

$currentVersion = $versionMatch.Groups[1].Value

if ($currentVersion -notmatch "^1\.2\.(\d+)$") {
    Write-Error "Version '$currentVersion' is not in the expected 1.2.x format."
    exit 1
}

$patch = [int]$Matches[1] + 1
$nextVersion = "1.2.$patch"

$csprojContent = $csprojContent -replace "<Version>[^<]+</Version>", "<Version>$nextVersion</Version>"
Set-Content -Path $avaloniaCsprojPath -Value $csprojContent

$coreContent = Get-Content -Raw -Path $coreCsprojPath
$coreContent = $coreContent -replace "<Version>[^<]+</Version>", "<Version>$nextVersion</Version>"
Set-Content -Path $coreCsprojPath -Value $coreContent

$aboutContent = Get-Content -Raw -Path $aboutPath
$aboutContent = $aboutContent -replace "\?\? ""1\.2\.\d+""", "?? ""$nextVersion"""
Set-Content -Path $aboutPath -Value $aboutContent

Write-Host "Bumped version to $nextVersion."
