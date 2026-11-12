# Colour helper review fixtures

Developers can regenerate the emitted legend PNG fixtures through the Rhino-free
raster path in all four themes:

```powershell
$env:CONDUITPLUS_COLOUR_RENDERS = Join-Path (Get-Location) 'artifacts/colour-helpers'
dotnet test tests/ConduitPlus.Core.Tests/ConduitPlus.Core.Tests.csproj -c Debug --filter FullyQualifiedName~ColourLegendRenderTests
```

The fixtures cover all named palettes, asymmetric diverging/Status scales, custom
4- and 32-stop palettes, subnormal and overflowing-span domains, and constant data.
Review the emitted PNGs in Paper, Night, Smoke and Blueprint.

Installed Rhino/Grasshopper host checks (a separate hidden RhinoCore instance):

```powershell
dotnet run --project tools/examples/ConduitPlus.Examples -c Debug -- --colour-checks
```

These check scalar iteration guards, multi-branch Palette/Order flattening,
grouped Bar membership, unwired warnings, the Scale palette menu and generated
key stability across archive/reopen and copy identity.
