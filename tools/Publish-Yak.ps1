#Requires -Version 7
<#
.SYNOPSIS
Publish the exact, already tested Yak candidates built by Pack-Yak.ps1. Never rebuilds.
.DESCRIPTION
Publishes the Windows candidate and, unless -SkipMac, the macOS candidate of the same version
(pack both with ./tools/Pack-Yak.ps1 and ./tools/Pack-Yak.ps1 -Platform mac from the same commit).
Each candidate is checked against its validation record (clean source, checksum, HEAD = source
commit, archive contents), then:
  -Server Test    pushes to the Yak test server (cleared nightly) and searches for it.
  -Server Public  tags vVERSION, pushes to the public Yak server, pushes the tag and creates a
                  GitHub release with each .yak, checksum and validation record attached.
-NotesFile adds release notes above GitHub's generated change list.
Public versions cannot be overwritten. Run Public only when the owner has approved the release.
Sign in once with: & 'C:\Program Files\Rhino 8\System\Yak.exe' login
.EXAMPLE
./tools/Publish-Yak.ps1 -Server Test
.EXAMPLE
./tools/Publish-Yak.ps1 -Server Public -NotesFile release-notes.md
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)][ValidateSet('Test', 'Public')][string]$Server,
    [string]$Version,
    [string]$NotesFile,
    [switch]$SkipMac,
    [string]$YakPath = $(if ($env:YAK_PATH) { $env:YAK_PATH } else { 'C:\Program Files\Rhino 8\System\Yak.exe' })
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'ReleaseVersion.ps1')
if (-not $Version) { $Version = Get-ReleaseVersion $repo }
Assert-ReleaseVersion $Version
if (-not (Test-Path -LiteralPath $YakPath -PathType Leaf)) { throw "Yak executable not found: $YakPath" }
if ($NotesFile -and -not (Test-Path -LiteralPath $NotesFile -PathType Leaf)) { throw "Release notes file not found: $NotesFile" }

$head = & git -C $repo rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot read HEAD.' }

function Get-Candidate([string]$Platform) {
    $name = "conduit-plus-$Version-rh8_20-$Platform.yak"
    $package = Join-Path $repo "artifacts/yak/$name"
    $recordPath = "$package.json"
    if (-not (Test-Path -LiteralPath $package) -or -not (Test-Path -LiteralPath $recordPath)) {
        $how = if ($Platform -eq 'mac') { './tools/Pack-Yak.ps1 -Platform mac (or pass -SkipMac)' } else { './tools/Pack-Yak.ps1' }
        throw "No clean $Platform candidate for $Version in artifacts/yak. Run $how first."
    }
    $record = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
    if ($record.version -cne $Version) { throw "$Platform validation record is for $($record.version), not $Version." }
    $recorded = if ($record.PSObject.Properties['platform']) { $record.platform } else { 'win' }
    if ($recorded -cne $Platform) { throw "$name has a validation record for platform '$recorded'." }
    if ($record.dirty -ne $false) { throw "Do not publish a dirty trial candidate ($name)." }
    $hash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -cne $record.sha256) { throw "$name checksum differs from its validation record." }
    if ($head -ne $record.sourceCommit) { throw "$name was built from $($record.sourceCommit), but HEAD is $head." }
    # Validation is read-only; keep -WhatIf from leaking into the validator's pipeline.
    & { $WhatIfPreference = $false; & (Join-Path $PSScriptRoot 'Test-YakPackage.ps1') -Package $package -Version $Version -Platform $Platform } | Out-Host
    [pscustomobject]@{ Platform = $Platform; Name = $name; Package = $package; Record = $recordPath; Hash = $hash; Commit = $record.sourceCommit }
}

$candidates = @(Get-Candidate 'win')
if (-not $SkipMac) { $candidates += Get-Candidate 'mac' }
$names = ($candidates.Name) -join ', '

function Invoke-Checked([string]$What, [scriptblock]$Command) {
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$What failed (exit $LASTEXITCODE)." }
}

if ($Server -eq 'Test') {
    $source = 'https://test.yak.rhino3d.com'
    if (-not $PSCmdlet.ShouldProcess("$names -> $source", 'yak push')) { return }
    foreach ($candidate in $candidates) { Invoke-Checked "Yak test push ($($candidate.Platform))" { & $YakPath push --source $source $candidate.Package } }
    Invoke-Checked 'Yak test search' { & $YakPath search --source $source --all --prerelease conduit-plus }
    Write-Output "Pushed $names to the Yak test server (cleared nightly)."
    return
}

$branch = & git -C $repo symbolic-ref -q --short HEAD
if ($branch -ne 'main') { throw "Publish public releases from main (current: $branch)." }
$tag = "v$Version"
$prerelease = $Version.Contains('-')
if (-not $PSCmdlet.ShouldProcess("$names (public Yak, tag $tag, GitHub release)", 'Publish publicly; this cannot be undone')) { return }

# Tag the reviewed source first. An existing tag must already point at the candidates' commit.
$existing = & git -C $repo rev-parse -q --verify "refs/tags/$tag^{commit}"
if ($LASTEXITCODE -eq 0) {
    if ($existing -ne $head) { throw "Tag $tag exists on $existing, not on the candidates' commit." }
} else {
    Invoke-Checked 'Creating the release tag' { & git -C $repo tag -a $tag $head -m "Conduit+ $Version" }
}

# Push the source first, so a published package always has its commit and tag on GitHub.
# Yak and GitHub are separate steps; a failure in one does not undo the other.
Invoke-Checked 'Pushing main' { & git -C $repo push origin HEAD:main }
Invoke-Checked 'Pushing the tag' { & git -C $repo push origin $tag }
foreach ($candidate in $candidates) {
    Invoke-Checked "Public Yak push ($($candidate.Platform))" { & $YakPath push $candidate.Package }
    Write-Output "Published $($candidate.Name) to the public Yak server."
}
Invoke-Checked 'Yak search' { & $YakPath search --all --prerelease conduit-plus }
$assets = foreach ($candidate in $candidates) { $candidate.Package; "$($candidate.Package).sha256"; $candidate.Record }
$releaseArgs = @('release', 'create', $tag) + $assets + @(
    '--repo', 'tsoumdoa/conduit-plus', '--title', "Conduit+ $Version", '--generate-notes', '--verify-tag')
if ($NotesFile) { $releaseArgs += @('--notes-file', $NotesFile) }
if ($prerelease) { $releaseArgs += '--prerelease' }
Invoke-Checked 'Creating the GitHub release' { & gh @releaseArgs }

# Download the attached archives again and confirm they are byte-identical.
$check = Join-Path ([IO.Path]::GetTempPath()) ("conduit-plus-release-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $check | Out-Null
try {
    foreach ($candidate in $candidates) {
        Invoke-Checked "Downloading the $($candidate.Platform) release asset" { & gh release download $tag --repo tsoumdoa/conduit-plus --pattern $candidate.Name --dir $check }
        $downloaded = (Get-FileHash -LiteralPath (Join-Path $check $candidate.Name) -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($downloaded -cne $candidate.Hash) { throw "GitHub release asset $($candidate.Name) differs from the published candidate." }
    }
} finally { Remove-Item -LiteralPath $check -Recurse -Force }
$summary = ($candidates | ForEach-Object { "$($_.Platform) SHA-256 $($_.Hash)" }) -join '; '
Write-Output "Released ${Version}: public Yak packages, tag $tag and GitHub release ($summary)."
