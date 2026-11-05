param([string]$YakPath = 'C:\Program Files\Rhino 8\System\Yak.exe')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not (Test-Path -LiteralPath $YakPath -PathType Leaf)) { throw "Yak executable not found: $YakPath" }
& dotnet build (Join-Path $repo 'ConduitPlus.sln') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
$stage = [IO.Path]::GetFullPath((Join-Path $repo 'artifacts/yak'))
# Check the exact absolute workspace path before replacing the staging directory.
$expected = [IO.Path]::Combine([IO.Path]::GetFullPath($repo), 'artifacts', 'yak')
if ($stage -ne $expected -or -not $stage.StartsWith([IO.Path]::GetFullPath($repo) + [IO.Path]::DirectorySeparatorChar)) {
    throw "Unsafe staging path: $stage"
}
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null
$rhinoOutput = Join-Path $repo 'src/ConduitPlus.Rhino/bin/Release/net8.0-windows'
$ghOutput = Join-Path $repo 'src/ConduitPlus.Grasshopper/bin/Release/net8.0-windows'
foreach ($name in @('ConduitPlus.Rhino.rhp', 'ConduitPlus.Core.dll')) {
    Copy-Item -LiteralPath (Join-Path $rhinoOutput $name) -Destination $stage
}
Copy-Item -LiteralPath (Join-Path $ghOutput 'ConduitPlus.Grasshopper.gha') -Destination $stage
Copy-Item -LiteralPath (Join-Path $repo 'packaging/manifest.yml') -Destination $stage
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $stage
$icon = Join-Path $repo 'assets/icon.png'
if (Test-Path -LiteralPath $icon) {
    Copy-Item -LiteralPath $icon -Destination $stage
    Add-Content -LiteralPath (Join-Path $stage 'manifest.yml') -Value 'icon: icon.png'
}
Push-Location -LiteralPath $stage
try {
    & $YakPath build --platform win
    if ($LASTEXITCODE -ne 0) { throw 'Yak build failed.' }
    $packages = @(Get-ChildItem -LiteralPath $stage -Filter '*.yak')
    if ($packages.Count -ne 1) { throw "Expected one Yak package, found $($packages.Count)." }
    Write-Output "Built $($packages[0].FullName)"
} finally { Pop-Location }
