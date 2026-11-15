using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Vanara.PInvoke;
using Vanara.Windows.Shell;
using vSuite;

namespace vSuite.Shell
{
    /// <summary>
    /// Loads shell icons on threads of their own (STA, as shell extensions expect) and hands them to the UI thread.
    /// - Shared icons (a key: one per extension, "&lt;folder&gt;") come from the file type alone - no file is opened, so a network share
    ///   costs nothing - and once loaded they're handed out straight away, without a trip through the queue.
    /// - An item's own icon (.exe, .lnk, a sidebar folder) on a network drive goes to a second, slow queue, so the round trips to the
    ///   server never hold up the list's icons.
    /// </summary>
    public sealed class ShellIcons : IDisposable
    {
        readonly BlockingCollection<Action> _queue = new BlockingCollection<Action>();
        readonly BlockingCollection<Action> _slow = new BlockingCollection<Action>();
        readonly ConcurrentDictionary<string, ImageSource> _cache = new ConcurrentDictionary<string, ImageSource>();
        readonly Dispatcher _ui;

        /// <summary>Icon size in device pixels (16 DIPs at the window's DPI).</summary>
        public int PixelSize = 16;

        /// <summary>Threads for the local queue: an .exe / .lnk's own icon is read from the file, and a few at once is ~2.5x faster.</summary>
        const int IconThreads = 3;

        public ShellIcons(Dispatcher ui)
        {
            _ui = ui;
            Start(_queue, "Shell icons", IconThreads, "Icon");
            Start(_slow, "Shell icons (network)", 1, "Icon");
        }

        // ---- handing images to the UI thread in batches: one dispatcher trip per image (and a redraw between each) was what made a
        //      big folder's icons trickle in - the loading was done in ~0.2 s, the last of 544 icons showed ~0.6 s later ----

        readonly ConcurrentQueue<KeyValuePair<Action<ImageSource>, ImageSource>> _ready = new ConcurrentQueue<KeyValuePair<Action<ImageSource>, ImageSource>>();
        int _drainQueued;
        /// <summary>How long one batch may keep the UI thread, so a flood of icons never makes the window stutter.</summary>
        const int BatchMs = 15;

        void Deliver(Action<ImageSource> done, ImageSource img)
        {
            _ready.Enqueue(new KeyValuePair<Action<ImageSource>, ImageSource>(done, img));
            if (Interlocked.Exchange(ref _drainQueued, 1) == 0) _ui.BeginInvoke(DispatcherPriority.Background, new Action(Drain));
        }

        void Drain()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            KeyValuePair<Action<ImageSource>, ImageSource> r;
            while (sw.ElapsedMilliseconds < BatchMs && _ready.TryDequeue(out r))
            {
                try { r.Key(r.Value); }
                catch (Exception ex) { Log.Write("Icon: " + ex.Message); }
            }
            Volatile.Write(ref _drainQueued, 0);
            // more came in (or the time ran out): another batch after whatever else is waiting (input, rendering)
            if (!_ready.IsEmpty && Interlocked.Exchange(ref _drainQueued, 1) == 0) _ui.BeginInvoke(DispatcherPriority.Background, new Action(Drain));
        }

        static void Start(BlockingCollection<Action> q, string name, int threads, string what)
        {
            for (int i = 0; i < threads; i++)
            {
                var t = new Thread(() =>
                {
                    foreach (Action a in q.GetConsumingEnumerable())
                    {
                        try { a(); }
                        catch (Exception ex) { Log.Write(what + ": " + ex.Message); }
                    }
                }) { IsBackground = true, Name = threads > 1 ? name + " " + (i + 1) : name };
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
            }
        }

        // ---- network paths: a UNC path, or a drive letter mapped to one (checked once per drive) ----
        static readonly ConcurrentDictionary<string, bool> RemoteRoots = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern uint GetDriveType(string root);

        public static bool IsRemote(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (path.StartsWith(@"\\") && !path.StartsWith(@"\\?\")) return true;
            if (path.Length < 2 || path[1] != ':') return false;
            string root = path.Substring(0, 2) + "\\";
            return RemoteRoots.GetOrAdd(root, r => GetDriveType(r) == 4);   // DRIVE_REMOTE
        }

        /// <summary>
        /// The sidebar's key for a folder: a plain folder on a network drive shares the standard folder icon (its own would mean a trip
        /// to the server for each one); drives, shares and local folders keep their own (Downloads, Pictures â€¦).
        /// </summary>
        public static string NodeKey(string path)
        {
            if (!IsRemote(path)) return "node:" + path;
            string p = path.TrimEnd('\\');
            int parts = p.StartsWith(@"\\") ? p.Substring(2).Split('\\').Length : p.Split('\\').Length;
            bool shareOrDrive = p.StartsWith(@"\\") ? parts <= 2 : parts <= 1;
            return shareOrDrive ? "node:" + path : "<folder>";
        }

        /// <summary>Load path's icon (or key's shared one) and call done with it on the UI thread; skipped once wanted() is false.</summary>
        public void Request(string path, string key, Func<bool> wanted, Action<ImageSource> done)
        {
            if (_queue.IsAddingCompleted) return;
            int px = PixelSize;
            string k = (key ?? "path:" + path) + "@" + px;
            ImageSource ready;
            if (_cache.TryGetValue(k, out ready))   // loaded before: now, no queue
            {
                if (ready != null)
                {
                    if (_ui.CheckAccess()) done(ready);
                    else Deliver(done, ready);
                }
                return;
            }
            bool byType = key != null && (key == "<folder>" || key.StartsWith("ext:"));
            BlockingCollection<Action> q = !byType && IsRemote(path) ? _slow : _queue;
            q.Add(() =>
            {
                if (wanted != null && !wanted()) return;
                ImageSource img;
                if (!_cache.TryGetValue(k, out img))
                {
                    img = byType ? LoadType(key == "<folder>" ? "" : key.Substring(4), key == "<folder>", px) : Load(path, px);
                    _cache[k] = img;
                }
                if (img != null) Deliver(done, img);
            });
        }

        // ---- thumbnails (icon views, the preview pane): newest request first, so what's on screen now loads first ----
        // Two steps: Windows' thumbnail cache is asked first (~1-2 ms), and only what isn't in it goes on to be made (~50 ms each: a
        // picture decoded, a folder read) - so thumbnails seen before don't wait behind new ones.

        readonly BlockingCollection<Action> _thumbsCached = new BlockingCollection<Action>(new ConcurrentStack<Action>());
        readonly BlockingCollection<Action> _thumbs = new BlockingCollection<Action>(new ConcurrentStack<Action>());
        int _thumbsStarted;
        const int CacheThreads = 2;
        // a few at once: on a network share each one mostly waits for the server (a folder's thumbnail reads inside it)
        const int ThumbThreads = 4;

        /// <summary>path's thumbnail (or its icon at that size, when it has none), px square; skipped once wanted() is false.</summary>
        public void RequestThumb(string path, int px, Func<bool> wanted, Action<ImageSource> done)
        {
            if (_thumbsCached.IsAddingCompleted) return;
            if (Interlocked.Exchange(ref _thumbsStarted, 1) == 0)
            {
                Start(_thumbsCached, "Thumbnails (cache)", CacheThreads, "Thumbnail");
                Start(_thumbs, "Thumbnails", ThumbThreads, "Thumbnail");
            }
            _thumbsCached.Add(() =>
            {
                if (wanted != null && !wanted()) return;
                ImageSource img = Thumb(path, px, ShellItemGetImageOptions.ResizeToFit | ShellItemGetImageOptions.InCacheOnly, true);
                if (img != null) { Deliver(done, img); return; }
                if (_thumbs.IsAddingCompleted) return;
                try
                {
                    _thumbs.Add(() =>
                    {
                        if (wanted != null && !wanted()) return;
                        ImageSource made = Thumb(path, px, ShellItemGetImageOptions.ResizeToFit, false);
                        if (made != null) Deliver(done, made);
                    });
                }
                catch (InvalidOperationException) { }   // closing meanwhile
            });
        }

        /// <summary>The image from the shell; with quiet, a miss (not in the cache) is null rather than an error.</summary>
        static ImageSource Thumb(string path, int px, ShellItemGetImageOptions options, bool quiet)
        {
            try
            {
                using (var item = new ShellItem(path))
                using (var bmp = item.GetImage(new SIZE(px, px), options))
                    return FromHBitmap(bmp.DangerousGetHandle());
            }
            catch (Exception) when (quiet) { return null; }
        }

        /// <summary>A file type's icon without a file of that type (".txt", or a folder) - for the New menu.</summary>
        public void RequestForType(string ext, bool folder, Action<ImageSource> done)
        {
            if (_queue.IsAddingCompleted) return;
            _queue.Add(() =>
            {
                string k = "type:" + (folder ? "<folder>" : ext);
                ImageSource img;
                if (!_cache.TryGetValue(k, out img))
                {
                    img = LoadType(ext, folder);
                    _cache[k] = img;
                }
                if (img != null) Deliver(done, img);
            });
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEINFO
        {
            public IntPtr hIcon; public int iIcon; public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr icon);

        [DllImport("user32.dll")]
        static extern int GetSystemMetrics(int index);

        /// <summary>A file type's icon from its extension alone (no file is opened): Windows' small icon, or its large one when px is bigger.</summary>
        static ImageSource LoadType(string ext, bool folder, int px = 16)
        {
            var info = new SHFILEINFO();
            const uint SHGFI_ICON = 0x100, SHGFI_SMALLICON = 0x1, SHGFI_LARGEICON = 0x0, SHGFI_USEFILEATTRIBUTES = 0x10;
            uint size = px > GetSystemMetrics(49 /*SM_CXSMICON*/) ? SHGFI_LARGEICON : SHGFI_SMALLICON;
            SHGetFileInfo(folder ? "folder" : "file" + ext, folder ? 0x10u : 0x80u, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(),
                          SHGFI_ICON | size | SHGFI_USEFILEATTRIBUTES);
            if (info.hIcon == IntPtr.Zero) return null;
            try
            {
                BitmapSource bs = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bs.Freeze();
                return bs;
            }
            finally { DestroyIcon(info.hIcon); }
        }

        /// <summary>Forget the cached icons (the DPI changed).</summary>
        public void Clear() { _cache.Clear(); }

        static ImageSource Load(string path, int px)
        {
            using (var item = new ShellItem(path))
            using (var bmp = item.GetImage(new SIZE(px, px), ShellItemGetImageOptions.IconOnly | ShellItemGetImageOptions.ResizeToFit))
                return FromHBitmap(bmp.DangerousGetHandle());
        }

        // ---- HBITMAP (a 32-bit DIB with premultiplied alpha) -> BitmapSource, keeping the alpha ----

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
            public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct DIBSECTION { public BITMAP dsBm; public BITMAPINFOHEADER dsBmih; public uint b0, b1, b2; public IntPtr dshSection; public uint dsOffset; }

        [DllImport("gdi32.dll", EntryPoint = "GetObjectW")]
        static extern int GetObject(IntPtr h, int size, out DIBSECTION ds);

        [DllImport("gdi32.dll")]
        static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, [Out] byte[] bits, ref BITMAPINFOHEADER bmi, uint usage);

        [DllImport("user32.dll")]
        static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        static BitmapSource FromHBitmap(IntPtr h)
        {
            if (h == IntPtr.Zero) return null;
            DIBSECTION ds;
            int got = GetObject(h, Marshal.SizeOf<DIBSECTION>(), out ds);
            BitmapSource result;
            if (got > 0 && ds.dsBm.bmBits != IntPtr.Zero && ds.dsBm.bmBitsPixel == 32)
            {
                // Icons come back bottom-up and thumbnails top-down, and GetObject doesn't say which: ask GDI for the rows top-down
                // (a negative height), which it gets right either way.
                int w = ds.dsBm.bmWidth, ht = ds.dsBm.bmHeight, stride = w * 4;
                var px = new byte[stride * ht];
                var bi = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -ht, biPlanes = 1, biBitCount = 32, biCompression = 0 };
                IntPtr dc = GetDC(IntPtr.Zero);
                int lines;
                try { lines = GetDIBits(dc, h, 0, (uint)ht, px, ref bi, 0); }
                finally { ReleaseDC(IntPtr.Zero, dc); }
                if (lines == 0) return null;
                bool anyAlpha = false;
                for (int i = 3; i < px.Length; i += 4) if (px[i] != 0) { anyAlpha = true; break; }
                if (!anyAlpha) for (int i = 3; i < px.Length; i += 4) px[i] = 255;
                result = BitmapSource.Create(w, ht, 96, 96, PixelFormats.Pbgra32, null, px, stride);
            }
            else
            {
                result = Imaging.CreateBitmapSourceFromHBitmap(h, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            }
            result.Freeze();
            return result;
        }

        public void Dispose() { _queue.CompleteAdding(); _slow.CompleteAdding(); _thumbsCached.CompleteAdding(); _thumbs.CompleteAdding(); }
    }
}
