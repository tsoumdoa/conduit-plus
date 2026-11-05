param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$destination = Join-Path $env:APPDATA 'Grasshopper/Libraries/ConduitPlusSpike'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$rhinoOutput = Join-Path $repo "src/ConduitPlus.Rhino/bin/$Configuration/net8.0-windows"
$ghOutput = Join-Path $repo "src/ConduitPlus.Grasshopper/bin/$Configuration/net8.0-windows"
foreach ($name in @('ConduitPlus.Rhino.rhp', 'ConduitPlus.Core.dll')) {
    Copy-Item -LiteralPath (Join-Path $rhinoOutput $name) -Destination $destination -Force
}
Copy-Item -LiteralPath (Join-Path $ghOutput 'ConduitPlus.Grasshopper.gha') -Destination $destination -Force
Write-Output "Installed developer files into $destination"
Write-Output "In Rhino 8, load $destination/ConduitPlus.Rhino.rhp using PlugInManager > Install, then start Grasshopper."
Write-Output 'Close Rhino before rebuilding/reinstalling; no host or WebView2 assemblies are installed.'
