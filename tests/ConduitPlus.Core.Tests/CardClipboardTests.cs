using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows.Forms;
using ConduitPlus.Rendering;
using Xunit;

namespace ConduitPlus.Core.Tests;

public sealed class CardClipboardTests
{
    [Fact]
    public void MenuLabelsUseTitleOrUntitledKindAndNumberRepeats() => Assert.Equal(
        ["Area & volume", "Untitled Metric (1)", "Untitled Metric (2)", "Loads (1)", "Untitled Bar", "Loads (2)"],
        CardImageExport.MenuLabels([
            new() { Id = "a", Kind = CardKind.Metric, Title = "Area & volume" },
            new() { Id = "b", Kind = CardKind.Metric, Title = "" },
            new() { Id = "c", Kind = CardKind.Metric, Title = "  " },
            new() { Id = "d", Kind = CardKind.Bar, Title = "Loads" },
            new() { Id = "e", Kind = CardKind.Bar },
            new() { Id = "f", Kind = CardKind.Table, Title = "Loads" }]));

    [Theory]
    [InlineData("Paper", "#FFFFFFFF")]
    [InlineData("Night", "#1A1D23FF")]
    [InlineData("Smoke", "#12151CFF")]
    [InlineData("Blueprint", "#0A2654FF")]
    public void BitmapBackingUsesOpaqueThemeSurface(string name, string expected)
    {
        Assert.True(Theme.TryGet(name, out var theme));
        Assert.Equal(expected, CardImageExport.Background(theme).ToHex());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PayloadContainsTransparentPngAndOpaqueDibAtTwoTimesWithSelectionButNoHover(bool stack) => OnSta(() =>
    {
        var state = new CanvasCardState();
        state.Replace(new() { Id = "bar", Kind = CardKind.Bar, Keys = ["a", "b"], Labels = ["A", "B"], Values = [.5, .8] });
        state.Activate("a"); state.Hover("b");
        var card = state.Spec!;
        CardSpec[] cards = stack ? [card, new() { Id = "metric", Kind = CardKind.Metric, Title = "Area", Value = "320" }] : [card];
        var theme = Theme.Smoke;
        var called = false;
        var result = CardClipboard.TryCopy(cards, theme, data =>
        {
            called = true;
            // Streams only: a managed Bitmap payload makes WinForms leak an HBITMAP per copy.
            Assert.Equal([DataFormats.Dib, "PNG"], data.GetFormats(false).Order(StringComparer.Ordinal).ToArray());
            Assert.All(data.GetFormats(false), f => Assert.IsType<MemoryStream>(data.GetData(f, false)));
            var png = Assert.IsType<MemoryStream>(data.GetData("PNG", false));
            Assert.Equal(0, png.Position);
            using var decoded = new Bitmap(png);
            var size = new CanvasStackLayout(cards, theme).Size;
            Assert.Equal((int)Math.Ceiling(size.Width * 2), decoded.Width);
            Assert.Equal((int)Math.Ceiling(size.Height * 2), decoded.Height);
            Assert.Equal(0, decoded.GetPixel(0, 0).A);
            var dib = Dib.Read(Assert.IsType<MemoryStream>(data.GetData(DataFormats.Dib, false)));
            Assert.Equal((decoded.Width, decoded.Height), (dib.Width, dib.Height));
            Assert.Equal(Color.FromArgb(18, 21, 28).ToArgb(), dib.Pixel(0, 0).ToArgb());
            // What OLE hands other apps: both formats render to HGLOBAL byte-for-byte.
            Assert.Equal(dib.Raw, Native(data, DataFormats.Dib));
            Assert.Equal(png.ToArray(), Native(data, "PNG"));

            // The payload must match the selected presentation with every hover field removed.
            var clean = cards.Select(BrushPresentation.WithoutHover).ToArray();
            using var expected = RasterDrawing.RenderStack(clean, theme, 2);
            using var expectedPng = new MemoryStream(); expected.Save(expectedPng, ImageFormat.Png);
            Assert.Equal(expectedPng.ToArray(), png.ToArray());
            using var hovered = RasterDrawing.RenderStack(cards, theme, 2);
            using var hoveredPng = new MemoryStream(); hovered.Save(hoveredPng, ImageFormat.Png);
            Assert.False(hoveredPng.ToArray().SequenceEqual(png.ToArray()));
            var unselected = clean.Select(c => c with { SelectedKeys = [], OwnerHasSelection = false }).ToArray();
            using var plain = RasterDrawing.RenderStack(unselected, theme, 2);
            using var plainPng = new MemoryStream(); plain.Save(plainPng, ImageFormat.Png);
            Assert.False(plainPng.ToArray().SequenceEqual(png.ToArray()));
        }, out var error);
        Assert.True(result, error);
        Assert.True(called);
        Assert.Equal("b", card.HoverKey); // Copy must not mutate shared interaction state.
        Assert.Equal(["a"], card.SelectedKeys);
    });

    [Fact]
    public void OpaqueFallbackReChoosesAutomaticPlainTitleInkForTheSurface()
    {
        // Paper over a dark viewport resolves white plain-title ink; flattened on white it would vanish.
        var theme = Theme.Paper with { PlainTitleInk = "#FFFFFF", PlainTitleHalo = "#00000080" };
        var opaque = CardImageExport.OpaqueTheme(theme);
        Assert.Equal("#000000", opaque.PlainTitleInk);
        Assert.Null(opaque.PlainTitleHalo);
        Assert.Equal("#FFFFFF", CardImageExport.OpaqueTheme(Theme.Night with { PlainTitleInk = "#000000" }).PlainTitleInk);
    }

    [Fact]
    public void OpaqueDibShowsPlainTitleThatTheHostInkWouldHide() => OnSta(() =>
    {
        var theme = Theme.Paper with { PlainTitleInk = "#FFFFFF" };
        CardSpec[] title = [new() { Id = "t", Kind = CardKind.Title, Title = "Massing study" }];
        using var stream = CardClipboard.OpaqueDib(title, theme);
        var dib = Dib.Read(stream);
        var dark = 0;
        for (var y = 0; y < dib.Height; y++)
            for (var x = 0; x < dib.Width; x++)
                if (dib.Pixel(x, y).GetBrightness() < .5f) dark++;
        Assert.True(dark > 20, $"Expected dark title ink on the white surface, found {dark} dark pixels.");
    });

    [Fact]
    public void BusyClipboardIsNonFatal() => OnSta(() =>
    {
        Assert.False(CardClipboard.TryCopy([new() { Id = "m", Value = "1" }], Theme.Paper,
            _ => throw new ExternalException("busy"), out var error));
        Assert.Contains("clipboard is busy", error);
    });

    [Fact]
    public void EmptyCardsNeverWriteClipboard() => OnSta(() =>
    {
        Assert.False(CardClipboard.TryCopy([], Theme.Paper, _ => Assert.Fail("Unexpected clipboard write"), out var error));
        Assert.Contains("no cards", error);
    });

    [Fact]
    public void NonStaCallIsNonFatal() => OnApartment(() =>
    {
        Assert.False(CardClipboard.TryCopy([new() { Id = "m" }], Theme.Paper,
            _ => Assert.Fail("Unexpected clipboard write"), out var error));
        Assert.Contains("STA", error);
    }, ApartmentState.MTA);

    private static byte[] Native(DataObject data, string format)
    {
        var com = (System.Runtime.InteropServices.ComTypes.IDataObject)data;
        var request = new FORMATETC { cfFormat = (short)DataFormats.GetFormat(format).Id, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = TYMED.TYMED_HGLOBAL };
        com.GetData(ref request, out var medium);
        try
        {
            Assert.Equal(TYMED.TYMED_HGLOBAL, medium.tymed);
            var size = checked((int)GlobalSize(medium.unionmember)); var pointer = GlobalLock(medium.unionmember);
            try { var bytes = new byte[size]; Marshal.Copy(pointer, bytes, 0, size); return bytes; }
            finally { GlobalUnlock(medium.unionmember); }
        }
        finally { ReleaseStgMedium(ref medium); }
    }
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr handle);
    [DllImport("ole32.dll")] private static extern void ReleaseStgMedium(ref STGMEDIUM medium);

    private sealed record Dib(int Width, int Height, int Stride, byte[] Bits, byte[] Raw)
    {
        public static Dib Read(MemoryStream stream)
        {
            var bytes = stream.ToArray();
            Assert.Equal(40, BitConverter.ToInt32(bytes, 0));
            int width = BitConverter.ToInt32(bytes, 4), height = BitConverter.ToInt32(bytes, 8);
            Assert.True(height > 0); // bottom-up
            Assert.Equal(24, BitConverter.ToInt16(bytes, 14));
            Assert.Equal(0, BitConverter.ToInt32(bytes, 16)); // BI_RGB
            var stride = (width * 3 + 3) & ~3;
            Assert.Equal(40 + stride * height, bytes.Length);
            return new(width, height, stride, bytes[40..], bytes);
        }
        public Color Pixel(int x, int y)
        {
            var i = (Height - 1 - y) * Stride + x * 3;
            return Color.FromArgb(Bits[i + 2], Bits[i + 1], Bits[i]);
        }
    }

    private static void OnSta(Action action) => OnApartment(action, ApartmentState.STA);

    private static void OnApartment(Action action, ApartmentState apartment)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { error = exception; } });
        thread.SetApartmentState(apartment); thread.Start(); thread.Join();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
