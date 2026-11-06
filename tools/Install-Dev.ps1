param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
if (Get-Process -Name Rhino -ErrorAction SilentlyContinue) {
    Write-Output 'Rhino is running; developer install skipped. Close Rhino and run this script again.'
    return
}
$repo = Split-Path $PSScriptRoot -Parent
$libraries = [IO.Path]::GetFullPath((Join-Path $env:APPDATA 'Grasshopper/Libraries'))
$destination = Join-Path $libraries 'ConduitPlus'
$legacy = [IO.Path]::GetFullPath((Join-Path $libraries 'ConduitPlusSpike'))
$rhinoOutput = Join-Path $repo "src/ConduitPlus.Rhino/bin/$Configuration/net8.0-windows"
$ghOutput = Join-Path $repo "src/ConduitPlus.Grasshopper/bin/$Configuration/net8.0-windows"
$files = @((Join-Path $rhinoOutput 'ConduitPlus.Rhino.rhp'), (Join-Path $rhinoOutput 'ConduitPlus.Core.dll'), (Join-Path $ghOutput 'ConduitPlus.Grasshopper.gha'))
foreach ($file in $files) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Build $Configuration first; missing: $file" }
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
foreach ($file in $files) { Copy-Item -LiteralPath $file -Destination $destination -Force }
if (Test-Path -LiteralPath $legacy) {
    if ((Split-Path $legacy -Parent) -ne $libraries -or (Split-Path $legacy -Leaf) -ne 'ConduitPlusSpike') {
        throw "Unsafe legacy installation path: $legacy"
    }
    Remove-Item -LiteralPath $legacy -Recurse -Force
    Write-Output "Removed legacy installation: $legacy"
}
Write-Output "Installed developer files into $destination"
Write-Output "In Rhino 8, load $destination/ConduitPlus.Rhino.rhp using PlugInManager > Install, then start Grasshopper."
Write-Output 'Close Rhino before rebuilding/reinstalling. Host assemblies are not installed.'
