param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [string]$Output = (Join-Path ([IO.Path]::GetTempPath()) ('ConduitPlus/examples-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))),
    [switch]$VerifyOnly,
    [switch]$Exports,
    [switch]$Safe,
    [switch]$Compact,
    [ValidateSet(1, 2, 4)][int]$Scale = 1,
    [ValidateSet('Baseline', 'Drag', 'Tooltip', 'Selected')][string]$State = 'Baseline',
    [ValidateRange(10, 600)][int]$TimeoutSeconds = 180
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$destination = [IO.Path]::GetFullPath($Output)
$project = Join-Path $PSScriptRoot 'ConduitPlus.Examples/ConduitPlus.Examples.csproj'
& dotnet build $project -c $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Example harness build failed.' }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$stdout = Join-Path $destination 'example.log'
$stderr = Join-Path $destination 'example-error.log'
$assembly = Join-Path $PSScriptRoot "ConduitPlus.Examples/bin/$Configuration/net8.0-windows/ConduitPlus.Examples.dll"
$arguments = @('"' + $assembly + '"')
if ($VerifyOnly) { $arguments += '--verify-only' }
if ($Exports) { $arguments += '--exports' }
if ($Safe) { $arguments += '--safe' }
if ($Compact) { $arguments += '--compact' }
$arguments += @('--scale', $Scale)
$arguments += @('--state', $State.ToLowerInvariant())
$previousOutput = $env:CONDUITPLUS_RESULTS
try {
    $env:CONDUITPLUS_RESULTS = $destination
    $process = Start-Process dotnet -ArgumentList $arguments -WorkingDirectory $repo -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
} finally { $env:CONDUITPLUS_RESULTS = $previousOutput }
$timer = [Diagnostics.Stopwatch]::StartNew()
while (-not $process.WaitForExit(500)) {
    $log = Get-Content -LiteralPath $stdout -Raw -ErrorAction SilentlyContinue
    if ($log -match 'Overlay and floating viewport disposed; shutting down RhinoCore\.') {
        if (-not $process.WaitForExit(3000)) {
            Stop-Process -Id $process.Id -Force
            Write-Output 'Stopped only this example process after overlay/view cleanup; RhinoCore shutdown stalled.'
        }
        break
    }
    if ($timer.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
        Stop-Process -Id $process.Id -Force
        throw "Example workload timed out (only process $($process.Id) stopped). See $destination"
    }
}
Get-Content -LiteralPath $stdout
$errors = Get-Content -LiteralPath $stderr -Raw -ErrorAction SilentlyContinue
if ($errors) { Write-Output $errors }
if (-not ((Get-Content -LiteralPath $stdout -Raw) -match 'Example workload completed successfully\.')) { throw "Example workload failed. See $stderr" }
Write-Output "Results: $destination"
