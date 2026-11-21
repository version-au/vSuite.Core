using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using vSuite.Ui;
using Xunit;

namespace vSuite.Tests
{
    /// <summary>
    /// The glass under a window: one picture of the whole desktop (every monitor's wallpaper in its place), the part behind the window
    /// shown - across monitors and past a screen's edge alike.
    /// </summary>
    public class GlassTests
    {
        // two monitors side by side, the right one taller: 1920x1080 at 0,0 and 2560x1440 at 1920,0
        static readonly IReadOnlyList<Int32Rect> Two = new[] { new Int32Rect(0, 0, 1920, 1080), new Int32Rect(1920, 0, 2560, 1440) };

        [Fact]
        public void the_desktop_is_every_monitor_together()
        {
            Assert.Equal(new Int32Rect(0, 0, 4480, 1440), Backdrop.Union(Two));
            // a monitor left of and above the main one (negative coordinates)
            Assert.Equal(new Int32Rect(-1920, -200, 3840, 1280), Backdrop.Union(new[] { new Int32Rect(0, 0, 1920, 1080), new Int32Rect(-1920, -200, 1920, 1080) }));
            Assert.True(Backdrop.Union(new Int32Rect[0]).IsEmpty);
        }

        [Fact]
        public void the_view_follows_the_window_anywhere()
        {
            var desktop = Backdrop.Union(Two);          // the picture: 1/8 of it, 560x180
            // on the right monitor
            Assert.Equal(new Rect(275, 25, 100, 50), Glass.ViewFor(new Rect(2200, 200, 800, 400), desktop, 560, 180));
            // across both: one continuous part of the picture
            Assert.Equal(new Rect(187.5, 25, 125, 62.5), Glass.ViewFor(new Rect(1500, 200, 1000, 500), desktop, 560, 180));
            // half off the left edge: the view goes past the picture's edge and keeps moving (it used to stop at the edge)
            Assert.Equal(new Rect(-50, 12.5, 125, 75), Glass.ViewFor(new Rect(-400, 100, 1000, 600), desktop, 560, 180));
            Assert.Equal(new Rect(-62.5, 12.5, 125, 75), Glass.ViewFor(new Rect(-500, 100, 1000, 600), desktop, 560, 180));
        }

        [Fact]
        public void a_view_past_the_pictures_edge_isnt_stretched()
        {
            // a 40x40 red picture; a 100x100 element showing (20,0)-(60,40) of it: the left half red, the right half nothing (not the red
            // stretched over it all) - so the part of a window still on the screen shows the right part of the wallpaper
            Color left = default, right = default;
            var t = new Thread(() =>
            {
                var px = new byte[40 * 40 * 4];
                for (int i = 0; i < px.Length; i += 4) { px[i + 2] = 255; px[i + 3] = 255; }
                var pic = BitmapSource.Create(40, 40, 96, 96, PixelFormats.Pbgra32, null, px, 160);
                var b = new Border { Width = 100, Height = 100, Background = new ImageBrush(pic) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(20, 0, 40, 40) } };
                b.Measure(new Size(100, 100));
                b.Arrange(new Rect(0, 0, 100, 100));
                var rtb = new RenderTargetBitmap(100, 100, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(b);
                var one = new byte[4];
                rtb.CopyPixels(new Int32Rect(25, 50, 1, 1), one, 4, 0);
                left = Color.FromArgb(one[3], one[2], one[1], one[0]);
                rtb.CopyPixels(new Int32Rect(75, 50, 1, 1), one, 4, 0);
                right = Color.FromArgb(one[3], one[2], one[1], one[0]);
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            Assert.Equal(255, left.R);
            Assert.Equal(255, left.A);
            Assert.Equal(0, right.A);
        }
    }
}
