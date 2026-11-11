# Regenerates assets/icon.png (Yak package icon) from the same drawing code the plug-in uses at runtime.
# assets/icon.svg is the hand-written vector of the same mark; T3 Code and the README use it.
# -Sheet <path> also writes a contact sheet of every component icon at 1x and 4x for review.
param([string]$Sheet)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath "$PSScriptRoot/..").Path
Add-Type -AssemblyName System.Drawing
if (-not ('ConduitPlus.Grasshopper.ConduitIcons' -as [type])) {
    Add-Type -Path "$repo/src/ConduitPlus.Grasshopper/ConduitIcons.cs" -ReferencedAssemblies System.Drawing, System.Drawing.Primitives, System.Drawing.Common, System.Private.Windows.GdiPlus, System.Private.Windows.Core
}
$icons = [ConduitPlus.Grasshopper.ConduitIcons]

$logo = $icons::Logo(256)
try { $logo.Save("$repo/assets/icon.png", [System.Drawing.Imaging.ImageFormat]::Png) } finally { $logo.Dispose() }
Write-Host "Wrote assets/icon.png"

if ($Sheet) {
    $kinds = [Enum]::GetValues([ConduitPlus.Grasshopper.ComponentIcon])
    $cell = 112
    $bitmap = [System.Drawing.Bitmap]::new($cell * $kinds.Count, 2 * $cell)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $g.Clear([System.Drawing.Color]::FromArgb(212, 212, 212))
        $g.InterpolationMode = 'NearestNeighbor'
        $g.PixelOffsetMode = 'Half'
        for ($i = 0; $i -lt $kinds.Count; $i++) {
            $small = $icons::Create($kinds[$i], 24)
            try {
                $g.DrawImage($small, $i * $cell + 44, 20, 24, 24)            # actual size
                $g.DrawImage($small, $i * $cell + 8, $cell + 8, 96, 96)      # 4x nearest neighbour: real pixels
            } finally { $small.Dispose() }
        }
        $bitmap.Save($Sheet, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Host "Wrote $Sheet"
    } finally { $g.Dispose(); $bitmap.Dispose() }
}
