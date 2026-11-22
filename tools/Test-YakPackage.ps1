#Requires -Version 7
<# .SYNOPSIS
Validate a final Yak ZIP archive without loading RhinoCommon or installing it.
#>
param(
    [Parameter(Mandatory)][string]$Package,
    [Parameter(Mandatory)][string]$Version,
    [string]$SourceDirectory,
    [ValidateSet('win', 'mac')][string]$Platform = 'win'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem
$Package = (Resolve-Path -LiteralPath $Package).Path
$expectedName = "conduit-plus-$Version-rh8_20-$Platform.yak"
if ([IO.Path]::GetFileName($Package) -cne $expectedName) { throw "Expected $Platform/Rhino 8.20 archive name $expectedName" }
$required = @('ConduitPlus.CanvasWidgets.dll', 'ConduitPlus.Core.dll', 'ConduitPlus.Display.dll', 'ConduitPlus.Grasshopper.gha', 'ConduitPlus.Rhino.rhp', 'LICENSE', 'README.md', 'icon.png', 'manifest.yml')
$zip = [IO.Compression.ZipFile]::OpenRead($Package)
try {
    $names = @($zip.Entries | ForEach-Object FullName)
    if ($names.Count -ne $required.Count -or @(Compare-Object ($required | Sort-Object) ($names | Sort-Object) -CaseSensitive).Count -gt 0) {
        throw "Unexpected package inventory: $($names -join ', '). Expected only: $($required -join ', ')"
    }
    foreach ($entry in $zip.Entries) {
        if ($entry.Length -eq 0) { throw "Empty package entry: $($entry.FullName)" }
        if ($entry.FullName -eq 'manifest.yml') {
            $reader = [IO.StreamReader]::new($entry.Open())
            try { $manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
            if ($manifest -cnotmatch '(?m)^name: conduit-plus\r?$' -or
                $manifest -cnotmatch ('(?m)^version: ' + [regex]::Escape($Version) + '\r?$') -or
                $manifest -cnotmatch '(?m)^icon: icon\.png\r?$' -or
                $manifest -cnotmatch ('(?m)^platform: ' + $Platform + '\r?$') -or
                $manifest -notmatch 'guid:3edfa2c9-7579-4ca6-bfad-54f18b48f701' -or
                $manifest -notmatch 'guid:b02874c1-57b6-43d2-871a-3cb89603f101') {
                throw 'Packaged manifest name, version, platform, icon or restore GUID is incorrect.'
            }
        }
        # Yak normalizes YAML and adds platform metadata when building. Validate that
        # manifest above; byte-compare the payload files rather than its template.
        if ($SourceDirectory -and $entry.FullName -ne 'manifest.yml') {
            $source = Join-Path $SourceDirectory $entry.FullName
            $stream = $entry.Open()
            $algorithm = [Security.Cryptography.SHA256]::Create()
            try { $actual = [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '') }
            finally { $stream.Dispose(); $algorithm.Dispose() }
            $expected = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
            if ($actual -cne $expected) { throw "Archive differs from staging: $($entry.FullName)" }
        }
        # Flavour check: Windows assemblies carry TargetPlatform("Windows7.0"); Mac (plain net8.0) ones must not.
        if ([IO.Path]::GetExtension($entry.FullName) -in @('.rhp', '.gha', '.dll') -and $entry.FullName -ne 'ConduitPlus.Core.dll') {
            $memory = [IO.MemoryStream]::new(); $stream = $entry.Open()
            try { $stream.CopyTo($memory) } finally { $stream.Dispose() }
            $windows = [Text.Encoding]::ASCII.GetString($memory.ToArray()).Contains('Windows7.0')
            if ($Platform -eq 'mac' -and $windows) { throw "$($entry.FullName) is a net8.0-windows build; the mac package needs the ConduitPlusPlatform=mac build." }
            if ($Platform -eq 'win' -and -not $windows) { throw "$($entry.FullName) is not a net8.0-windows build." }
        }
    }
} finally { $zip.Dispose() }
Write-Output "Verified $expectedName ($($required.Count) files; no host DLLs, debug files or extra payload)."
