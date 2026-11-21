using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace vSuite.Ui
{
    /// <summary>
    /// Windows 11's snap layouts on a window's own maximise button. A window that draws its own title bar (WindowChrome) only gets the
    /// layouts menu when Windows is told the pointer is over "the maximise button" (WM_NCHITTEST = HTMAXBUTTON). Windows then keeps the
    /// mouse there, so the button's hover / pressed looks and its click are done here.
    /// Only while snap layouts can show (Windows 11, Snap windows and "Show snap layouts when I hover over a window's maximise button"
    /// on - read again at most every few seconds): otherwise nothing changes and the button is the plain WPF button it always was.
    /// </summary>
    public static class SnapLayouts
    {
        const int WM_NCHITTEST = 0x0084, WM_NCMOUSEMOVE = 0x00A0, WM_NCLBUTTONDOWN = 0x00A1, WM_NCLBUTTONUP = 0x00A2, WM_NCLBUTTONDBLCLK = 0x00A3,
                  WM_NCMOUSELEAVE = 0x02A2, HTMAXBUTTON = 9;

        static readonly ConditionalWeakTable<Window, Hook> Hooks = new ConditionalWeakTable<Window, Hook>();

        /// <summary>
        /// Give the window's maximise button the layouts menu. The button keeps its own Click (run here when Windows has the mouse).
        /// Call once, any time (before or after the window shows).
        /// </summary>
        public static void Attach(Window window, ButtonBase maximise)
        {
            if (maximise != null) Attach(window, () => maximise);
        }

        /// <summary>
        /// The same for a maximise "button" that isn't a WPF Button, or is made again now and then (vTerm's strip): `maximise` gives the
        /// one shown now (null = none); `click` maximises / restores (null = the Button's own Click); `look(element, hot, pressed)` draws
        /// it hovered / pressed / plain (null = vx.Hover / vx.Active as its Background, a Control's).
        /// </summary>
        public static void Attach(Window window, Func<FrameworkElement> maximise, Action click = null, Action<FrameworkElement, bool, bool> look = null)
        {
            if (window == null || maximise == null || Hooks.TryGetValue(window, out _)) return;
            var h = new Hook(window, maximise, click, look);
            Hooks.Add(window, h);
            // after WindowChrome's own hook (added when the window's handle is made): HwndSource asks the newest hook first, and
            // WindowChrome answers every hit test itself
            if (window.IsLoaded) h.Start();
            else window.Loaded += delegate { h.Start(); };
        }

        /// <summary>Can snap layouts show? Tests set it; null = Windows' own settings.</summary>
        public static Func<bool> IsOnOverride { get; set; }

        static bool _on, _read;
        static long _checked;

        /// <summary>Windows 11 (build 22000 on) with Snap windows and the hover menu on - Windows' defaults when the values aren't there.</summary>
        public static bool IsOn
        {
            get
            {
                if (IsOnOverride != null) return IsOnOverride();
                long now = Environment.TickCount64;
                if (!ReadAgain(_read, _checked, now)) return _on;
                _checked = now;
                _read = true;
                _on = ReadSetting();
                return _on;
            }
        }

        /// <summary>Read Windows' setting again: the first time, then at most every 3 s. (1.0.44 started from long.MinValue - the
        /// subtraction overflowed, so the setting was never read and the menu never came.)</summary>
        public static bool ReadAgain(bool readBefore, long lastRead, long now) { return !readBefore || now - lastRead >= 3000 || now < lastRead; }

        /// <summary>Windows' own settings, read now (IsOn keeps the answer a few seconds).</summary>
        public static bool ReadSetting()
        {
            try
            {
                if (Environment.OSVersion.Version.Major < 10 || Environment.OSVersion.Version.Build < 22000) return false;
                using (var desk = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                    if (desk?.GetValue("WindowArrangementActive") is object v && v.ToString().Trim() == "0") return false;
                using (var adv = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"))
                    if (adv?.GetValue("EnableSnapAssistFlyout") is object v && v.ToString().Trim() == "0") return false;
                return true;
            }
            catch { return false; }
        }

        /// <summary>Is a screen point (device pixels) on the element? False when it isn't shown.</summary>
        public static bool Over(FrameworkElement e, int x, int y)
        {
            if (e == null || !e.IsVisible || e.ActualWidth <= 0 || PresentationSource.FromVisual(e) == null) return false;
            Point a = e.PointToScreen(new Point(0, 0)), b = e.PointToScreen(new Point(e.ActualWidth, e.ActualHeight));
            return x >= Math.Min(a.X, b.X) && x < Math.Max(a.X, b.X) && y >= Math.Min(a.Y, b.Y) && y < Math.Max(a.Y, b.Y);
        }

        sealed class Hook
        {
            readonly Window _w;
            readonly Func<FrameworkElement> _find;
            readonly Action _click;
            readonly Action<FrameworkElement, bool, bool> _look;
            HwndSourceHook _proc;      // kept here: HwndSource holds its hooks weakly
            FrameworkElement _hot;     // the button the pointer is on (Windows has the mouse), or null
            bool _pressed;

            public Hook(Window w, Func<FrameworkElement> find, Action click, Action<FrameworkElement, bool, bool> look)
            {
                _w = w;
                _find = find;
                _click = click;
                _look = look ?? DefaultLook;
            }

            public void Start()
            {
                if (_proc != null) return;
                var src = PresentationSource.FromVisual(_w) as HwndSource;
                if (src == null) return;
                _proc = WndProc;
                src.AddHook(_proc);
                _w.Closed += delegate { try { src.RemoveHook(_proc); } catch { } };
            }

            IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
            {
                switch (msg)
                {
                    case WM_NCHITTEST:
                        FrameworkElement b = IsOn ? _find() : null;
                        if (b != null && b.IsEnabled && Over(b, (short)((long)lParam & 0xFFFF), (short)(((long)lParam >> 16) & 0xFFFF)))
                        {
                            if (_hot != b)
                            {
                                // a button made again while the pointer was on it (vTerm's strip): the old one plain, the new one hovered
                                if (_hot != null) Draw(_hot, false, false);
                                else Track(hwnd);
                                _hot = b;
                                _pressed = false;
                                Draw(b, true, false);
                            }
                            handled = true;
                            return new IntPtr(HTMAXBUTTON);
                        }
                        if (_hot != null) Leave();
                        break;
                    case WM_NCMOUSEMOVE:
                        if (_hot != null && wParam.ToInt64() != HTMAXBUTTON) Leave();
                        break;
                    case WM_NCMOUSELEAVE:
                        if (_hot != null) Leave();
                        break;
                    case WM_NCLBUTTONDOWN:
                    case WM_NCLBUTTONDBLCLK:
                        // never to Windows: it would draw its own old-style button over ours while held
                        if (wParam.ToInt64() == HTMAXBUTTON && _hot != null) { _pressed = true; Draw(_hot, true, true); handled = true; return IntPtr.Zero; }
                        break;
                    case WM_NCLBUTTONUP:
                        if (wParam.ToInt64() == HTMAXBUTTON && _hot != null)
                        {
                            bool click = _pressed;
                            var on = _hot;
                            _pressed = false;
                            Draw(on, true, false);
                            handled = true;
                            if (click) on.Dispatcher.BeginInvoke(new Action(() => Click(on)));
                            return IntPtr.Zero;
                        }
                        break;
                }
                return IntPtr.Zero;
            }

            void Click(FrameworkElement on)
            {
                if (_click != null) _click();
                else if (on is ButtonBase bb) bb.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, bb));
            }

            void Leave()
            {
                var was = _hot;
                _hot = null;
                _pressed = false;
                Draw(was, false, false);
            }

            void Draw(FrameworkElement e, bool hot, bool pressed)
            {
                try { _look(e, hot, pressed); } catch { }
            }

            /// <summary>Hover / pressed as vSuite's button styles show them (vx.Hover / vx.Active); plain = back to the style.</summary>
            static void DefaultLook(FrameworkElement e, bool hot, bool pressed)
            {
                if (!(e is Control c)) return;
                if (!hot) { c.ClearValue(Control.BackgroundProperty); return; }
                var brush = c.TryFindResource(pressed ? "vx.Active" : "vx.Hover") as Brush
                            ?? new SolidColorBrush(Color.FromArgb(pressed ? (byte)0x33 : (byte)0x1F, 0x80, 0x80, 0x80));
                c.SetValue(Control.BackgroundProperty, brush);
            }

            /// <summary>Ask Windows for WM_NCMOUSELEAVE when the pointer leaves the button's spot.</summary>
            static void Track(IntPtr hwnd)
            {
                var t = new TRACKMOUSEEVENT { cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(), dwFlags = TME_LEAVE | TME_NONCLIENT, hwndTrack = hwnd };
                TrackMouseEvent(ref t);
            }
        }

        const uint TME_LEAVE = 0x2, TME_NONCLIENT = 0x10;

        [StructLayout(LayoutKind.Sequential)]
        struct TRACKMOUSEEVENT { public uint cbSize; public uint dwFlags; public IntPtr hwndTrack; public uint dwHoverTime; }

        [DllImport("user32.dll")]
        static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT e);
    }
}
