#Requires -Version 7
<#
.SYNOPSIS
Publish the exact, already tested Yak candidate built by Pack-Yak.ps1. Never rebuilds.
.DESCRIPTION
Checks the candidate against its validation record (clean source, checksum, HEAD = source
commit, archive contents), then:
  -Server Test    pushes to the Yak test server (cleared nightly) and searches for it.
  -Server Public  tags vVERSION, pushes to the public Yak server, pushes the tag and creates a
                  GitHub release with the .yak, checksum and validation record attached.
Public versions cannot be overwritten. Run Public only when the owner has approved the release.
Sign in once with: & 'C:\Program Files\Rhino 8\System\Yak.exe' login
.EXAMPLE
./tools/Publish-Yak.ps1 -Server Test
.EXAMPLE
./tools/Publish-Yak.ps1 -Server Public
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)][ValidateSet('Test', 'Public')][string]$Server,
    [string]$Version,
    [string]$YakPath = $(if ($env:YAK_PATH) { $env:YAK_PATH } else { 'C:\Program Files\Rhino 8\System\Yak.exe' })
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'ReleaseVersion.ps1')
if (-not $Version) { $Version = Get-ReleaseVersion $repo }
Assert-ReleaseVersion $Version
if (-not (Test-Path -LiteralPath $YakPath -PathType Leaf)) { throw "Yak executable not found: $YakPath" }

$name = "conduit-plus-$Version-rh8_20-win.yak"
$package = Join-Path $repo "artifacts/yak/$name"
$recordPath = "$package.json"
if (-not (Test-Path -LiteralPath $package) -or -not (Test-Path -LiteralPath $recordPath)) {
    throw "No clean candidate for $Version in artifacts/yak. Run ./tools/Pack-Yak.ps1 first."
}
$record = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
if ($record.version -cne $Version) { throw "Validation record is for $($record.version), not $Version." }
if ($record.dirty -ne $false) { throw 'Do not publish a dirty trial candidate.' }
$hash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -cne $record.sha256) { throw 'Candidate checksum differs from its validation record.' }
$head = & git -C $repo rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot read HEAD.' }
if ($head -ne $record.sourceCommit) { throw "Candidate was built from $($record.sourceCommit), but HEAD is $head." }
# Validation is read-only; keep -WhatIf from leaking into the validator's pipeline.
& { $WhatIfPreference = $false; & (Join-Path $PSScriptRoot 'Test-YakPackage.ps1') -Package $package -Version $Version }

function Invoke-Checked([string]$What, [scriptblock]$Command) {
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$What failed (exit $LASTEXITCODE)." }
}

if ($Server -eq 'Test') {
    $source = 'https://test.yak.rhino3d.com'
    if (-not $PSCmdlet.ShouldProcess("$name -> $source", 'yak push')) { return }
    Invoke-Checked 'Yak test push' { & $YakPath push --source $source $package }
    Invoke-Checked 'Yak test search' { & $YakPath search --source $source --all --prerelease conduit-plus }
    Write-Output "Pushed $name to the Yak test server (cleared nightly)."
    return
}

$branch = & git -C $repo symbolic-ref -q --short HEAD
if ($branch -ne 'main') { throw "Publish public releases from main (current: $branch)." }
$tag = "v$Version"
$prerelease = $Version.Contains('-')
if (-not $PSCmdlet.ShouldProcess("$name (public Yak, tag $tag, GitHub release)", 'Publish publicly; this cannot be undone')) { return }

# Tag the reviewed source first. An existing tag must already point at the candidate's commit.
$existing = & git -C $repo rev-parse -q --verify "refs/tags/$tag^{commit}"
if ($LASTEXITCODE -eq 0) {
    if ($existing -ne $record.sourceCommit) { throw "Tag $tag exists on $existing, not on the candidate's commit." }
} else {
    Invoke-Checked 'Creating the release tag' { & git -C $repo tag -a $tag $record.sourceCommit -m "Conduit+ $Version" }
}

# Push the source first, so a published package always has its commit and tag on GitHub.
# Yak and GitHub are separate steps; a failure in one does not undo the other.
Invoke-Checked 'Pushing main' { & git -C $repo push origin HEAD:main }
Invoke-Checked 'Pushing the tag' { & git -C $repo push origin $tag }
Invoke-Checked 'Public Yak push' { & $YakPath push $package }
Write-Output "Published $name to the public Yak server."
Invoke-Checked 'Yak search' { & $YakPath search --all --prerelease conduit-plus }
$releaseArgs = @('release', 'create', $tag, $package, "$package.sha256", $recordPath,
    '--repo', 'tsoumdoa/conduit-plus', '--title', "Conduit+ $Version", '--generate-notes', '--verify-tag')
if ($prerelease) { $releaseArgs += '--prerelease' }
Invoke-Checked 'Creating the GitHub release' { & gh @releaseArgs }

# Download the attached archive again and confirm it is byte-identical.
$check = Join-Path ([IO.Path]::GetTempPath()) ("conduit-plus-release-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $check | Out-Null
try {
    Invoke-Checked 'Downloading the release asset' { & gh release download $tag --repo tsoumdoa/conduit-plus --pattern $name --dir $check }
    $downloaded = (Get-FileHash -LiteralPath (Join-Path $check $name) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($downloaded -cne $hash) { throw 'GitHub release asset differs from the published candidate.' }
} finally { Remove-Item -LiteralPath $check -Recurse -Force }
Write-Output "Released ${Version}: public Yak package, tag $tag and GitHub release (SHA-256 $hash)."
