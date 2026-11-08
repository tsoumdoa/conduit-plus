# Conduit+

Viewport result cards for Rhino 8.20+ on Windows, running .NET 8.

Drop the `.yak` archive onto Rhino, accept installation, then restart Rhino.
Open Grasshopper and find **Conduit+** → **Cards** and **Display**. Connect a
Metric, Bar, Table, Line, Text or Legend to Overlay. Run `ConduitPlusDemo` for a
dashboard with linked geometry; `ConduitPlusCapture` saves PNGs.

Use Rhino's `SetDotNetRuntime` command to select .NET 8 if necessary. This
package does not support macOS, Rhino 7 or .NET Framework. PDF export is
experimental; high-resolution captures and mixed-monitor DPI need interactive
validation. Demo Truss produces illustrative data, not structural analysis.

Source, full documentation, examples and issues:
https://github.com/tsoumdoa/conduit-plus
