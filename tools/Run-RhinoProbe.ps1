param(
    [ValidateSet('Bench', 'Capture', 'Line', 'Pdf')][string]$Mode = 'Bench',
    [ValidateSet('Native', 'Raster', 'Web')][string]$Renderer = 'Raster',
    [ValidateSet(1, 2, 4)][int]$Scale = 1,
    [ValidateSet('direct', 'prepared')][string]$Preparation = 'direct',
    [string]$Output = 'docs/spike/raster-pass',
    [switch]$Small,
    [switch]$Compact,
    [int]$TimeoutSeconds = 60
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$destination = if ([IO.Path]::IsPathRooted($Output)) { [IO.Path]::GetFullPath($Output) } else { [IO.Path]::GetFullPath((Join-Path $repo $Output)) }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$env:CONDUITPLUS_RESULTS = $destination
$tag = if ($Mode -eq 'Capture') { "$Renderer-$Scale-$Preparation".ToLowerInvariant() } else { $Mode.ToLowerInvariant() }
$stdout = Join-Path $destination "$tag.log"
$stderr = Join-Path $destination "$tag-error.log"
$arguments = @('tools/ConduitPlus.RhinoProbe/bin/Debug/net8.0-windows/ConduitPlus.RhinoProbe.dll')
if ($Mode -eq 'Capture') { $arguments += @('--capture', $Renderer, $Scale, $Preparation) }
elseif ($Mode -ne 'Bench') { $arguments += "--$($Mode.ToLowerInvariant())" }
if ($Small) { $arguments += '--small' }
if ($Compact) { $arguments += '--compact' }
$process = Start-Process dotnet -ArgumentList $arguments -WorkingDirectory $repo -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
$timer = [Diagnostics.Stopwatch]::StartNew()
while (-not $process.WaitForExit(500)) {
    # RhinoCore.Dispose can stall in safe mode even after our workload and cleanup completed.
    $log = Get-Content -LiteralPath $stdout -Raw -ErrorAction SilentlyContinue
    if ($log -match 'Overlay and floating viewport disposed; shutting down RhinoCore\.') {
        if (-not $process.WaitForExit(3000)) {
            Stop-Process -Id $process.Id -Force
            Write-Output 'Stopped only this probe process after completed overlay/view cleanup; RhinoCore shutdown stalled.'
        }
        break
    }
    if ($timer.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
        Stop-Process -Id $process.Id -Force
        throw "Probe workload timed out: $tag (only process $($process.Id) stopped)."
    }
}
Get-Content -LiteralPath $stdout
$errors = Get-Content -LiteralPath $stderr -Raw -ErrorAction SilentlyContinue
if ($errors) { Write-Output $errors }
if ((Get-Content -LiteralPath $stdout -Raw) -notmatch 'Probe workload completed successfully\.') { throw "Probe workload failed: $tag. See $stderr" }
