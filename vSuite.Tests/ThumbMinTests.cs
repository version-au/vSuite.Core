using vSuite.Ui;
using Xunit;

namespace vSuite.Tests
{
    public class ThumbMinTests
    {
        [Fact]
        public void ShortContentKeepsItsThumb()
        {
            // 500 px track, half the content showing: a 250 px thumb - left alone
            Assert.Equal(100, ThumbMinConverter.ViewportFor(100, 100, 500, 40));
        }

        [Fact]
        public void LongContentGetsTheLeastThumb()
        {
            // 500 px track, 30 lines of 100 000: the thumb would be under a pixel - made 40 px
            double v = ThumbMinConverter.ViewportFor(30, 100000, 500, 40);
            double thumb = 500 * v / (100000 + v);
            Assert.Equal(40, thumb, 3);
        }

        [Fact]
        public void TinyTracksAndNoViewportAreLeftAlone()
        {
            Assert.Equal(30, ThumbMinConverter.ViewportFor(30, 100000, 50, 40));
            Assert.True(double.IsNaN(ThumbMinConverter.ViewportFor(double.NaN, 100, 500, 40)));
            Assert.Equal(30, ThumbMinConverter.ViewportFor(30, 0, 500, 40));
        }
    }
}
