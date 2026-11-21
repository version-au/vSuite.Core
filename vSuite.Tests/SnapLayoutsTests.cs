using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using vSuite.Ui;
using Xunit;

namespace vSuite.Tests
{
    /// <summary>
    /// Snap layouts on a window's own maximise button: Windows is told "the maximise button" over it only while snap layouts can show;
    /// otherwise the window answers as before. A press and release there runs the button's Click; its hover look comes and goes.
    /// </summary>
    public class SnapLayoutsTests
    {
        const int WM_NCHITTEST = 0x84, WM_NCLBUTTONDOWN = 0xA1, WM_NCLBUTTONUP = 0xA2, WM_NCMOUSELEAVE = 0x2A2, HTMAXBUTTON = 9;

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        static IntPtr At(int x, int y) { return new IntPtr((y << 16) | (x & 0xFFFF)); }

        static void Settle() { Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); }

        [Fact]
        public void windows_setting_is_read_the_first_time_then_every_few_seconds()
        {
            // 1.0.44 never read it (its "last read" overflowed), so the menu never came on a real window
            Assert.True(SnapLayouts.ReadAgain(false, 0, 0));
            Assert.True(SnapLayouts.ReadAgain(false, 0, long.MaxValue - 5));
            Assert.False(SnapLayouts.ReadAgain(true, 10_000, 11_000));
            Assert.True(SnapLayouts.ReadAgain(true, 10_000, 13_000));
            // and the real thing gives Windows' setting, not a stale "off"
            SnapLayouts.IsOnOverride = null;
            Assert.Equal(SnapLayouts.ReadSetting(), SnapLayouts.IsOn);
        }

        [Fact]
        public void the_maximise_button_gets_the_layouts_menu_only_while_snap_layouts_are_on()
        {
            Exception failed = null;
            var t = new Thread(() =>
            {
                try { Run(); }
                catch (Exception ex) { failed = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (failed != null) throw new Exception(failed.Message, failed);
        }

        [Fact]
        public void a_drawn_maximise_box_made_again_keeps_working()
        {
            Exception failed = null;
            var t = new Thread(() =>
            {
                try { RunDrawn(); }
                catch (Exception ex) { failed = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (failed != null) throw new Exception(failed.Message, failed);
        }

        /// <summary>vTerm's strip: Borders (not Buttons), its own click and hover colours, the boxes made again when the window changes.</summary>
        static void RunDrawn()
        {
            SnapLayouts.IsOnOverride = () => true;
            try
            {
                int clicks = 0;
                var lit = new System.Collections.Generic.Dictionary<FrameworkElement, string>();
                var holder = new Border { Width = 46, Height = 36, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
                WindowChrome.SetIsHitTestVisibleInChrome(holder, true);
                Border Box() { return new Border { Width = 46, Height = 36, Background = Brushes.Transparent }; }
                holder.Child = Box();
                var w = new Window
                {
                    Width = 400, Height = 200, Left = -20000, Top = -20000, WindowStartupLocation = WindowStartupLocation.Manual,
                    ShowActivated = false, ShowInTaskbar = false, Content = new Grid { Children = { holder } },
                };
                WindowChrome.SetWindowChrome(w, new WindowChrome { CaptionHeight = 36, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false });
                SnapLayouts.Attach(w, () => holder.Child as FrameworkElement, () => clicks++,
                    (e, hot, pressed) => lit[e] = pressed ? "pressed" : hot ? "hot" : "plain");
                w.Show();
                Settle();
                var hwnd = new WindowInteropHelper(w).Handle;
                Point c = holder.PointToScreen(new Point(23, 18));
                IntPtr on = At((int)c.X, (int)c.Y);

                var first = (FrameworkElement)holder.Child;
                Assert.Equal(HTMAXBUTTON, SendMessage(hwnd, WM_NCHITTEST, IntPtr.Zero, on).ToInt32());
                Assert.Equal("hot", lit[first]);
                SendMessage(hwnd, WM_NCLBUTTONDOWN, new IntPtr(HTMAXBUTTON), on);
                Assert.Equal("pressed", lit[first]);
                SendMessage(hwnd, WM_NCLBUTTONUP, new IntPtr(HTMAXBUTTON), on);
                Settle();
                Assert.Equal(1, clicks);

                // the window made its buttons again (maximised: a new glyph) with the pointer still there
                holder.Child = Box();
                Settle();
                var second = (FrameworkElement)holder.Child;
                Assert.Equal(HTMAXBUTTON, SendMessage(hwnd, WM_NCHITTEST, IntPtr.Zero, on).ToInt32());
                Assert.Equal("plain", lit[first]);
                Assert.Equal("hot", lit[second]);
                SendMessage(hwnd, WM_NCMOUSELEAVE, IntPtr.Zero, IntPtr.Zero);
                Assert.Equal("plain", lit[second]);

                // no maximise box shown (vTerm's drop-down): the window's own answer
                holder.Child = null;
                Settle();
                Assert.NotEqual(HTMAXBUTTON, SendMessage(hwnd, WM_NCHITTEST, IntPtr.Zero, on).ToInt32());
                w.Close();
            }
            finally { SnapLayouts.IsOnOverride = null; }
        }

        static void Run()
        {
            bool on = true;
            SnapLayouts.IsOnOverride = () => on;
            try
            {
                int clicks = 0;
                var max = new Button { Width = 46, Height = 36, Content = "max", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
                WindowChrome.SetIsHitTestVisibleInChrome(max, true);
                max.Click += delegate { clicks++; };
                var other = new Button { Width = 46, Height = 36, Content = "other", HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
                WindowChrome.SetIsHitTestVisibleInChrome(other, true);
                var grid = new Grid();
                grid.Children.Add(max);
                grid.Children.Add(other);
                var w = new Window
                {
                    Width = 400, Height = 200, Left = -20000, Top = -20000, WindowStartupLocation = WindowStartupLocation.Manual,
                    ShowActivated = false, ShowInTaskbar = false, Content = grid,
                };
                WindowChrome.SetWindowChrome(w, new WindowChrome { CaptionHeight = 36, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false });
                SnapLayouts.Attach(w, max);
                w.Show();
                Settle();
                var hwnd = new WindowInteropHelper(w).Handle;

                Point c = max.PointToScreen(new Point(23, 18));
                Point o = other.PointToScreen(new Point(23, 18));
                IntPtr onMax = At((int)c.X, (int)c.Y), onOther = At((int)o.X, (int)o.Y);

                // on: Windows hears "the maximise button" there, and the button looks hovered
                Assert.Equal(HTMAXBUTTON, SendMessage(hwnd, WM_NCHITTEST, IntPtr.Zero, onMax).ToInt32());
                Assert.Equal(BaseValueSource.Local, DependencyPropertyHelper.GetValueSource(max, Control.BackgroundProperty).BaseValueSource);
                // anywhere else: the window's own answer, as before
                Assert.NotEqual(HTMAXBUTTON, SendMessage(hwnd, WM_NCHITTEST, IntPtr.Zero, onOther).ToInt32());
                Assert.NotEqual(BaseValueSource.Local, DependencyPropertyHelper.GetValueSource(max, Control.BackgroundProperty).BaseValueSource);

                // a press and release on it: the button's own Click, once
                SendMessage(hwnd, WM_NCHITTEST, IntPtr.Zero, onMax);
                SendMessage(hwnd, WM_NCLBUTTONDOWN, new IntPtr(HTMAXBUTTON), onMax);
                SendMessage(hwnd, WM_NCLBUTTONUP, new IntPtr(HTMAXBUTTON), onMax);
                Settle();
                Assert.Equal(1, clicks);
                // a release without a press there (pressed elsewhere) does nothing
                SendMessage(hwnd, WM_NCLBUTTONUP, new IntPtr(HTMAXBUTTON), onMax);
                Settle();
                Assert.Equal(1, clicks);
                // leaving takes the hover look away
                SendMessage(hwnd, WM_NCMOUSELEAVE, IntPtr.Zero, IntPtr.Zero);
                Assert.NotEqual(BaseValueSource.Local, DependencyPropertyHelper.GetValueSource(max, Control.BackgroundProperty).BaseValueSource);

                // off (Windows 10, or the setting off): the button is left to the window, as it always was
                on = false;
                Assert.NotEqual(HTMAXBUTTON, SendMessage(hwnd, WM_NCHITTEST, IntPtr.Zero, onMax).ToInt32());
                Assert.NotEqual(BaseValueSource.Local, DependencyPropertyHelper.GetValueSource(max, Control.BackgroundProperty).BaseValueSource);
                w.Close();
            }
            finally { SnapLayouts.IsOnOverride = null; }
        }
    }
}
