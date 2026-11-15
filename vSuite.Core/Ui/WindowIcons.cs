using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace vSuite.Ui
{
    /// <summary>
    /// A main window's icons in the theme's colours:
    ///   - Window.Icon (WPF) plus the big and small icons each drawn at its own size with WM_SETICON: WPF gives Windows only the 16 px
    ///     frame, which vShell's taskbar stretched for its big buttons and pins (blurry). The big one is 64 px by the DPI.
    ///   - the "vShell.LiveIcon" property, a counter bumped with every new icon: vShell's taskbar then uses the window's own icon on
    ///     combined and pinned buttons, and notices each change (Windows can reuse an icon handle's number). It starts from the time, so
    ///     vShell (which keeps a pin's last live icon by this number) takes the new icon after an update.
    ///   - the Instance property, so a second start hands its file or folder to this window.
    /// Make one per main window; call Apply on every theme change. It takes itself off when the window closes.
    /// </summary>
    public sealed class WindowIcons
    {
        readonly Window _w;
        IntPtr _hwnd, _big, _small;
        Theme _theme;
        long _version = (DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond) % 100000000 * 10;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SetProp(IntPtr hwnd, string name, IntPtr value);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr RemoveProp(IntPtr hwnd, string name);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
        [DllImport("user32.dll", EntryPoint = "SendMessageW")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        public WindowIcons(Window w)
        {
            _w = w;
            w.DpiChanged += (o, e) =>
                // WPF sets its own icons again for the new DPI: ours go back on top (and vShell picks them up)
                w.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { Bump(); SetNative(); }));
            w.Closed += (o, e) =>
            {
                if (_hwnd != IntPtr.Zero) { RemoveProp(_hwnd, "vShell.LiveIcon"); RemoveProp(_hwnd, Instance.Prop); }
                if (_big != IntPtr.Zero) DestroyIcon(_big);
                if (_small != IntPtr.Zero) DestroyIcon(_small);
                _big = _small = IntPtr.Zero;
            };
        }

        /// <summary>The theme changed (or the window's handle was just made): new icons in its colours.</summary>
        public void Apply(Theme t)
        {
            _theme = t;
            try
            {
                _hwnd = new WindowInteropHelper(_w).Handle;
                if (_hwnd != IntPtr.Zero) SetProp(_hwnd, Instance.Prop, new IntPtr(Instance.Id));   // a second start hands its file here
                Bump();
                _w.Icon = AppIcon.WindowIcon(t);
                SetNative();
            }
            catch (Exception ex) { Log.Write("Icon: " + ex.Message); }
        }

        void Bump()
        {
            if (_hwnd != IntPtr.Zero) SetProp(_hwnd, "vShell.LiveIcon", new IntPtr(++_version));
        }

        void SetNative()
        {
            if (_hwnd == IntPtr.Zero || _theme == null) return;
            double dpi = VisualTreeHelper.GetDpi(_w).DpiScaleX;
            IntPtr big = AppIcon.Handle(_theme, (int)Math.Round(64 * dpi)), small = AppIcon.Handle(_theme, (int)Math.Round(16 * dpi));
            if (big == IntPtr.Zero || small == IntPtr.Zero) return;
            SendMessage(_hwnd, 0x0080 /*WM_SETICON*/, new IntPtr(1) /*ICON_BIG*/, big);
            SendMessage(_hwnd, 0x0080, IntPtr.Zero /*ICON_SMALL*/, small);
            if (_big != IntPtr.Zero) DestroyIcon(_big);
            if (_small != IntPtr.Zero) DestroyIcon(_small);
            _big = big;
            _small = small;
        }
    }
}
