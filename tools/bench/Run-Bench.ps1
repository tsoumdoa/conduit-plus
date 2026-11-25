param(
    [ValidateSet('Bench', 'Capture', 'Pdf', 'PlatformCheck', 'LabelOcclusion', 'Labels', 'LabelInput')][string]$Mode = 'Bench',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [ValidateSet(1, 2, 4)][int]$Scale = 1,
    [string]$Output,
    [switch]$Compact,
    [switch]$Thresholds,
    [switch]$Distribution,
    [ValidateSet('Paper', 'Night')][string]$DistributionTheme,
    [ValidateRange(10, 600)][int]$TimeoutSeconds = 90
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (-not $Output) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $Output = if ($Mode -eq 'LabelOcclusion') { Join-Path $repo "artifacts/labels-occlusion-$stamp" }
        else { Join-Path ([IO.Path]::GetTempPath()) "ConduitPlus/bench-$stamp" }
}
$destination = [IO.Path]::GetFullPath($Output)
if ($Distribution -and ($Mode -eq 'Capture' -or $Mode -eq 'Pdf') -and !$DistributionTheme) {
    # A second viewport PNG capture in the same headless RhinoCore can fail natively.
    foreach ($theme in @('Paper', 'Night')) {
        & $PSCommandPath -Mode $Mode -Distribution -DistributionTheme $theme -Configuration $Configuration -Scale $Scale -TimeoutSeconds $TimeoutSeconds -Output (Join-Path $destination $theme.ToLowerInvariant())
    }
    return
}
$project = Join-Path $PSScriptRoot 'ConduitPlus.RhinoBench/ConduitPlus.RhinoBench.csproj'
& dotnet build $project -c $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Benchmark build failed.' }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$stdout = Join-Path $destination 'bench.log'
$stderr = Join-Path $destination 'bench-error.log'
$assembly = Join-Path $PSScriptRoot "ConduitPlus.RhinoBench/bin/$Configuration/net8.0-windows/ConduitPlus.RhinoBench.dll"
$arguments = @('"' + $assembly + '"')
if ($Mode -eq 'Capture') { $arguments += @('--capture', $Scale) }
elseif ($Mode -eq 'Pdf') { $arguments += '--pdf' }
elseif ($Mode -eq 'PlatformCheck') { $arguments += '--platform-check' }
if ($Mode -eq 'LabelOcclusion') { $arguments += '--label-occlusion' }
if ($Mode -eq 'Labels') { $arguments += '--labels' }
if ($Mode -eq 'LabelInput') { $arguments += '--label-input' }
if ($Compact) { $arguments += '--compact' }
if ($Distribution) { $arguments += '--distribution' }
if ($DistributionTheme -eq 'Night') { $arguments += '--distribution-night' }
if ($Thresholds) { $arguments += '--thresholds' }
$previousOutput = $env:CONDUITPLUS_RESULTS
try {
    $env:CONDUITPLUS_RESULTS = $destination
    $process = Start-Process dotnet -ArgumentList $arguments -WorkingDirectory $repo -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
} finally { $env:CONDUITPLUS_RESULTS = $previousOutput }
$timer = [Diagnostics.Stopwatch]::StartNew()
while (-not $process.WaitForExit(500)) {
    # Safe-mode RhinoCore.Dispose can stall after successful workload and cleanup.
    $log = Get-Content -LiteralPath $stdout -Raw -ErrorAction SilentlyContinue
    if ($log -match 'Overlay and floating viewport disposed; shutting down RhinoCore\.') {
        if (-not $process.WaitForExit(3000)) {
            Stop-Process -Id $process.Id -Force
            Write-Output 'Stopped only this benchmark process after overlay/view cleanup; RhinoCore shutdown stalled.'
        }
        break
    }
    if ($timer.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
        Stop-Process -Id $process.Id -Force
        throw "Benchmark workload timed out (only process $($process.Id) stopped). See $destination"
    }
}
Get-Content -LiteralPath $stdout
$errors = Get-Content -LiteralPath $stderr -Raw -ErrorAction SilentlyContinue
if ($errors) { Write-Output $errors }
if ((Get-Content -LiteralPath $stdout -Raw) -notmatch 'Benchmark workload completed successfully\.') {
    throw "Benchmark workload failed. See $stderr"
}
Write-Output "Results: $destination"
