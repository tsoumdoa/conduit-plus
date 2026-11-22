using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ConduitPlus.Core;

namespace ConduitPlus.Rendering;

/// <summary>Copies a hover-free 2× image. Call on the host's UI (STA) thread.</summary>
public static class CardClipboard
{
    public const double Scale = 2;

    public static bool TryCopy(IReadOnlyList<CardSpec> specs, Theme theme, [NotNullWhen(false)] out string? error) =>
        OperatingSystem.IsMacOS() ? TryCopyMac(specs, theme, out error) : TryCopyWindows(specs, theme, out error);
    private static bool TryCopyWindows(IReadOnlyList<CardSpec> specs, Theme theme, [NotNullWhen(false)] out string? error) =>
        TryCopy(specs, theme, data => Clipboard.SetDataObject(data, true, 5, 100), out error);

    /// <summary>
    /// macOS payload: the same hover-free 2× transparent PNG ("public.png") plus an opaque PNG on the theme surface
    /// for the TIFF fallback ("public.tiff", converted natively), mirroring Windows' PNG + opaque DIB pair.
    /// </summary>
    internal static (byte[] Png, byte[] OpaquePng) MacPayload(IReadOnlyList<CardSpec> specs, Theme theme)
    {
        if (specs.Count == 0) throw new InvalidOperationException("There are no cards to copy.");
        var cards = specs.Select(BrushPresentation.WithoutHover).ToArray();
        using var png = new MemoryStream();
        using (var image = Render(cards, theme)) image.Save(png, ImageFormat.Png);
        using var opaque = new MemoryStream();
        using (var image = Render(cards, CardImageExport.OpaqueTheme(theme)))
        using (var flat = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb))
        {
            using (var graphics = Graphics.FromImage(flat))
            {
                var background = CardImageExport.Background(theme);
                graphics.Clear(Color.FromArgb(255, background.R, background.G, background.B));
                graphics.DrawImageUnscaled(image, 0, 0);
            }
            flat.Save(opaque, ImageFormat.Png);
        }
        return (png.ToArray(), opaque.ToArray());
    }

    private static bool TryCopyMac(IReadOnlyList<CardSpec> specs, Theme theme, [NotNullWhen(false)] out string? error)
    {
        try
        {
            var (png, opaque) = MacPayload(specs, theme);
            if (!ConduitPlus.Platform.MacPasteboard.Write(png, opaque, out var failure))
            {
                error = "Could not copy card as image: " + failure;
                return false;
            }
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            error = "Could not copy card as image: " + exception.Message;
            return false;
        }
    }

    // Keep payload verification independent of the system clipboard and the user's clipboard contents.
    internal static bool TryCopy(IReadOnlyList<CardSpec> specs, Theme theme, Action<DataObject> setData, [NotNullWhen(false)] out string? error)
    {
        try
        {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
                throw new InvalidOperationException("Copy card as image requires the UI (STA) thread.");
            if (specs.Count == 0) throw new InvalidOperationException("There are no cards to copy.");
            var cards = specs.Select(BrushPresentation.WithoutHover).ToArray();
            using var png = new MemoryStream();
            using (var image = Render(cards, theme)) image.Save(png, ImageFormat.Png);
            png.Position = 0;
            using var dib = OpaqueDib(cards, theme);
            // Only streams go on the clipboard: they are copied into HGLOBALs on flush. A managed Bitmap
            // (DataFormats.Bitmap) would make WinForms allocate an HBITMAP that it never frees.
            // Windows synthesizes CF_BITMAP for apps that ask for it from this CF_DIB.
            var data = new DataObject();
            data.SetData("PNG", false, png);
            data.SetData(DataFormats.Dib, false, dib);
            try { setData(data); }
            catch (ExternalException)
            {
                error = "Could not copy card as image: the clipboard is busy. Try again.";
                return false;
            }
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            error = "Could not copy card as image: " + exception.Message;
            return false;
        }
    }

    private static Bitmap Render(CardSpec[] cards, Theme theme) =>
        cards.Length == 1 ? RasterDrawing.Render(cards[0], theme, Scale) : RasterDrawing.RenderStack(cards, theme, Scale);

    /// <summary>Opaque 24-bit bottom-up CF_DIB (BITMAPINFOHEADER + pixels) on the theme surface.</summary>
    internal static MemoryStream OpaqueDib(CardSpec[] cards, Theme theme)
    {
        var opaqueTheme = CardImageExport.OpaqueTheme(theme);
        using var image = Render(cards, opaqueTheme);
        using var opaque = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(opaque))
        {
            var background = CardImageExport.Background(theme);
            graphics.Clear(Color.FromArgb(background.R, background.G, background.B));
            graphics.DrawImageUnscaled(image, 0, 0);
        }
        int width = opaque.Width, height = opaque.Height, stride = (width * 3 + 3) & ~3;
        var stream = new MemoryStream(40 + stride * height);
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(40); writer.Write(width); writer.Write(height); // positive height: bottom-up rows
            writer.Write((short)1); writer.Write((short)24); writer.Write(0); // planes, bit count, BI_RGB
            writer.Write(stride * height); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
            var bits = opaque.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                var row = new byte[stride];
                for (var y = height - 1; y >= 0; y--)
                {
                    Array.Clear(row);
                    Marshal.Copy(bits.Scan0 + y * bits.Stride, row, 0, width * 3);
                    writer.Write(row);
                }
            }
            finally { opaque.UnlockBits(bits); }
        }
        stream.Position = 0;
        return stream;
    }
}
