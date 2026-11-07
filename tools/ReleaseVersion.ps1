#Requires -Version 7
# Shared by the release scripts (dot-source it). Directory.Build.props holds the only version.

# Yak release versions use SemVer without build metadata. Numeric identifiers cannot have leading zeroes.
$script:SemVerPattern = '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*)?$'
$script:VersionElementPattern = '<Version>([^<]*)</Version>'

function Get-PropsPath([string]$Repo) { Join-Path $Repo 'Directory.Build.props' }

function Get-ReleaseVersion([string]$Repo) {
    $text = Get-Content -LiteralPath (Get-PropsPath $Repo) -Raw
    $found = [regex]::Matches($text, $script:VersionElementPattern)
    if ($found.Count -ne 1) { throw "Expected exactly one <Version> in Directory.Build.props, found $($found.Count)." }
    $version = $found[0].Groups[1].Value
    Assert-ReleaseVersion $version
    $version
}

function Assert-ReleaseVersion([string]$Version) {
    if ($Version -cnotmatch $script:SemVerPattern) {
        throw "Invalid release version '$Version'. Use SemVer without build metadata, e.g. 0.2.0 or 0.2.0-beta.1."
    }
}

function Set-ReleaseVersion([string]$Repo, [string]$Version) {
    Assert-ReleaseVersion $Version
    $path = Get-PropsPath $Repo
    $text = Get-Content -LiteralPath $path -Raw
    if ([regex]::Matches($text, $script:VersionElementPattern).Count -ne 1) { throw 'Expected exactly one <Version> in Directory.Build.props.' }
    $updated = [regex]::Replace($text, $script:VersionElementPattern, "<Version>$Version</Version>")
    [IO.File]::WriteAllText($path, $updated, [Text.UTF8Encoding]::new($false))
}

# Precedence of SemVer 2.0 versions without build metadata: -1, 0 or 1.
function Compare-ReleaseVersion([string]$A, [string]$B) {
    $split = { param($v) $core, $pre = $v.Split('-', 2); [pscustomobject]@{ Core = [int[]]$core.Split('.'); Pre = $pre } }
    $x = & $split $A; $y = & $split $B
    for ($i = 0; $i -lt 3; $i++) { if ($x.Core[$i] -ne $y.Core[$i]) { return [Math]::Sign($x.Core[$i] - $y.Core[$i]) } }
    if (-not $x.Pre -and -not $y.Pre) { return 0 }
    if (-not $x.Pre) { return 1 }
    if (-not $y.Pre) { return -1 }
    $p = $x.Pre.Split('.'); $q = $y.Pre.Split('.')
    for ($i = 0; $i -lt [Math]::Min($p.Count, $q.Count); $i++) {
        $pn = $p[$i] -match '^[0-9]+$'; $qn = $q[$i] -match '^[0-9]+$'
        if ($pn -and $qn) { $c = ([bigint]$p[$i]).CompareTo([bigint]$q[$i]) }
        elseif ($pn) { $c = -1 } elseif ($qn) { $c = 1 }
        else { $c = [string]::CompareOrdinal($p[$i], $q[$i]) }
        if ($c -ne 0) { return [Math]::Sign($c) }
    }
    [Math]::Sign($p.Count - $q.Count)
}
