using System.Windows.Forms;
using ConduitPlus.Core;
using Grasshopper.GUI.Canvas;

namespace ConduitPlus.Grasshopper;

internal static class CardHostModifiers
{
    internal static CardModifiers Read(Keys keys) =>
        ((keys & Keys.Shift) != 0 ? CardModifiers.Shift : 0) |
        ((keys & Keys.Control) != 0 ? CardModifiers.Control : 0) |
        ((keys & Keys.Alt) != 0 ? CardModifiers.Alt : 0) |
        ((keys & ~(Keys.Shift | Keys.Control | Keys.Alt)) != 0 ? CardModifiers.Other : 0);
    internal static GH_ObjectResponse Response(CardResponse response) => response switch
    {
        CardResponse.Capture => GH_ObjectResponse.Capture,
        CardResponse.Handled => GH_ObjectResponse.Handled,
        CardResponse.Release => GH_ObjectResponse.Release,
        _ => GH_ObjectResponse.Ignore
    };
}
