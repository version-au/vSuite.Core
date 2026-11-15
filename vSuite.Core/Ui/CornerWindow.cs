using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
namespace vSuite.Ui
{
    /// <summary>
    /// A small window of its own over the bottom right of an area of the app's window (the anchor), for an app whose content there is a
    /// native window that would cover anything drawn in the window itself: vView's playing video and Jellyfin page, vTerm's terminal text.
    /// Owned by the main window (above it, never above other apps), not activated by a click (the keys stay where they were), only shown
    /// while it's Wanted and the main window shows. The update notice (NoticeWindow); vView's Jellyfin Skip intro / Up next derive from it too.
    /// </summary>
    public abstract class CornerWindow : Window
    {
        protected readonly Window Main;
        readonly FrameworkElement _anchor;

        protected CornerWindow(Window main, FrameworkElement anchor, string title)
        {
            Main = main;
            _anchor = anchor;
            Owner = main;
            Title = title;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            SizeToContent = SizeToContent.WidthAndHeight;

            SourceInitialized += (s, e) =>
            {
                IntPtr h = new WindowInteropHelper(this).Handle;
                SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            };
            SizeChanged += (s, e) => Place();
            main.LocationChanged += (s, e) => Place();
            main.SizeChanged += (s, e) => Place();
            main.StateChanged += (s, e) => Sync();
            main.IsVisibleChanged += (s, e) => Sync();   // closes with it (an owned window)
            anchor.SizeChanged += (s, e) => Place();
        }

        /// <summary>It has something to show.</summary>
        protected abstract bool Wanted { get; }

        /// <summary>The theme's colours and the suite's styles (no glass - what's in it draws its own background).</summary>
        public void ApplyLook(Theme t, bool blur)
        {
            if (t != null) Look.Dress(this, t, blur, false);
        }

        /// <summary>Shown while Wanted and the main window is up (not hidden behind the classic player, not minimised).</summary>
        public void Sync()
        {
            bool want = Wanted && Main.IsVisible && Main.WindowState != WindowState.Minimized;
            if (want && !IsVisible) Show();
            else if (!want && IsVisible) Hide();
            if (want) Place();
        }

        /// <summary>Its bottom right on the picture area's bottom right.</summary>
        void Place()
        {
            if (!IsVisible || _anchor.ActualWidth <= 0 || PresentationSource.FromVisual(_anchor) == null) return;
            try
            {
                Point corner = _anchor.PointToScreen(new Point(_anchor.ActualWidth, _anchor.ActualHeight));   // device pixels
                var src = PresentationSource.FromVisual(Main);
                if (src?.CompositionTarget != null) corner = src.CompositionTarget.TransformFromDevice.Transform(corner);
                Left = corner.X - ActualWidth;
                Top = corner.Y - ActualHeight;
            }
            catch (InvalidOperationException) { }   // not connected to a screen right now
        }

        const int GWL_EXSTYLE = -20, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080;
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
    }

    /// <summary>The update notice (vSuite's UpdateNotice), in a corner window - shown while UpdateCenter has news.</summary>
    public sealed class NoticeWindow : CornerWindow
    {
        public NoticeWindow(Window main, FrameworkElement anchor) : base(main, anchor, App.Name + " update")
        {
            // the notice's own margin (right / bottom) and this padding (left / top) leave room for its shadow
            Content = new Border { Padding = new Thickness(14, 14, 0, 0), Child = new UpdateNotice() };
            Action changed = () => Dispatcher.BeginInvoke(new Action(Sync));
            UpdateCenter.Changed += changed;
            Closed += (s, e) => UpdateCenter.Changed -= changed;
        }

        protected override bool Wanted { get { return UpdateCenter.NoticeVisible && UpdateCenter.Available != null; } }
    }
}
