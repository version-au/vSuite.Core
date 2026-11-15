using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using vSuite;

namespace vSuite.Ui
{
    /// <summary>DWM look of the window: dark mode, corners, outline colour, the Blur / Frosted backdrop and dark native menus.</summary>
    public static class WindowEffects
    {
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_BORDER_COLOR = 34, DWMWA_CAPTION_COLOR = 35, DWMWA_SYSTEMBACKDROP_TYPE = 38;
        const int DWMWCP_DONOTROUND = 1, DWMWCP_ROUND = 2, DWMWCP_ROUNDSMALL = 3;
        const int DWMSBT_NONE = 1, DWMSBT_MAINWINDOW = 2, DWMSBT_TRANSIENTWINDOW = 3, DWMSBT_TABBEDWINDOW = 4;
        const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("uxtheme.dll", EntryPoint = "#135")]
        static extern int SetPreferredAppMode(int mode);

        [DllImport("uxtheme.dll", EntryPoint = "#136")]
        static extern void FlushMenuThemes();

        [DllImport("user32.dll")]
        static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        static extern int GetSystemMetricsForDpi(int index, uint dpi);

        /// <summary>System backdrops (Mica / Acrylic) need Windows 11 22H2.</summary>
        public static bool BackdropSupported { get { return Environment.OSVersion.Version.Build >= 22621; } }

        static readonly DependencyProperty GlassProperty = DependencyProperty.RegisterAttached("SuiteGlass", typeof(Glass), typeof(WindowEffects));

        /// <summary>
        /// The suite's look on a window, on every theme change (after its content is set - SourceInitialized is a good place): the Windows
        /// side (dark mode, corners, outline, title colour - Apply) and a glass layer under the content, as vShell's bars: the wallpaper -
        /// blurred for Blur / Frosted, as it is when they're off - with the content's vx.Bg (the theme colour at the theme's opacity) over
        /// it. seeThrough = false: no layer, solid (an app's "Use the theme's opacity" off) - pass the same to Theme.Apply.
        /// </summary>
        public static void Look(Window w, Theme t, bool seeThrough)
        {
            if (w == null || t == null) return;
            IntPtr hwnd = new WindowInteropHelper(w).Handle;
            System.Windows.Shell.WindowChrome chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(w);
            if (chrome != null && chrome.GlassFrameThickness != new Thickness(0)) chrome.GlassFrameThickness = new Thickness(0);
            Apply(hwnd, t, seeThrough);
            SetSystemMenuStyle(hwnd, true);
            var glass = w.GetValue(GlassProperty) as Glass;
            if (glass == null && seeThrough && w.Content != null)
            {
                glass = Glass.Under(w);
                w.SetValue(GlassProperty, glass);
            }
            if (glass != null) glass.Refresh(!seeThrough ? "Off" : t.Blur == "Blur" || t.Blur == "Acrylic" ? t.Blur : "Clear");
        }

        /// <summary>The Windows side of the look (dark mode, corners, outline, title colour, no Windows backdrop). Windows use Look.</summary>
        public static void Apply(IntPtr hwnd, Theme t, bool blur)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                Set(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, t.IsDark ? 1 : 0);
                Set(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, t.Radius <= 0 ? DWMWCP_DONOTROUND : t.Radius <= 4 ? DWMWCP_ROUNDSMALL : DWMWCP_ROUND);
                Color b = t.WindowBorder;
                Set(hwnd, DWMWA_BORDER_COLOR, t.BorderThickness > 0 ? b.R | (b.G << 8) | (b.B << 16) : DWMWA_COLOR_NONE);
                // the title area: with "Show accent colour on title bars" Windows paints its accent colour there, and a see-through theme
                // showed it (a blue strip over a red theme) - paint the theme's own colour instead
                Color cap = t.CaptionColor;
                Set(hwnd, DWMWA_CAPTION_COLOR, cap.R | (cap.G << 8) | (cap.B << 16));
                // vShell's Blur and Frosted are drawn by the window itself now (Glass: the blurred wallpaper under the theme colour at the
                // theme's opacity, as vShell's bars - see Look) - no Windows backdrop: Mica was nearly solid, so the opacity made no
                // difference, and Windows' Acrylic shows the windows behind (a blue app turned a red theme's see-through top blue).
                if (BackdropSupported) Set(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_NONE);
                HwndSource src = HwndSource.FromHwnd(hwnd);
                if (src != null && src.CompositionTarget != null)
                    src.CompositionTarget.BackgroundColor = Color.FromRgb(t.Bg.R, t.Bg.G, t.Bg.B);
            }
            catch (Exception ex) { Log.Write("Window effects: " + ex.Message); }

            // native menus (the shell's right-click menu) in dark or light to match - undocumented, but stable since 1809
            try { SetPreferredAppMode(t.IsDark ? 2 : 3); FlushMenuThemes(); }
            catch { }
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);

        /// <summary>
        /// With Blur / Frosted the whole window is glass, and DWM draws Windows' own minimise / maximise / close buttons into it - they show
        /// through vShell's see-through background as faint "ghost" buttons behind ours. DWM only draws them for a window with a system
        /// menu (WS_SYSMENU), so the style comes off while the glass is on (Alt+Space and right-clicking the title bar still open the
        /// window menu - MainWindow shows it itself).
        /// </summary>
        public static void SetSystemMenuStyle(IntPtr hwnd, bool on)
        {
            if (hwnd == IntPtr.Zero) return;
            const int GWL_STYLE = -16;
            const long WS_SYSMENU = 0x80000;
            const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20;
            long style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
            long want = on ? style | WS_SYSMENU : style & ~WS_SYSMENU;
            if (want == style) return;
            SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(want));
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }

        /// <summary>How far a maximised borderless window hangs off the screen (DIPs) - pad the content by this much.</summary>
        public static double MaximizedOverhang(IntPtr hwnd)
        {
            try
            {
                uint dpi = GetDpiForWindow(hwnd);
                if (dpi == 0) dpi = 96;
                int px = GetSystemMetricsForDpi(32 /* SM_CXSIZEFRAME */, dpi) + GetSystemMetricsForDpi(92 /* SM_CXPADDEDBORDER */, dpi);
                return px * 96.0 / dpi;
            }
            catch { return 8; }
        }

        static void Set(IntPtr hwnd, int attr, int value) { DwmSetWindowAttribute(hwnd, attr, ref value, sizeof(int)); }
    }
}
