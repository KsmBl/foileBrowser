using System.Drawing;
using System.Runtime.InteropServices;

namespace FoileBrowser.Views;

/// <summary>
/// Photographs the running window from inside the process, so the screenshots in the docs can be
/// regenerated on any machine and a change can be checked visually without a person at the screen.
///
/// Shelling out to a desktop screenshot tool is not dependable: ImageMagick's <c>import</c> built
/// without its X11 delegate exits zero having written nothing, and under a rootless Xwayland the
/// pixels belong to the Wayland compositor rather than to any X client. Asking the widgets to paint
/// themselves sidesteps the display server, and gives the toolkit's own output rather than whatever
/// happened to be stacked on the desktop.
///
/// The technique is the one <c>NativeForms.Demo</c> uses for its gallery shots; the Windows half is
/// the same idea through <c>PrintWindow</c>.
/// </summary>
internal static partial class Screenshot
{
    /// <summary>The widest capture attempted, so a nonsense geometry cannot ask for gigabytes.</summary>
    private const int MaxExtent = 8192;

    /// <summary>Writes a PNG of every window this process has on screen; false when nothing was drawn.</summary>
    public static bool TryCapture(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
            return OperatingSystem.IsWindows() ? Win32.Capture(path) : Gtk.Capture(path);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or IOException)
        {
            return false;
        }
    }

    // ---- GTK: composite every mapped toplevel through the toolkit's own draw pipeline ----

    private static unsafe partial class Gtk
    {
        private const string Lib = "libgtk-3.so.0";
        private const string Gdk = "libgdk-3.so.0";
        private const string Cairo = "libcairo.so.2";
        private const string GLib = "libglib-2.0.so.0";

        private const int CairoFormatRgb24 = 1;
        private const int CairoStatusSuccess = 0;
        private const int GtkWindowPopup = 1;

        [LibraryImport(Lib)] private static partial void gtk_widget_draw(nint widget, nint cr);
        [LibraryImport(Lib)] private static partial void gtk_test_widget_wait_for_draw(nint widget);
        [LibraryImport(Lib)] private static partial nint gtk_window_list_toplevels();
        [LibraryImport(Lib)] private static partial nint gtk_widget_get_window(nint widget);
        [LibraryImport(Lib)] private static partial int gtk_widget_get_mapped(nint widget);
        [LibraryImport(Lib)] private static partial nint gtk_bin_get_child(nint bin);
        [LibraryImport(Lib)] private static partial int gtk_window_get_window_type(nint window);

        [LibraryImport(Gdk)] private static partial void gdk_window_get_origin(nint window, out int x, out int y);
        [LibraryImport(Gdk)] private static partial int gdk_window_get_width(nint window);
        [LibraryImport(Gdk)] private static partial int gdk_window_get_height(nint window);

        [LibraryImport(GLib)] private static partial uint g_list_length(nint list);
        [LibraryImport(GLib)] private static partial nint g_list_nth_data(nint list, uint n);
        [LibraryImport(GLib)] private static partial void g_list_free(nint list);

        [LibraryImport(Cairo)] private static partial nint cairo_image_surface_create(int format, int width, int height);
        [LibraryImport(Cairo)] private static partial nint cairo_create(nint surface);
        [LibraryImport(Cairo)] private static partial void cairo_destroy(nint cr);
        [LibraryImport(Cairo)] private static partial void cairo_surface_destroy(nint surface);
        [LibraryImport(Cairo)] private static partial void cairo_surface_flush(nint surface);
        [LibraryImport(Cairo)] private static partial void cairo_save(nint cr);
        [LibraryImport(Cairo)] private static partial void cairo_restore(nint cr);
        [LibraryImport(Cairo)] private static partial void cairo_translate(nint cr, double tx, double ty);
        [LibraryImport(Cairo)] private static partial void cairo_set_source_rgb(nint cr, double r, double g, double b);
        [LibraryImport(Cairo)] private static partial void cairo_paint(nint cr);

        [LibraryImport(Cairo, StringMarshalling = StringMarshalling.Utf8)]
        private static partial int cairo_surface_write_to_png(nint surface, string filename);

        public static bool Capture(string path)
        {
            var layers = MappedLayers();
            if (layers.Count == 0)
                return false;

            var frame = layers[0].Bounds;
            foreach (var layer in layers)
                frame = Rectangle.Union(frame, layer.Bounds);

            if (frame.Width <= 0 || frame.Height <= 0 || frame.Width > MaxExtent || frame.Height > MaxExtent)
                return false;

            // Let the anchor window finish the frame it owes before its pixels are read. Only the
            // first: a modal dialog stacked over it is mid-realization often enough that asking it
            // to settle trips a GTK assertion, and it is about to be drawn synchronously anyway.
            if (layers.Count > 0)
                gtk_test_widget_wait_for_draw(layers[0].Widget);

            var surface = cairo_image_surface_create(CairoFormatRgb24, frame.Width, frame.Height);
            var cr = cairo_create(surface);
            try
            {
                cairo_set_source_rgb(cr, 0.36, 0.36, 0.38); // the neutral grey a desktop shows behind
                cairo_paint(cr);

                foreach (var layer in layers)
                {
                    cairo_save(cr);
                    cairo_translate(cr, layer.Bounds.X - frame.X, layer.Bounds.Y - frame.Y);
                    // A popup renders nothing through its own toplevel, so its single child is drawn
                    // instead; an ordinary window paints itself and must not be drawn twice.
                    if (gtk_window_get_window_type(layer.Widget) == GtkWindowPopup)
                    {
                        var content = gtk_bin_get_child(layer.Widget);
                        if (content != 0)
                            gtk_widget_draw(content, cr);
                    }
                    else
                    {
                        gtk_widget_draw(layer.Widget, cr);
                    }

                    cairo_restore(cr);
                }

                cairo_surface_flush(surface);
                return cairo_surface_write_to_png(surface, path) == CairoStatusSuccess;
            }
            finally
            {
                cairo_destroy(cr);
                cairo_surface_destroy(surface);
            }
        }

        /// <summary>Mapped toplevels with their screen rectangles, oldest first — the order they stack in.</summary>
        private static List<(nint Widget, Rectangle Bounds)> MappedLayers()
        {
            var result = new List<(nint, Rectangle)>();
            var list = gtk_window_list_toplevels();
            try
            {
                var count = g_list_length(list);
                for (var i = 0u; i < count; ++i)
                {
                    var widget = g_list_nth_data(list, i);
                    if (widget == 0 || gtk_widget_get_mapped(widget) == 0)
                        continue;

                    var window = gtk_widget_get_window(widget);
                    if (window == 0)
                        continue;

                    gdk_window_get_origin(window, out var x, out var y);
                    var bounds = new Rectangle(x, y, gdk_window_get_width(window), gdk_window_get_height(window));
                    if (bounds.Width > 0 && bounds.Height > 0)
                        result.Add((widget, bounds));
                }
            }
            finally
            {
                g_list_free(list);
            }

            return result;
        }
    }

    // ---- Win32: ask the window to print itself, then write the DIB out as a PNG ----

    private static unsafe partial class Win32
    {
        private const string User = "user32.dll";
        private const string Gdi = "gdi32.dll";

        /// <summary>PW_RENDERFULLCONTENT — includes child windows and non-GDI content.</summary>
        private const uint PrintFullContent = 2;

        private const int BiRgb = 0;
        private const int DibRgbColors = 0;

        [LibraryImport(User)] [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool PrintWindow(nint window, nint deviceContext, uint flags);

        [LibraryImport(User)] private static partial nint GetDC(nint window);
        [LibraryImport(User)] private static partial int ReleaseDC(nint window, nint dc);

        [LibraryImport(User)] [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetClientRect(nint window, out Rect rect);

        [LibraryImport(User)] [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetWindowRect(nint window, out Rect rect);

        [LibraryImport(User)] [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool IsWindowVisible(nint window);

        [LibraryImport(User)] private static partial uint GetWindowThreadProcessId(nint window, out uint processId);

        [LibraryImport(User)] [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool EnumWindows(delegate* unmanaged<nint, nint, int> callback, nint parameter);

        [LibraryImport(User)] [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool EnumChildWindows(nint parent, delegate* unmanaged<nint, nint, int> callback, nint parameter);


        [LibraryImport(Gdi)] private static partial nint CreateCompatibleDC(nint dc);

        [LibraryImport(Gdi)] [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool BitBlt(
            nint destination, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint rop);
        [LibraryImport(Gdi)] private static partial nint SelectObject(nint dc, nint obj);
        [LibraryImport(Gdi)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DeleteDC(nint dc);
        [LibraryImport(Gdi)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DeleteObject(nint obj);

        [LibraryImport(Gdi)]
        private static partial nint CreateDIBSection(
            nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfo
        {
            public uint Size;
            public int Width, Height;
            public ushort Planes, BitCount;
            public uint Compression, SizeImage;
            public int XPelsPerMeter, YPelsPerMeter;
            public uint ClrUsed, ClrImportant;
            public uint FirstColour;
        }

        /// <summary>Collected by the enumeration callback, which cannot capture state.</summary>
        private static readonly List<nint> Found = [];
        private static uint _wanted;

        /// <summary>SRCCOPY.</summary>
        private const uint CopySource = 0x00CC0020;

        public static bool Capture(string path)
        {
            var window = MainWindow();
            if (window == 0 || !GetWindowRect(window, out var rect))
                return false;

            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0 || width > MaxExtent || height > MaxExtent)
                return false;

            var info = new BitmapInfo
            {
                Size = (uint)Marshal.SizeOf<BitmapInfo>() - sizeof(uint),
                Width = width,
                Height = -height, // top-down, so the rows come out in image order
                Planes = 1,
                BitCount = 32,
                Compression = BiRgb,
            };

            var screen = GetDC(0);
            var memory = CreateCompatibleDC(screen);
            var bitmap = CreateDIBSection(memory, ref info, DibRgbColors, out var bits, 0, 0);
            var previous = SelectObject(memory, bitmap);
            try
            {
                if (bitmap == 0 || bits == 0)
                    return false;

                // Three routes, because no single one is dependable everywhere. Reading the
                // composited pixels off the screen is the most faithful but is refused on a display
                // whose contents belong to a compositor; PrintWindow covers a real Windows desktop;
                // and printing each child in turn is what is left under Wine, whose PrintWindow does
                // not descend into owner-drawn surfaces.
                var drawn = BitBlt(memory, 0, 0, width, height, screen, rect.Left, rect.Top, CopySource)
                    && !IsBlank(bits, width, height);

                if (!drawn)
                    drawn = PrintWindow(window, memory, PrintFullContent) && !IsBlank(bits, width, height);

                PrintChildren(window, memory, rect);
                if (!drawn && IsBlank(bits, width, height))
                    return false;

                var pixels = new int[width * height];
                Marshal.Copy(bits, pixels, 0, pixels.Length);
                for (var i = 0; i < pixels.Length; ++i)
                    pixels[i] = unchecked((int)((uint)pixels[i] | 0xFF000000)); // PrintWindow leaves alpha at zero

                return Png.Write(path, width, height, pixels);
            }
            finally
            {
                SelectObject(memory, previous);
                if (bitmap != 0)
                    DeleteObject(bitmap);
                DeleteDC(memory);
                ReleaseDC(0, screen);
            }
        }

        /// <summary>Whether nothing was written — every pixel still the bitmap's initial zero.</summary>
        private static bool IsBlank(nint bits, int width, int height)
        {
            var sample = new int[Math.Min(width * height, 4096)];
            Marshal.Copy(bits, sample, 0, sample.Length);
            foreach (var pixel in sample)
                if ((pixel & 0x00FFFFFF) != 0)
                    return false;

            return true;
        }

        /// <summary>
        /// Asks every visible child window to print itself into the composite at its own offset.
        /// Under Wine this is what actually captures the owner-drawn surfaces, since each of them is
        /// a real child window that PrintWindow on the parent does not descend into.
        /// </summary>
        private static void PrintChildren(nint parent, nint target, Rect frame)
        {
            _children.Clear();
            EnumChildWindows(parent, &OnChild, 0);

            foreach (var child in _children)
            {
                if (!GetWindowRect(child, out var bounds))
                    continue;

                var width = bounds.Right - bounds.Left;
                var height = bounds.Bottom - bounds.Top;
                if (width <= 0 || height <= 0)
                    continue;

                var info = new BitmapInfo
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfo>() - sizeof(uint),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = BiRgb,
                };

                var dc = CreateCompatibleDC(target);
                var bitmap = CreateDIBSection(dc, ref info, DibRgbColors, out var bits, 0, 0);
                var previous = SelectObject(dc, bitmap);
                try
                {
                    if (bitmap != 0 && bits != 0 && PrintWindow(child, dc, PrintFullContent) && !IsBlank(bits, width, height))
                        BitBlt(target, bounds.Left - frame.Left, bounds.Top - frame.Top, width, height, dc, 0, 0, CopySource);
                }
                finally
                {
                    SelectObject(dc, previous);
                    if (bitmap != 0)
                        DeleteObject(bitmap);
                    DeleteDC(dc);
                }
            }
        }

        private static readonly List<nint> _children = [];

        [UnmanagedCallersOnly]
        private static int OnChild(nint window, nint parameter)
        {
            if (IsWindowVisible(window))
                _children.Add(window);
            return 1;
        }

        /// <summary>This process's first visible toplevel.</summary>
        private static unsafe nint MainWindow()
        {
            Found.Clear();
            _wanted = (uint)Environment.ProcessId;
            EnumWindows(&OnWindow, 0);
            return Found.Count > 0 ? Found[0] : 0;
        }

        [UnmanagedCallersOnly]
        private static int OnWindow(nint window, nint parameter)
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner == _wanted && IsWindowVisible(window))
                Found.Add(window);
            return 1;
        }
    }

    // ---- a minimal PNG writer, so the Windows path needs no image library ----

    private static class Png
    {
        public static bool Write(string path, int width, int height, int[] argb)
        {
            using var file = File.Create(path);
            file.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

            Span<byte> header = stackalloc byte[13];
            WriteBigEndian(header[..4], (uint)width);
            WriteBigEndian(header[4..8], (uint)height);
            header[8] = 8;  // bits per channel
            header[9] = 2;  // truecolour
            Chunk(file, "IHDR", header);

            // One filter byte per row, then BGR from the DIB reordered to RGB.
            var raw = new byte[height * ((width * 3) + 1)];
            var offset = 0;
            for (var y = 0; y < height; ++y)
            {
                raw[offset++] = 0; // filter: none
                for (var x = 0; x < width; ++x)
                {
                    var pixel = argb[(y * width) + x];
                    raw[offset++] = (byte)(pixel >> 16);
                    raw[offset++] = (byte)(pixel >> 8);
                    raw[offset++] = (byte)pixel;
                }
            }

            Chunk(file, "IDAT", Deflate(raw));
            Chunk(file, "IEND", []);
            return true;
        }

        /// <summary>zlib-wrapped deflate: the two-byte header, the raw stream, then Adler-32.</summary>
        private static byte[] Deflate(byte[] data)
        {
            using var buffer = new MemoryStream();
            buffer.WriteByte(0x78);
            buffer.WriteByte(0x01);
            using (var deflate = new System.IO.Compression.DeflateStream(
                buffer, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
                deflate.Write(data);

            Span<byte> adler = stackalloc byte[4];
            WriteBigEndian(adler, Adler32(data));
            buffer.Write(adler);
            return buffer.ToArray();
        }

        private static uint Adler32(ReadOnlySpan<byte> data)
        {
            uint a = 1, b = 0;
            foreach (var value in data)
            {
                a = (a + value) % 65521;
                b = (b + a) % 65521;
            }

            return (b << 16) | a;
        }

        private static void Chunk(Stream stream, string type, ReadOnlySpan<byte> payload)
        {
            Span<byte> length = stackalloc byte[4];
            WriteBigEndian(length, (uint)payload.Length);
            stream.Write(length);

            var tagged = new byte[4 + payload.Length];
            for (var i = 0; i < 4; ++i)
                tagged[i] = (byte)type[i];
            payload.CopyTo(tagged.AsSpan(4));
            stream.Write(tagged);

            Span<byte> crc = stackalloc byte[4];
            WriteBigEndian(crc, Crc32(tagged));
            stream.Write(crc);
        }

        private static uint Crc32(ReadOnlySpan<byte> data)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var value in data)
            {
                crc ^= value;
                for (var bit = 0; bit < 8; ++bit)
                    crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
            }

            return ~crc;
        }

        private static void WriteBigEndian(Span<byte> destination, uint value) =>
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(destination, value);
    }
}
