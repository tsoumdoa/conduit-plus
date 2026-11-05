# Run inside Rhino 8: _-RunPythonScript "C:\path\to\conduit-plus\tools\run-spike.py"
# Rhino's bundled IronPython supports this file (no external Python packages).
import os
import traceback
import Rhino
import System.Drawing

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
output = os.environ.get("CONDUITPLUS_RESULTS", os.path.join(root, "docs", "spike"))
if not os.path.isdir(output):
    os.makedirs(output)
def progress(message):
    with open(os.path.join(output, "rhino-startup.log"), "a") as f:
        f.write(message + "\n")
progress("Python startup script entered")
try:
    plugin = os.path.join(root, "src", "ConduitPlus.Rhino", "bin", "Debug", "net8.0-windows", "ConduitPlus.Rhino.rhp")
    loaded = Rhino.PlugIns.PlugIn.LoadPlugIn(plugin)
    if loaded[1] == System.Guid.Empty:
        raise Exception("Rhino could not load " + plugin)
    progress("Conduit+ plug-in loaded")
    if os.environ.get("CONDUITPLUS_LARGE_VIEW") == "1":
        view = Rhino.RhinoDoc.ActiveDoc.Views.Add("Conduit+ spike", Rhino.Display.DefinedViewportProjection.Perspective, System.Drawing.Rectangle(0, 0, 1900, 1800), True)
        Rhino.RhinoDoc.ActiveDoc.Views.ActiveView = view
        progress("Large floating viewport created")
    progress("Starting ConduitPlusBench")
    ok = Rhino.RhinoApp.RunScript("_ConduitPlusBench", False)
    with open(os.path.join(output, "rhino-run-complete.txt"), "w") as f:
        f.write("RunScript returned {0}\n".format(ok))
except:
    with open(os.path.join(output, "rhino-launch-error.txt"), "w") as f:
        f.write(traceback.format_exc())
    raise
finally:
    if os.environ.get("CONDUITPLUS_EXIT") == "1":
        Rhino.RhinoApp.RunScript("_-Exit _No", False)
