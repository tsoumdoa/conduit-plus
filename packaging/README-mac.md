# Conduit+ for Mac (preview)

Viewport result cards for Rhino 8.20+ for Mac, running .NET 8.

**Beta, untested on a Mac.** This package is built and unit-tested on Windows but
has not yet been run in Rhino for Mac. Expect problems, from small display glitches
to the plug-in not loading. Please report anything that fails or differs from
Windows at https://github.com/tsoumdoa/conduit-plus/issues, with the report from
`ConduitPlusPlatformCheck` if you can.

Drop the `.yak` archive onto Rhino, accept installation, then restart Rhino.
Open Grasshopper and find **Conduit+** → **Cards** and **Display**. Connect a
Metric, Bar, Table, Line, Text or Legend to Overlay. Run `ConduitPlusDemo` for a
dashboard with linked geometry; `ConduitPlusCapture` saves PNGs and
`ConduitPlusPlatformCheck` writes a self-test report to ~/Documents/ConduitPlus.
The GHA and RHP share ConduitPlus.Display.dll and ConduitPlus.Core.dll; keep the
package files together.

On Mac, Command-click toggles a member (Ctrl-click on Windows) and Control-click
is the secondary click. Scroll tables with the mouse wheel or a two-finger
trackpad swipe over the card; elsewhere the gesture navigates the viewport as usual.
Cards use Helvetica Neue where Windows uses Segoe UI.

Use Rhino's `SetDotNetRuntime` command to select .NET 8 if necessary. Rhino 7 is
not supported. PDF export is experimental. Demo Truss produces illustrative data,
not structural analysis.

Source, full documentation, the macOS test checklist and issues:
https://github.com/tsoumdoa/conduit-plus (docs/macos.md)

Canvas Overlay also requires ConduitPlus.CanvasWidgets.dll beside the GHA. Keep the complete Yak payload together.
