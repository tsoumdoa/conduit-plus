using System.Runtime.InteropServices;
using Rhino.PlugIns;

[assembly: Guid("3EDFA2C9-7579-4CA6-BFAD-54F18B48F701")]

namespace ConduitPlus.Rhino;

public sealed class ConduitPlusPlugin : PlugIn
{
    protected override LoadReturnCode OnLoad(ref string errorMessage)
    { OverlayManager.Start(); return LoadReturnCode.Success; }
    protected override void OnShutdown() => OverlayManager.Stop();
}
