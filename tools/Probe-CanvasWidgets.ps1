#Requires -Version 7
<# .SYNOPSIS Read pinned Grasshopper widget metadata without starting Rhino. #>
param([string]$PackageRoot = (Join-Path $env:USERPROFILE '.nuget/packages/grasshopper/8.20.25147.11001-rc/lib/net7.0'))
$ErrorActionPreference = 'Stop'
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $PackageRoot 'Grasshopper.dll'))
"Assembly: $($assembly.FullName)"
$flags = [Reflection.BindingFlags]'Public,NonPublic,Instance,Static,DeclaredOnly'
foreach ($name in @('Grasshopper.GUI.Widgets.GH_Widget', 'Grasshopper.GUI.Widgets.GH_CanvasWidget_FixedObject', 'Grasshopper.GUI.Canvas.GH_CanvasWidgetListEventArgs', 'Grasshopper.GUI.Canvas.GH_Canvas+WidgetListCreatedEventHandler', 'Grasshopper.Instances+CanvasCreatedEventHandler')) {
    $type = $assembly.GetType($name, $true)
    "`n$name"
    foreach ($method in $type.GetMethods($flags)) {
        "  $method [public=$($method.IsPublic), static=$($method.IsStatic), virtual=$($method.IsVirtual), final=$($method.IsFinal), abstract=$($method.IsAbstract)]"
    }
}
$canvas = $assembly.GetType('Grasshopper.GUI.Canvas.GH_Canvas', $true)
"`nCanvas discovery/dispatch"
foreach ($name in @('Canvases', 'Widgets', 'ActiveWidget', 'DrawingMode', 'Graphics')) {
    $property = $canvas.GetProperty($name, $flags)
    "  $property [public=$($property.GetMethod.IsPublic), static=$($property.GetMethod.IsStatic)]"
}
$event = $canvas.GetEvent('WidgetListCreated', $flags)
"  $event [public=$($event.AddMethod.IsPublic), static=$($event.AddMethod.IsStatic)]"
$widget = $assembly.GetType('Grasshopper.GUI.Widgets.GH_Widget', $true)
if ($widget.GetMethods($flags).Name -match 'Wheel') { throw 'Unexpected widget wheel API; re-evaluate the adapter.' }
