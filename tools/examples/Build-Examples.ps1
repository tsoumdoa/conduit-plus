param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [string]$Output = (Join-Path ([IO.Path]::GetTempPath()) ('ConduitPlus/examples-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))),
    [ValidateSet('All', 'Structural', 'Rooms', 'Massing', 'Showcase', 'Hello')][string]$Example = 'All',
    [string]$Shot = 'paper',
    [switch]$VerifyOnly,
    [switch]$CanvasStackChecks,
    [switch]$Exports,
    [switch]$Safe,
    [switch]$Compact,
    [ValidateSet(1, 2, 4)][int]$Scale = 1,
    [ValidateSet('Paper', 'Night', 'Smoke', 'Blueprint')][string]$Theme = 'Paper',
    [ValidateSet('Baseline', 'Tooltip', 'Selected', 'GroupSelected', 'GroupPartial', 'GroupHover', 'Scope')][string]$State = 'Baseline',
    [ValidateRange(10, 600)][int]$TimeoutSeconds = 180
)
$ErrorActionPreference = 'Stop'
if ($Compact -and $Example -eq 'All') { $Example = 'Structural' }
if ($Compact -and $Example -ne 'Structural') { throw 'Compact fixtures require -Example Structural.' }
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$destination = [IO.Path]::GetFullPath($Output)
$project = Join-Path $PSScriptRoot 'ConduitPlus.Examples/ConduitPlus.Examples.csproj'
& dotnet build $project -c $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Example harness build failed.' }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
# Keep each definition in its own RhinoCore process. Switching GH definitions after a
# capture can fail natively in RhinoCore even when each fixture passes independently.
$fixtures = if ($Example -eq 'All') { @('Structural', 'Rooms', 'Massing', 'Showcase', 'Hello') } else { @($Example) }
foreach ($fixture in $fixtures) {
    $suffix = if ($Example -eq 'All') { '-' + $fixture.ToLowerInvariant() } else { '' }
    $stdout = Join-Path $destination ("example$suffix.log")
    $stderr = Join-Path $destination ("example$suffix-error.log")
    $assembly = Join-Path $PSScriptRoot "ConduitPlus.Examples/bin/$Configuration/net8.0-windows/ConduitPlus.Examples.dll"
    $arguments = @('"' + $assembly + '"')
    if ($VerifyOnly) { $arguments += '--verify-only' }
    if ($CanvasStackChecks) { $arguments += '--canvas-stack-checks' }
    if ($Exports) { $arguments += '--exports' }
    if ($Safe) { $arguments += '--safe' }
    if ($Compact) { $arguments += '--compact' }
    $arguments += @('--example', $fixture.ToLowerInvariant())
    $arguments += @('--shot', $Shot)
    $arguments += @('--scale', $Scale)
    $arguments += @('--theme', $Theme)
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
}
Write-Output "Results: $destination"
