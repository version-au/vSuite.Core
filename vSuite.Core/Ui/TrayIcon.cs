using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace vSuite.Ui
{
    /// <summary>
    /// An app's icon in the notification area (vTrace's Minimise to the tray, vShell's own icon). Click = Clicked; right-click = the
    /// app's menu (Menu builds it each time), dressed in the suite's look with MenuTheme's colours, at the pointer. The menu closes on a
    /// click anywhere else (another program, the desktop, the taskbar), on Esc, or when another program comes in front - Windows doesn't
    /// tell a menu opened from the tray that it lost the focus. Its own hidden window gets the icon's messages, and puts the icon back
    /// after Explorer restarts. Needs no Windows Forms.
    /// </summary>
    public sealed class TrayIcon : IDisposable
    {
        const int CallbackMessage = 0x8000 + 0x5A;      // WM_APP + 90
        const int WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205;
        static readonly int TaskbarCreated = RegisterWindowMessage("TaskbarCreated");

        readonly HwndSource _window;
        IntPtr _icon, _ownIcon;
        bool _shown;
        string _tip;
        ContextMenu _menu;
        DispatcherTimer _watch;
        bool _wasDown, _escDown;
        static readonly uint Me = (uint)Environment.ProcessId;

        /// <summary>A left click on the icon.</summary>
        public event Action Clicked;
        /// <summary>Builds the right-click menu (each time it opens). null = no menu.</summary>
        public Func<ContextMenu> Menu;
        /// <summary>The colours for the menu (the suite's look on it). null = the app's own resources style it (vTrace's app-wide styles).</summary>
        public Func<Theme> MenuTheme;

        public TrayIcon(string tip)
        {
            _tip = Clip(tip);
            // a hidden pop-up window (never shown) - a message-only window wouldn't hear TaskbarCreated
            _window = new HwndSource(new HwndSourceParameters("vSuite tray icon") { WindowStyle = unchecked((int)0x80000000), Width = 0, Height = 0 });
            _window.AddHook(Hook);
        }

        public bool Shown { get { return _shown; } }

        /// <summary>The menu while it's open (tests), else null.</summary>
        public ContextMenu OpenMenu { get { return _menu != null && _menu.IsOpen ? _menu : null; } }

        public void Show()
        {
            if (_shown) return;
            if (_icon == IntPtr.Zero) _icon = ExeIcon();
            NOTIFYICONDATA d = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP);
            _shown = Shell_NotifyIcon(NIM_ADD, ref d);
            if (!_shown) Log.Write("Tray icon: Windows didn't take it");
        }

        public void Hide()
        {
            CloseMenu();
            if (!_shown) return;
            NOTIFYICONDATA d = Data(0);
            Shell_NotifyIcon(NIM_DELETE, ref d);
            _shown = false;
        }

        /// <summary>The text when the pointer rests on it (up to 127 characters).</summary>
        public string Tip
        {
            get { return _tip; }
            set
            {
                string v = Clip(value);
                if (v == _tip) return;
                _tip = v;
                Modify(NIF_TIP);
            }
        }

        /// <summary>A picture for the icon (drawn at the tray's size) - e.g. a logo in the theme's colours. null = the exe's icon.</summary>
        public void SetIcon(BitmapSource picture)
        {
            IntPtr h = picture != null ? ToIcon(picture) : IntPtr.Zero;
            IntPtr old = _ownIcon;
            _ownIcon = h;
            _icon = h != IntPtr.Zero ? h : ExeIcon();
            Modify(NIF_ICON);
            if (old != IntPtr.Zero) DestroyIcon(old);
        }

        /// <summary>The icon's size in the tray, in pixels (small icon size, at the main monitor's DPI).</summary>
        public static int Size { get { return Math.Max(16, GetSystemMetrics(49 /* SM_CXSMICON */)); } }

        /// <summary>Open the menu at the pointer (a right-click does this).</summary>
        public void ShowMenu()
        {
            CloseMenu();
            if (Menu == null) return;
            ContextMenu m;
            try { m = Menu(); } catch (Exception ex) { Log.Write("Tray menu: " + ex.Message); return; }
            if (m == null) return;
            Theme t = null;
            try { t = MenuTheme != null ? MenuTheme() : null; } catch { }
            if (t != null)
            {
                // the suite's menu style and the theme's vx.* brushes on the menu itself (it has no window to take them from)
                m.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = Look.ControlsUri });
                t.Apply(m.Resources, false);
            }
            m.Placement = PlacementMode.MousePoint;
            m.Closed += delegate { if (_menu == m) { _menu = null; StopWatch(); } };
            _menu = m;
            m.IsOpen = true;
            _wasDown = AnyButton();
            _escDown = Down(0x1B);
            if (_watch == null)
            {
                _watch = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(50) };
                _watch.Tick += delegate { Watch(); };
            }
            _watch.Start();
        }

        public void CloseMenu()
        {
            ContextMenu m = _menu;
            _menu = null;
            StopWatch();
            if (m != null) try { m.IsOpen = false; } catch { }
        }

        void StopWatch() { if (_watch != null) _watch.Stop(); }

        /// <summary>While the menu is open: a press outside it, Esc, or another program in front closes it.</summary>
        void Watch()
        {
            if (_menu == null || !_menu.IsOpen) { _menu = null; StopWatch(); return; }
            bool close = false;
            bool down = AnyButton();
            if (down && !_wasDown)
            {
                POINT p;
                GetCursorPos(out p);
                IntPtr h = WindowFromPoint(p);
                if (!IsMenuWindow(GetAncestor(h, 2 /* GA_ROOT */))) close = true;
            }
            _wasDown = down;
            bool esc = Down(0x1B);
            if (esc && !_escDown) close = true;
            _escDown = esc;
            IntPtr fg = GetForegroundWindow();
            if (fg != IntPtr.Zero)
            {
                uint pid;
                GetWindowThreadProcessId(fg, out pid);
                if (pid != Me && _fgOther != fg && _fgOther != IntPtr.Zero) close = true;
                _fgOther = fg;
            }
            if (close) CloseMenu();
        }
        IntPtr _fgOther;

        /// <summary>One of this app's menu windows (the menu or a sub-menu)?</summary>
        static bool IsMenuWindow(IntPtr root)
        {
            if (root == IntPtr.Zero) return false;
            uint pid;
            GetWindowThreadProcessId(root, out pid);
            if (pid != Me) return false;
            var src = HwndSource.FromHwnd(root);
            return src != null && src.RootVisual != null && HasMenu(src.RootVisual, 0);
        }

        static bool HasMenu(DependencyObject d, int depth)
        {
            if (d is ContextMenu || d is MenuItem) return true;
            if (depth > 12) return false;
            int n = VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < n; i++) if (HasMenu(VisualTreeHelper.GetChild(d, i), depth + 1)) return true;
            return false;
        }

        static bool Down(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }
        static bool AnyButton() { return Down(0x01) || Down(0x02) || Down(0x04) || Down(0x05) || Down(0x06); }

        IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == TaskbarCreated && TaskbarCreated != 0)
            {
                if (_shown) { _shown = false; Show(); }       // Explorer restarted: add it again
                return IntPtr.Zero;
            }
            if (msg != CallbackMessage) return IntPtr.Zero;
            handled = true;
            int what = (int)((long)lParam & 0xFFFF);          // the older notifications: just the mouse message (one per click)
            if (what == WM_LBUTTONUP) { if (Clicked != null) Clicked(); }
            else if (what == WM_RBUTTONUP) ShowMenu();
            return IntPtr.Zero;
        }

        void Modify(int flags)
        {
            if (!_shown) return;
            NOTIFYICONDATA d = Data(flags);
            Shell_NotifyIcon(NIM_MODIFY, ref d);
        }

        NOTIFYICONDATA Data(int flags)
        {
            return new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(), hWnd = _window.Handle, uID = 1, uFlags = flags,
                uCallbackMessage = CallbackMessage, hIcon = _icon, szTip = _tip, szInfo = "", szInfoTitle = ""
            };
        }

        static string Clip(string s) { s = string.IsNullOrEmpty(s) ? App.Name : s; return s.Length > 127 ? s.Substring(0, 127) : s; }

        IntPtr _exeIcon;
        IntPtr ExeIcon()
        {
            if (_exeIcon == IntPtr.Zero)
            {
                IntPtr[] small = new IntPtr[1];
                if (ExtractIconEx(Environment.ProcessPath ?? "", 0, null, small, 1) > 0) _exeIcon = small[0];
            }
            return _exeIcon;
        }

        /// <summary>A picture as a Windows icon at the tray's size (straight alpha, as icons want it).</summary>
        static IntPtr ToIcon(BitmapSource picture)
        {
            try
            {
                int px = Size;
                BitmapSource src = picture;
                if (src.PixelWidth != px || src.PixelHeight != px)
                    src = new TransformedBitmap(src, new ScaleTransform((double)px / src.PixelWidth, (double)px / src.PixelHeight));
                var bgra = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
                var pixels = new byte[px * px * 4];
                bgra.CopyPixels(pixels, px * 4, 0);
                IntPtr color = CreateBitmap(px, px, 1, 32, pixels);
                IntPtr mask = CreateBitmap(px, px, 1, 1, new byte[((px + 15) / 16) * 2 * px]);
                var info = new ICONINFO { fIcon = true, hbmMask = mask, hbmColor = color };
                IntPtr h = CreateIconIndirect(ref info);
                DeleteObject(color);
                DeleteObject(mask);
                return h;
            }
            catch (Exception ex) { Log.Write("Tray icon picture: " + ex.Message); return IntPtr.Zero; }
        }

        public void Dispose()
        {
            Hide();
            try { _window.RemoveHook(Hook); _window.Dispose(); } catch { }
            if (_ownIcon != IntPtr.Zero) { DestroyIcon(_ownIcon); _ownIcon = IntPtr.Zero; }
            if (_exeIcon != IntPtr.Zero) { DestroyIcon(_exeIcon); _exeIcon = IntPtr.Zero; }
            _icon = IntPtr.Zero;
        }

        const int NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID, uFlags, uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public int dwState, dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public int uVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ICONINFO { public bool fIcon; public int xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIcon(int msg, ref NOTIFYICONDATA data);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, int count);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
        [DllImport("user32.dll")] static extern IntPtr CreateIconIndirect(ref ICONINFO info);
        [DllImport("gdi32.dll")] static extern IntPtr CreateBitmap(int w, int h, uint planes, uint bits, byte[] data);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int RegisterWindowMessage(string name);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    }
}
