#Requires -Version 7
<# .SYNOPSIS
Exercise the archive gate against a real candidate and deliberately damaged copies.
#>
param([Parameter(Mandatory)][string]$Package)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$validator = Join-Path $repo 'tools/Test-YakPackage.ps1'
. (Join-Path $repo 'tools/ReleaseVersion.ps1')
$version = Get-ReleaseVersion $repo
$stage = Join-Path $repo 'artifacts/yak/stage'
$Package = (Resolve-Path -LiteralPath $Package).Path
& $validator -Package $Package -Version $version -SourceDirectory $stage
$root = Join-Path $repo ('artifacts/yak/validation-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$cases = @(
    @{ Name = 'missing assembly'; Entry = 'ConduitPlus.Core.dll'; Action = 'delete'; Error = 'Unexpected package inventory' },
    @{ Name = 'extra host DLL'; Entry = 'RhinoCommon.dll'; Action = 'add'; Error = 'Unexpected package inventory' },
    @{ Name = 'changed payload'; Entry = 'ConduitPlus.Core.dll'; Action = 'replace'; Error = 'Archive differs from staging' },
    @{ Name = 'wrong manifest version'; Entry = 'manifest.yml'; Action = 'manifest'; Error = 'Packaged manifest' },
    @{ Name = 'wrong manifest name'; Entry = 'manifest.yml'; Action = 'manifest-name'; Error = 'Packaged manifest' },
    @{ Name = 'wrong manifest platform'; Entry = 'manifest.yml'; Action = 'manifest-platform'; Error = 'Packaged manifest' },
    @{ Name = 'missing restore GUID'; Entry = 'manifest.yml'; Action = 'manifest-guid'; Error = 'Packaged manifest' },
    @{ Name = 'empty icon'; Entry = 'icon.png'; Action = 'empty'; Error = 'Empty package entry' }
)
try {
    foreach ($case in $cases) {
        $folder = Join-Path $root $case.Action
        New-Item -ItemType Directory -Path $folder | Out-Null
        $copy = Join-Path $folder ([IO.Path]::GetFileName($Package))
        Copy-Item -LiteralPath $Package -Destination $copy
        $zip = [IO.Compression.ZipFile]::Open($copy, [IO.Compression.ZipArchiveMode]::Update)
        try {
            $entry = $zip.GetEntry($case.Entry)
            if ($case.Action -like 'manifest*') {
                $reader = [IO.StreamReader]::new($entry.Open())
                try { $text = $reader.ReadToEnd() }
                finally { $reader.Dispose() }
                $text = switch ($case.Action) {
                    'manifest' { $text.Replace("version: $version", 'version: 999.0.0') }
                    'manifest-name' { $text.Replace('name: conduit-plus', 'name: wrong-package') }
                    'manifest-platform' { $text.Replace('platform: win', 'platform: any') }
                    'manifest-guid' { $text.Replace('guid:b02874c1-57b6-43d2-871a-3cb89603f101', 'removed-guid') }
                }
            } else { $text = 'intentionally invalid payload' }
            if ($entry) { $entry.Delete() }
            if ($case.Action -ne 'delete') {
                $entry = $zip.CreateEntry($case.Entry)
                $writer = [IO.StreamWriter]::new($entry.Open())
                try { if ($case.Action -ne 'empty') { $writer.Write($text) } }
                finally { $writer.Dispose() }
            }
        } finally { $zip.Dispose() }
        $rejected = $false
        try { & $validator -Package $copy -Version $version -SourceDirectory $stage }
        catch {
            if ($_.Exception.Message -notlike "*$($case.Error)*") { throw }
            $rejected = $true
        }
        if (-not $rejected) { throw "Validator accepted $($case.Name)." }
        Write-Output "PASS: rejects $($case.Name)"
    }
    $wrongTag = Join-Path $root ([IO.Path]::GetFileName($Package).Replace('-win.yak', '-any.yak'))
    Copy-Item -LiteralPath $Package -Destination $wrongTag
    $rejected = $false
    try { & $validator -Package $wrongTag -Version $version }
    catch {
        if ($_.Exception.Message -notlike '*Expected Windows/Rhino 8.20 archive name*') { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw 'Validator accepted an incorrect distribution tag.' }
    Write-Output 'PASS: rejects incorrect distribution tag'
    Write-Output 'Packaging validation: one real archive accepted; nine damaged candidates rejected.'
} finally {
    $resolved = [IO.Path]::GetFullPath($root)
    $parent = [IO.Path]::GetFullPath((Join-Path $repo 'artifacts/yak'))
    if ((Split-Path $resolved -Parent) -ne $parent -or (Split-Path $resolved -Leaf) -notmatch '^validation-[a-f0-9]{32}$') {
        throw "Unsafe test cleanup path: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
