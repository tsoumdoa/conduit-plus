using System.Drawing;
using ConduitPlus.Core;
using Rhino.Display;

namespace ConduitPlus.Rhino;

internal sealed record CapturedCard(CardSpec Spec, CardLayout Layout, Bitmap Bitmap, double Scale) : IDisposable
{
    public void Dispose() => Bitmap.Dispose();
}

internal sealed class BitmapCard(CapturedCard capture) : IDisposable
{
    public CapturedCard Capture { get; } = capture;
    public DisplayBitmap Display { get; } = new(capture.Bitmap);
    public void Dispose() { Display.Dispose(); Capture.Dispose(); }
}
