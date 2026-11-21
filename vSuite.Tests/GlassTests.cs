using System.Collections.Generic;
using System.Windows;
using vSuite.Ui;
using Xunit;

namespace vSuite.Tests
{
    /// <summary>The glass under a window: which monitor covers which part of it (a window dragged across monitors or past a screen's edge).</summary>
    public class GlassTests
    {
        // two monitors side by side: 1920x1080 at 0,0 and 2560x1440 at 1920,0
        static readonly IReadOnlyList<Int32Rect> Two = new[] { new Int32Rect(0, 0, 1920, 1080), new Int32Rect(1920, 0, 2560, 1440) };

        [Fact]
        public void a_window_on_one_monitor_is_one_piece_covering_it()
        {
            var p = Glass.Pieces(new Int32Rect(100, 100, 800, 600), Two);
            Assert.Single(p);
            Assert.Equal(0, p[0].Monitor);
            Assert.Equal(new Int32Rect(100, 100, 800, 600), p[0].Part);
        }

        [Fact]
        public void across_two_monitors_each_covers_its_own_part()
        {
            var p = Glass.Pieces(new Int32Rect(1500, 200, 1000, 500), Two);
            Assert.Equal(2, p.Count);
            Assert.Equal((0, new Int32Rect(1500, 200, 420, 500)), p[0]);
            Assert.Equal((1, new Int32Rect(1920, 200, 580, 500)), p[1]);
        }

        [Fact]
        public void half_off_the_screen_only_the_part_on_it()
        {
            // off the left edge, and below the shorter monitor
            var left = Glass.Pieces(new Int32Rect(-400, 100, 1000, 600), Two);
            Assert.Equal((0, new Int32Rect(0, 100, 600, 600)), Assert.Single(left));
            var below = Glass.Pieces(new Int32Rect(1700, 900, 600, 400), Two);
            Assert.Equal(2, below.Count);
            Assert.Equal(new Int32Rect(1700, 900, 220, 180), below[0].Part);     // (the 1080-high monitor ends at 1080)
            Assert.Equal(new Int32Rect(1920, 900, 380, 400), below[1].Part);
            // wholly off every monitor: nothing
            Assert.Empty(Glass.Pieces(new Int32Rect(5000, 0, 300, 300), Two));
        }
    }
}
