using vSuite.Ui;
using Xunit;

namespace vSuite.Tests
{
    public class TabStripTests
    {
        [Fact]
        public void FewTabsAreFullWidth()
        {
            bool over;
            Assert.Equal(220, TabStrip.WidthFor(1000, 3, 90, 220, 52, out over));
            Assert.False(over);
        }

        [Fact]
        public void MoreTabsShareTheRoom()
        {
            bool over;
            Assert.Equal(198, TabStrip.WidthFor(1000, 5, 90, 220, 52, out over));   // 1000 / 5 - 2
            Assert.False(over);
        }

        [Fact]
        public void TooManyScrollAtTheNarrowest()
        {
            bool over;
            Assert.Equal(90, TabStrip.WidthFor(1000, 20, 90, 220, 52, out over));
            Assert.True(over);
        }

        [Fact]
        public void NoTabs()
        {
            bool over;
            Assert.Equal(220, TabStrip.WidthFor(500, 0, 90, 220, 52, out over));
            Assert.False(over);
        }
    }
}
