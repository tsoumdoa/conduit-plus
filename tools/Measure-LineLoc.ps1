# Physical source lines, including blanks/braces/comments. This is intentionally not a statement count.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
function MethodLines([string]$file, [string]$start) {
    $lines = Get-Content -LiteralPath (Join-Path $repo $file)
    $first = 0
    while ($first -lt $lines.Count -and $lines[$first] -notmatch $start) { $first++ }
    if ($first -eq $lines.Count) { throw "Missing method in $file" }
    $last = $first + 1
    while ($last -lt $lines.Count -and $lines[$last] -ne '    }') { $last++ }
    if ($last -eq $lines.Count) { throw "Missing method end in $file" }
    return $last - $first + 1
}
$sharedFile = (Get-Content -LiteralPath (Join-Path $repo 'src/ConduitPlus.Core/LineCardPainter.cs')).Count
$dispatch = @(Get-Content -LiteralPath (Join-Path $repo 'src/ConduitPlus.Core/Drawing.cs') | Where-Object { $_ -match 'CardKind.Line' }).Count
$interface = 1 # ICanvas2D.FillPolygon signature (shared by the C# adapters).
$shared = $sharedFile + $dispatch + $interface
$gdi = MethodLines 'src/ConduitPlus.Rhino/Rendering/GdiCanvas.cs' '^    public void FillPolygon'
$native = MethodLines 'src/ConduitPlus.Rhino/Rendering.cs' '^    public void FillPolygon'
$webFile = (Get-Content -LiteralPath (Join-Path $repo 'src/ConduitPlus.Web/line.ts')).Count
$webDispatch = @(Get-Content -LiteralPath (Join-Path $repo 'src/ConduitPlus.Web/runtime.ts') | Where-Object { $_ -match "renderLine|export type Card|export type Theme|const h=c.kind|else if\(c.kind==='line'" }).Count
[pscustomobject]@{ Renderer='Raster'; Shared=$shared; RendererSpecific=$gdi; Total=$shared+$gdi }
[pscustomobject]@{ Renderer='Native (outline fallback)'; Shared=$shared; RendererSpecific=$native; Total=$shared+$native }
[pscustomobject]@{ Renderer='Web'; Shared=0; RendererSpecific=$webFile+$webDispatch; Total=$webFile+$webDispatch }
