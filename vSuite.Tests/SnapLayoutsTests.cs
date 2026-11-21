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
