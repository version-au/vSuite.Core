using System;
using System.Globalization;
using System.Windows.Data;

namespace vSuite.Ui
{
    /// <summary>
    /// Keeps a scrollbar's thumb at least a set length (Controls.xaml: 40 px) however long the content: the Track's ViewportSize is made
    /// as big as it needs to be for that. Dragging still covers the whole range (the Track maps the room left beside the thumb to it), and
    /// the ScrollViewer's own page size is untouched. Values: the scrollbar's ViewportSize, Minimum, Maximum, and the track's length.
    /// </summary>
    public sealed class ThumbMinConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 4) return 0.0;
            double viewport = ToDouble(values[0]), min = ToDouble(values[1]), max = ToDouble(values[2]), track = ToDouble(values[3]);
            double least = 40;
            if (parameter != null) double.TryParse(parameter.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out least);
            return ViewportFor(viewport, max - min, track, least);
        }

        /// <summary>The viewport size that gives a thumb of at least `least` px on a track `track` px long (range = Maximum - Minimum).</summary>
        public static double ViewportFor(double viewport, double range, double track, double least)
        {
            if (double.IsNaN(viewport) || viewport <= 0 || range <= 0 || double.IsNaN(track) || track <= least * 1.5) return viewport;
            double thumb = track * viewport / (range + viewport);
            if (thumb >= least) return viewport;
            return least * range / (track - least);
        }

        static double ToDouble(object o) { return o is double ? (double)o : double.NaN; }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) { throw new NotSupportedException(); }
    }
}
