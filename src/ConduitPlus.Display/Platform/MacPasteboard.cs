namespace ConduitPlus.Platform;

/// <summary>NSPasteboard writer for card images (main thread). Not reachable on Windows.</summary>
internal static class MacPasteboard
{
    internal static bool Write(byte[] png, byte[] opaquePng, out string? error)
    {
        error = null;
        var board = ObjC.Send(ObjC.Class("NSPasteboard"), ObjC.Sel("generalPasteboard"));
        if (board == IntPtr.Zero) { error = "the system pasteboard is unavailable."; return false; }
        ObjC.SendLong(board, ObjC.Sel("clearContents"));
        if (!ObjC.SendBool(board, ObjC.Sel("setData:forType:"), ObjC.NSData(png), ObjC.NSString("public.png")))
        { error = "the pasteboard rejected the PNG image. Try again."; return false; }
        // TIFF is what many Mac apps read first; NSImage converts the opaque rendering natively.
        var image = ObjC.Send(ObjC.Send(ObjC.Class("NSImage"), ObjC.Sel("alloc")), ObjC.Sel("initWithData:"), ObjC.NSData(opaquePng));
        if (image != IntPtr.Zero)
        {
            try
            {
                var tiff = ObjC.Send(image, ObjC.Sel("TIFFRepresentation"));
                if (tiff != IntPtr.Zero) ObjC.SendBool(board, ObjC.Sel("setData:forType:"), tiff, ObjC.NSString("public.tiff"));
            }
            finally { ObjC.Send(image, ObjC.Sel("release")); }
        }
        return true;
    }

    /// <summary>Types and byte sizes currently on the general pasteboard (diagnostics).</summary>
    internal static string[] Describe()
    {
        var board = ObjC.Send(ObjC.Class("NSPasteboard"), ObjC.Sel("generalPasteboard"));
        var types = ObjC.Send(board, ObjC.Sel("types"));
        if (types == IntPtr.Zero) return [];
        var count = (int)ObjC.SendULong(types, ObjC.Sel("count"));
        var result = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var type = ObjC.Send(types, ObjC.Sel("objectAtIndex:"), (IntPtr)i);
            var data = ObjC.Send(board, ObjC.Sel("dataForType:"), type);
            result.Add($"{ObjC.FromNSString(type)} ({(data == IntPtr.Zero ? 0 : ObjC.SendULong(data, ObjC.Sel("length")))} bytes)");
        }
        return result.ToArray();
    }

    internal static byte[] Read(string type)
    {
        var board = ObjC.Send(ObjC.Class("NSPasteboard"), ObjC.Sel("generalPasteboard"));
        return ObjC.Bytes(ObjC.Send(board, ObjC.Sel("dataForType:"), ObjC.NSString(type)));
    }
}
