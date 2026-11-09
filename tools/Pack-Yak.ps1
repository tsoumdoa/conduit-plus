#Requires -Version 7
<#
.SYNOPSIS
Build, test and verify a local Windows Yak release candidate. Never publishes.
.DESCRIPTION
With -Version, first bumps <Version> in Directory.Build.props and commits that change
("Release VERSION") on a clean working tree, then builds the candidate from that commit.
.EXAMPLE
./tools/Pack-Yak.ps1 -Version 0.2.0-beta.1
.EXAMPLE
./tools/Pack-Yak.ps1   # repackage the version already in Directory.Build.props
#>
param(
    [string]$Version,
    [string]$YakPath = $(if ($env:YAK_PATH) { $env:YAK_PATH } else { 'C:\Program Files\Rhino 8\System\Yak.exe' }),
    [switch]$StageOnly,
    [switch]$AllowDirty
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'ReleaseVersion.ps1')

if ($Version) {
    Assert-ReleaseVersion $Version
    if ($AllowDirty) { throw '-Version commits the bump; it cannot be combined with -AllowDirty.' }
    if (@(& git -C $repo status --porcelain).Count -gt 0) { throw 'Working tree is dirty. Commit or stash changes before bumping the version.' }
    $current = Get-ReleaseVersion $repo
    if ((Compare-ReleaseVersion $Version $current) -le 0) { throw "New version $Version must be greater than the current $current." }
    & git -C $repo rev-parse -q --verify "refs/tags/v$Version" | Out-Null
    if ($LASTEXITCODE -eq 0) { throw "Tag v$Version already exists." }
    Set-ReleaseVersion $repo $Version
    & git -C $repo add -- Directory.Build.props
    if ($LASTEXITCODE -ne 0) { throw 'Cannot stage Directory.Build.props.' }
    # The pre-commit hook builds and tests this commit (Debug); a failure leaves the bump uncommitted.
    & git -C $repo commit -m "Release $Version" -- Directory.Build.props
    if ($LASTEXITCODE -ne 0) { throw "Version bump commit failed; Directory.Build.props now says $Version but is not committed." }
    Write-Output "Bumped $current -> $Version and committed."
}

$version = Get-ReleaseVersion $repo
$manifest = Get-Content -LiteralPath (Join-Path $repo 'packaging/manifest.yml') -Raw
if ([regex]::Matches($manifest, '(?m)^version: "@VERSION@"\r?$').Count -ne 1) {
    throw 'Expected exactly one version: "@VERSION@" placeholder in packaging/manifest.yml.'
}
if (-not $StageOnly -and -not (Test-Path -LiteralPath $YakPath -PathType Leaf)) { throw "Yak executable not found: $YakPath" }
$status = @(& git -C $repo status --porcelain)
if ($LASTEXITCODE -ne 0) { throw 'Cannot read Git working tree status.' }
$dirty = $status.Count -gt 0
if ($dirty -and -not $AllowDirty) { throw 'Working tree is dirty. Commit/stash changes, or use -AllowDirty for a local trial candidate.' }
$commit = & git -C $repo rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot read source commit.' }

& dotnet build (Join-Path $repo 'ConduitPlus.sln') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
& dotnet test (Join-Path $repo 'ConduitPlus.sln') -c Release --no-build --nologo
if ($LASTEXITCODE -ne 0) { throw 'Release tests failed.' }

$output = Join-Path $repo 'artifacts/yak'
$stage = [IO.Path]::GetFullPath((Join-Path $output 'stage'))
# Verify the resolved absolute workspace path before recursively replacing generated staging files.
$expected = [IO.Path]::Combine([IO.Path]::GetFullPath($repo), 'artifacts', 'yak', 'stage')
if ($stage -ne $expected -or -not $stage.StartsWith([IO.Path]::GetFullPath($repo) + [IO.Path]::DirectorySeparatorChar)) {
    throw "Unsafe staging path: $stage"
}
$rhinoOutput = Join-Path $repo 'src/ConduitPlus.Rhino/bin/Release/net8.0-windows'
$ghOutput = Join-Path $repo 'src/ConduitPlus.Grasshopper/bin/Release/net8.0-windows'
$payload = @{
    'ConduitPlus.Rhino.rhp' = Join-Path $rhinoOutput 'ConduitPlus.Rhino.rhp'
    'ConduitPlus.Display.dll' = Join-Path $rhinoOutput 'ConduitPlus.Display.dll'
    'ConduitPlus.Core.dll' = Join-Path $rhinoOutput 'ConduitPlus.Core.dll'
    'ConduitPlus.Grasshopper.gha' = Join-Path $ghOutput 'ConduitPlus.Grasshopper.gha'
    'LICENSE' = Join-Path $repo 'LICENSE'
    'README.md' = Join-Path $repo 'packaging/README.md'
    'icon.png' = Join-Path $repo 'assets/icon.png'
}
foreach ($source in $payload.Values) {
    if (-not (Test-Path -LiteralPath $source -PathType Leaf) -or (Get-Item -LiteralPath $source).Length -eq 0) {
        throw "Missing or empty packaging input: $source"
    }
    if ([IO.Path]::GetExtension($source) -in @('.rhp', '.gha', '.dll')) {
        $actual = [Diagnostics.FileVersionInfo]::GetVersionInfo($source).ProductVersion
        if ($actual -cne $version) { throw "Assembly version mismatch in ${source}: expected $version, found $actual" }
    }
}
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null
foreach ($name in $payload.Keys) { Copy-Item -LiteralPath $payload[$name] -Destination (Join-Path $stage $name) }
$manifest.Replace('@VERSION@', $version) | Set-Content -LiteralPath (Join-Path $stage 'manifest.yml') -Encoding utf8
if ($StageOnly) {
    Write-Output "Staged $version at $stage (Release build and tests passed; no Yak archive built)."
    return
}
Push-Location -LiteralPath $stage
try {
    & $YakPath build --platform win
    if ($LASTEXITCODE -ne 0) { throw 'Yak build failed.' }
} finally { Pop-Location }
$packages = @(Get-ChildItem -LiteralPath $stage -Filter '*.yak' -File)
if ($packages.Count -ne 1) { throw "Expected one Yak package, found $($packages.Count)." }
$package = $packages[0].FullName
& (Join-Path $PSScriptRoot 'Test-YakPackage.ps1') -Package $package -Version $version -SourceDirectory $stage
$candidateOutput = if ($dirty) { Join-Path $output 'trials' } else { $output }
New-Item -ItemType Directory -Path $candidateOutput -Force | Out-Null
$destination = Join-Path $candidateOutput $packages[0].Name
Copy-Item -LiteralPath $package -Destination $destination -Force
$hash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $($packages[0].Name)" | Set-Content -LiteralPath "$destination.sha256" -Encoding ascii
$files = foreach ($name in @($payload.Keys | Sort-Object)) {
    [ordered]@{ name = $name; sha256 = (Get-FileHash -LiteralPath (Join-Path $stage $name) -Algorithm SHA256).Hash.ToLowerInvariant() }
}
[ordered]@{
    version = $version
    sourceCommit = $commit
    dirty = $dirty
    builtAtUtc = [DateTime]::UtcNow.ToString('o')
    platform = 'win'
    minimumRhino = '8.20'
    runtime = 'net8.0-windows'
    package = $packages[0].Name
    sha256 = $hash
    validation = 'Release build, unit tests, archive inventory, assembly versions and staged payload hashes passed'
    manualRhinoValidation = 'pending'
    files = @($files)
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$destination.json" -Encoding utf8
Write-Output "Local candidate: $destination"
Write-Output "SHA-256: $hash"
Write-Output 'No package was published. Install this exact archive in Rhino, then publish it with tools/Publish-Yak.ps1 (see docs/releasing.md).'
