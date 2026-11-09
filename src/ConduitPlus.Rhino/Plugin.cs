using System.Runtime.InteropServices;
using Rhino.PlugIns;

[assembly: Guid("3EDFA2C9-7579-4CA6-BFAD-54F18B48F701")]

namespace ConduitPlus.Rhino;

public sealed class ConduitPlusPlugin : PlugIn
{
    // Display owns first-use initialization and RhinoApp.Closing cleanup, even without this RHP.
}
