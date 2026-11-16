using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace vSuite.Ui
{
    /// <summary>
    /// vShell's Blur and Frosted for the suite's windows, drawn the way vShell's bars and widgets draw them (moved here from vShell's
    /// Backdrop): the desktop wallpaper, blurred once per monitor at 1/8 size, shows behind the window under the theme colour at the
    /// theme's opacity - so the opacity, Blur and Frosted look the same as on the bars. It shows the wallpaper, not the windows behind.
    /// (Windows' own Mica backdrop was used before: nearly solid, so the opacity made no visible difference.)
    /// </summary>
    public static class Backdrop
    {
        const double Shrink = 0.125;        // the blurred pictures: 1/8 of the monitor (blur hides the detail anyway)
        const double ClearShrink = 0.5;     // the plain one (Blur and Frosted off): half size, so it stays sharp

        static double ShrinkFor(string mode) { return mode == "Clear" ? ClearShrink : Shrink; }

        /// <summary>Raised (on the UI thread) when the wallpaper or the monitors changed and the pictures were made again.</summary>
        public static event Action Changed;

        static readonly Dictionary<string, BitmapSource> Cache = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
        static string _key;
        static ImageBrush _noise;
        static DispatcherTimer _timer;

        // ---------------- monitors ----------------

        internal sealed class Monitor { public string DeviceName; public Int32Rect Bounds; }

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct MONITORINFOEX
        {
            public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        }
        delegate bool MonitorEnumProc(IntPtr hMon, IntPtr hdc, ref RECT r, IntPtr data);
        [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFOEX mi);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

        static Monitor Info(IntPtr h)
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf(typeof(MONITORINFOEX)) };
            if (!GetMonitorInfo(h, ref mi)) return null;
            return new Monitor { DeviceName = mi.szDevice, Bounds = new Int32Rect(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right - mi.rcMonitor.Left, mi.rcMonitor.Bottom - mi.rcMonitor.Top) };
        }

        internal static List<Monitor> Monitors()
        {
            var list = new List<Monitor>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr dc, ref RECT r, IntPtr d) => { Monitor m = Info(h); if (m != null) list.Add(m); return true; }, IntPtr.Zero);
            return list;
        }

        internal static Monitor MonitorAt(int x, int y) { return Info(MonitorFromPoint(new POINT { X = x, Y = y }, 2 /* MONITOR_DEFAULTTONEAREST */)); }

        // ---------------- Windows' wallpaper ----------------

        enum Position { Center = 0, Tile = 1, Stretch = 2, Fit = 3, Fill = 4, Span = 5 }

        [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDesktopWallpaper
        {
            void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
            [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
            [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint monitorIndex);
            uint GetMonitorDevicePathCount();
            RECT GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
            void SetBackgroundColor(uint color);
            uint GetBackgroundColor();
            void SetPosition(Position position);
            Position GetPosition();
        }

        static readonly Guid DesktopWallpaperClsid = new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD");

        // one IDesktopWallpaper for the app (made on the UI thread, where every caller is); dropped after a failure and made again next time
        // (by its CLSID: the app may have made the same object through its own class first - see ComObject)
        static IDesktopWallpaper _dw;
        static IDesktopWallpaper Wallpaper()
        {
            if (_dw == null) _dw = ComObject.Create<IDesktopWallpaper>(DesktopWallpaperClsid);
            return _dw;
        }

        // ---------------- is the picture's file there, and when was it written ----------------
        // Asked every few seconds on the UI thread (Check). A wallpaper on a network share that has gone away would hold every
        // window for the network's timeout each time - so the file is looked at on another thread, and the UI thread waits only a
        // moment for the answer: a slow share gives the last known answer now and the new one at the next look.

        sealed class FileState { public bool Exists; public long Stamp; public DateTime At; public bool Busy; }
        static readonly Dictionary<string, FileState> Files = new Dictionary<string, FileState>(StringComparer.OrdinalIgnoreCase);

        static void StateOf(string f, out bool exists, out long stamp)
        {
            FileState s;
            bool start = false, first;
            lock (Files)
            {
                if (!Files.TryGetValue(f, out s)) { s = new FileState(); Files[f] = s; }
                first = s.At == default(DateTime);
                if (!s.Busy) { s.Busy = true; start = true; }
            }
            if (start)
            {
                var t = System.Threading.Tasks.Task.Run(() =>
                {
                    bool ex = false;
                    long st = 0;
                    try { ex = File.Exists(f); if (ex) st = File.GetLastWriteTimeUtc(f).Ticks; } catch { }
                    lock (Files) { s.Exists = ex; s.Stamp = st; s.At = DateTime.UtcNow; s.Busy = false; }
                });
                try { t.Wait(first ? 400 : 150); } catch { }
            }
            lock (Files) { exists = s.Exists; stamp = s.Stamp; }
        }

        static bool There(string f) { bool e; long s; StateOf(f, out e, out s); return e; }

        /// <summary>The picture Windows shows on this monitor and how it is fitted (vShell sets its per-monitor pictures through Windows too).</summary>
        static string PictureFor(Monitor m, out Position fit)
        {
            fit = Position.Fill;
            try
            {
                var dw = Wallpaper();
                try { fit = dw.GetPosition(); } catch { }
                uint n = dw.GetMonitorDevicePathCount();
                for (uint i = 0; i < n; i++)
                {
                    try
                    {
                        string id = dw.GetMonitorDevicePathAt(i);
                        RECT r = dw.GetMonitorRECT(id);
                        if (r.Left != m.Bounds.X || r.Top != m.Bounds.Y || r.Right - r.Left != m.Bounds.Width) continue;
                        string f = dw.GetWallpaper(id);
                        if (!string.IsNullOrEmpty(f) && There(f)) return f;
                    }
                    catch { }
                }
            }
            catch { _dw = null; }
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                {
                    string p = k == null ? null : k.GetValue("WallPaper") as string;
                    // no picture set = a plain-colour desktop (Windows keeps the old TranscodedWallpaper file then - it mustn't be used)
                    if (string.IsNullOrEmpty(p)) return null;
                    if (There(p)) return p;
                }
            }
            catch { }
            // a picture is set but its file has gone (moved / deleted): Windows' own copy of it
            string t = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
            return File.Exists(t) ? t : null;
        }

        /// <summary>What decides the pictures: each monitor, its picture (and when it was written), the fit. A change = make them again.</summary>
        static string Key()
        {
            var sb = new System.Text.StringBuilder();
            foreach (Monitor m in Monitors())
            {
                Position fit;
                string f = PictureFor(m, out fit);
                sb.Append(m.DeviceName).Append('@').Append(m.Bounds.X).Append(',').Append(m.Bounds.Y).Append(',').Append(m.Bounds.Width).Append('x').Append(m.Bounds.Height)
                  .Append('=').Append(f).Append(':').Append(fit);
                if (f != null) { bool there; long stamp; StateOf(f, out there, out stamp); sb.Append(':').Append(stamp); }
                sb.Append('|');
            }
            sb.Append("bg=").Append(BackgroundColour());
            return sb.ToString();
        }

        static Color BackgroundColour()
        {
            try
            {
                uint v = Wallpaper().GetBackgroundColor();     // COLORREF: 0x00BBGGRR
                return Color.FromRgb((byte)(v & 0xFF), (byte)((v >> 8) & 0xFF), (byte)((v >> 16) & 0xFF));
            }
            catch
            {
                _dw = null;
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Colors"))
                    {
                        string[] rgb = ((k == null ? null : k.GetValue("Background") as string) ?? "0 0 0").Split(' ');
                        return Color.FromRgb(byte.Parse(rgb[0]), byte.Parse(rgb[1]), byte.Parse(rgb[2]));
                    }
                }
                catch { return Colors.Black; }
            }
        }

        /// <summary>Make the pictures again if the wallpaper or monitors changed (a glass layer calls it; a timer too while any is showing).</summary>
        public static void Check()
        {
            string k;
            try { k = Key(); } catch { return; }
            if (k == _key) return;
            bool first = _key == null;
            _key = k;
            Cache.Clear();
            _colour = null;
            if (!first) { Action a = Changed; if (a != null) a(); }
        }

        static int _users;
        internal static void Use(bool on)
        {
            _users = Math.Max(0, _users + (on ? 1 : -1));
            if (_users > 0 && _timer == null)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                _timer.Tick += delegate { Check(); };
                _timer.Start();
            }
            else if (_users == 0 && _timer != null) { _timer.Stop(); _timer = null; }
        }

        /// <summary>The wallpaper of a monitor for "Blur" / "Acrylic" (Frosted) - blurred, 1/8 of its pixel size - or "Clear" (half size, not blurred); null when there's no picture.</summary>
        internal static BitmapSource For(Monitor m, string mode)
        {
            if (m == null) return null;
            if (_key == null) Check();
            string ck = m.DeviceName + "|" + m.Bounds + "|" + mode;
            BitmapSource b;
            if (Cache.TryGetValue(ck, out b)) return b;
            b = Make(m, mode);
            Cache[ck] = b;
            return b;
        }

        static BitmapSource Make(Monitor m, string mode)
        {
            try
            {
                int mw = m.Bounds.Width, mh = m.Bounds.Height;
                if (mw <= 0 || mh <= 0) return null;
                double shrink = ShrinkFor(mode);
                int sw = Math.Max(8, (int)Math.Round(mw * shrink)), sh = Math.Max(8, (int)Math.Round(mh * shrink));
                double fx = (double)sw / mw, fy = (double)sh / mh;

                // 1. the wallpaper as Windows shows it on this monitor, small
                var dv = new DrawingVisual();
                bool any;
                using (DrawingContext dc = dv.RenderOpen())
                {
                    dc.PushTransform(new ScaleTransform(fx, fy));
                    dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, mw, mh));
                    any = DrawWallpaper(dc, m, shrink);
                    dc.Pop();
                }
                if (!any) return null;
                var small = new RenderTargetBitmap(sw, sh, 96, 96, PixelFormats.Pbgra32);
                small.Render(dv);
                small.Freeze();
                if (mode == "Clear") return small;   // Blur and Frosted off: the wallpaper as it is, like the bars over the desktop

                // 2. blurred (drawn a little larger so the blur doesn't fade to transparent at the edges)
                bool frosted = mode == "Acrylic";
                double radius = frosted ? 9 : 6;
                int pad = (int)Math.Ceiling(radius);
                var img = new Image { Source = small, Stretch = Stretch.Fill, Width = sw + 2 * pad, Height = sh + 2 * pad };
                img.Effect = new BlurEffect { Radius = radius, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality };
                var canvas = new Canvas { Width = sw, Height = sh, ClipToBounds = true, Background = Brushes.Black };
                Canvas.SetLeft(img, -pad);
                Canvas.SetTop(img, -pad);
                canvas.Children.Add(img);
                // Frosted: a little lighter and less saturated (the grain is added per window, at full resolution)
                if (frosted) canvas.Children.Add(new Border { Width = sw, Height = sh, Background = new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF)) });
                canvas.Measure(new Size(sw, sh));
                canvas.Arrange(new Rect(0, 0, sw, sh));
                canvas.UpdateLayout();
                var blurred = new RenderTargetBitmap(sw, sh, 96, 96, PixelFormats.Pbgra32);
                blurred.Render(canvas);
                blurred.Freeze();
                return blurred;
            }
            catch (Exception ex) { Log.Write("Blur picture: " + ex.Message); return null; }
        }

        /// <summary>Draw the monitor's wallpaper (in monitor pixels, 0,0 = its top-left). false = no picture.</summary>
        static bool DrawWallpaper(DrawingContext dc, Monitor m, double shrink)
        {
            Position fit;
            string path = PictureFor(m, out fit);
            if (path == null) return false;
            var mr = new Rect(0, 0, m.Bounds.Width, m.Bounds.Height);
            if (fit == Position.Span)
            {
                // one picture over all monitors together
                int l = int.MaxValue, t = int.MaxValue, r = int.MinValue, b = int.MinValue;
                foreach (Monitor x in Monitors())
                {
                    l = Math.Min(l, x.Bounds.X); t = Math.Min(t, x.Bounds.Y);
                    r = Math.Max(r, x.Bounds.X + x.Bounds.Width); b = Math.Max(b, x.Bounds.Y + x.Bounds.Height);
                }
                if (l == int.MaxValue) return false;
                BitmapImage all = Load(path, SmallWidth(r - l, shrink));
                if (all == null) return false;
                dc.PushClip(new RectangleGeometry(mr));
                Draw(dc, all, new Rect(l - m.Bounds.X, t - m.Bounds.Y, r - l, b - t), Position.Fill);
                dc.Pop();
                return true;
            }
            // the picture is drawn at 1/8 size and blurred, so a small copy is plenty - except Center / Tile (drawn at their own size)
            BitmapImage img = Load(path, fit == Position.Tile || fit == Position.Center ? 0 : SmallWidth(m.Bounds.Width, shrink));
            if (img == null) return false;
            Draw(dc, img, mr, fit);
            return true;
        }

        static int SmallWidth(int width, double shrink) { return Math.Max(64, (int)Math.Ceiling(width * shrink * 2)); }

        static BitmapImage Load(string path, int decodeWidth)
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bi.UriSource = new Uri(Path.GetFullPath(path));
                if (decodeWidth > 0) bi.DecodePixelWidth = decodeWidth;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch (Exception ex) { Log.Write("Wallpaper image '" + path + "': " + ex.Message); return null; }
        }

        static void Draw(DrawingContext dc, BitmapImage img, Rect r, Position fit)
        {
            double iw = img.PixelWidth, ih = img.PixelHeight;
            if (iw <= 0 || ih <= 0) return;
            dc.PushClip(new RectangleGeometry(r));
            switch (fit)
            {
                case Position.Stretch:
                    dc.DrawImage(img, r);
                    break;
                case Position.Center:
                    dc.DrawImage(img, new Rect(r.Left + (r.Width - iw) / 2, r.Top + (r.Height - ih) / 2, iw, ih));
                    break;
                case Position.Tile:
                    for (double y = r.Top; y < r.Bottom; y += Math.Max(4, ih))
                        for (double x = r.Left; x < r.Right; x += Math.Max(4, iw))
                            dc.DrawImage(img, new Rect(x, y, iw, ih));
                    break;
                default:    // Fill (cover) or Fit (contain)
                    double sx = r.Width / iw, sy = r.Height / ih;
                    double sc = fit == Position.Fit ? Math.Min(sx, sy) : Math.Max(sx, sy);
                    double dw = iw * sc, dh = ih * sc;
                    dc.DrawImage(img, new Rect(r.Left + (r.Width - dw) / 2, r.Top + (r.Height - dh) / 2, dw, dh));
                    break;
            }
            dc.Pop();
        }

        static Brush _colour;
        /// <summary>The desktop's plain background colour (what shows when there's no wallpaper picture) - kept until the desktop changes.</summary>
        public static Brush DesktopColour()
        {
            if (_colour == null)
            {
                var b = new SolidColorBrush(BackgroundColour());
                b.Freeze();
                _colour = b;
            }
            return _colour;
        }
        /// <summary>A fine grain for Frosted (like Windows' acrylic), tiled at full resolution.</summary>
        public static Brush Noise()
        {
            if (_noise != null) return _noise;
            const int n = 96;
            var px = new byte[n * n * 4];
            var rnd = new Random(11);
            for (int i = 0; i < n * n; i++)
            {
                bool light = rnd.Next(2) == 0;
                byte a = (byte)rnd.Next(0, 11);
                byte c = light ? (byte)255 : (byte)0;
                byte pc = (byte)(c * a / 255);   // premultiplied
                px[i * 4] = pc; px[i * 4 + 1] = pc; px[i * 4 + 2] = pc; px[i * 4 + 3] = a;
            }
            var bmp = BitmapSource.Create(n, n, 96, 96, PixelFormats.Pbgra32, null, px, n * 4);
            bmp.Freeze();
            _noise = new ImageBrush(bmp) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, n, n), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
            _noise.Freeze();
            return _noise;
        }
    }

    /// <summary>
    /// The wallpaper behind an element (a window's content, vShell's bars, widgets and popups): it shows the part of the monitor's
    /// wallpaper - blurred for "Blur" / "Acrylic", as it is for "Clear" - that lies behind it, following the window as it moves. The
    /// element's own background (the theme colour at the theme's opacity) goes over it. <see cref="Under"/> puts one under a window's
    /// content. vShell derives its bars' / widgets' glass from it (their own blur setting).
    /// </summary>
    public class Glass : Border
    {
        readonly Border _grain = new Border();
        Window _win;
        bool _hooked;
        string _mode = "Off";

        public Glass()
        {
            IsHitTestVisible = false;
            Focusable = false;
            Child = _grain;
            Visibility = Visibility.Collapsed;
            SizeChanged += delegate { Update(); };
            Loaded += delegate { Hook(); Update(); };
            Unloaded += delegate { Unhook(); };
        }

        /// <summary>
        /// Put a glass layer under the window's content (wrapping it in a grid) and return it; call <see cref="Refresh"/> with the theme's
        /// blur mode whenever the theme changes. The content keeps its own background (vx.Bg) over the glass.
        /// </summary>
        public static Glass Under(Window w)
        {
            var content = w.Content as UIElement;
            var host = new Grid();
            var glass = new Glass();
            w.Content = host;
            host.Children.Add(glass);
            if (content != null) host.Children.Add(content);
            return glass;
        }

        public void SetCorners(CornerRadius cr) { CornerRadius = cr; _grain.CornerRadius = cr; }

        /// <summary>The theme changed: "Blur", "Acrylic" (Frosted), "Clear" (the wallpaper not blurred - Blur and Frosted off) or "Off" (no layer).</summary>
        public void Refresh(string mode)
        {
            bool was = _mode != "Off";
            _mode = string.IsNullOrEmpty(mode) ? "Off" : mode;
            bool on = _mode != "Off";
            Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            _grain.Background = on && _mode == "Acrylic" ? Backdrop.Noise() : null;
            if (on != was && _hooked) Backdrop.Use(on);
            if (on) Backdrop.Check();
            Update();
        }

        void Hook()
        {
            if (_hooked) return;
            _win = Window.GetWindow(this);
            if (_win == null) return;
            _hooked = true;
            _win.LocationChanged += OnMoved;
            _win.Closed += OnClosed;
            Backdrop.Changed += OnChanged;
            if (_mode != "Off") Backdrop.Use(true);
        }

        void Unhook()
        {
            if (!_hooked) return;
            _hooked = false;
            if (_win != null) { _win.LocationChanged -= OnMoved; _win.Closed -= OnClosed; }
            Backdrop.Changed -= OnChanged;
            if (_mode != "Off") Backdrop.Use(false);
        }

        void OnMoved(object s, EventArgs e) { Update(); }
        void OnClosed(object s, EventArgs e) { Unhook(); }
        void OnChanged() { Update(); }

        /// <summary>Show the part of the blurred wallpaper that is behind this element now.</summary>
        public void Update()
        {
            if (Visibility != Visibility.Visible || ActualWidth < 1 || ActualHeight < 1) return;
            try
            {
                if (PresentationSource.FromVisual(this) == null) return;
                Point tl = PointToScreen(new Point(0, 0));                       // device pixels
                Point br = PointToScreen(new Point(ActualWidth, ActualHeight));
                double x = Math.Min(tl.X, br.X), y = Math.Min(tl.Y, br.Y);
                double w = Math.Abs(br.X - tl.X), h = Math.Abs(br.Y - tl.Y);
                Backdrop.Monitor m = Backdrop.MonitorAt((int)(x + w / 2), (int)(y + h / 2));
                if (m == null) return;
                BitmapSource pic = Backdrop.For(m, _mode);
                if (pic == null) { Background = Backdrop.DesktopColour(); return; }   // a plain-colour desktop: that colour
                double fx = pic.PixelWidth / (double)Math.Max(1, m.Bounds.Width), fy = pic.PixelHeight / (double)Math.Max(1, m.Bounds.Height);
                var view = new Rect((x - m.Bounds.X) * fx, (y - m.Bounds.Y) * fy, Math.Max(1, w * fx), Math.Max(1, h * fy));
                // keep inside the picture (a window can reach past the monitor's edge)
                if (view.Width > pic.PixelWidth) view.Width = pic.PixelWidth;
                if (view.Height > pic.PixelHeight) view.Height = pic.PixelHeight;
                if (view.X < 0) view.X = 0;
                if (view.Y < 0) view.Y = 0;
                if (view.Right > pic.PixelWidth) view.X = Math.Max(0, pic.PixelWidth - view.Width);
                if (view.Bottom > pic.PixelHeight) view.Y = Math.Max(0, pic.PixelHeight - view.Height);
                var brush = Background as ImageBrush;
                if (brush == null || brush.ImageSource != pic || brush.IsFrozen)
                {
                    brush = new ImageBrush(pic) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute };
                    RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
                    Background = brush;
                }
                if (brush.Viewbox != view) brush.Viewbox = view;
            }
            catch (Exception ex) { Log.Write("Glass: " + ex.Message); }
        }
    }
}
