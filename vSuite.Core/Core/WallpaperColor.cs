using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace vSuite
{
    /// <summary>The theme colours worked out from a wallpaper (all opaque; the caller applies the background opacity).</summary>
    public sealed class WallpaperPalette
    {
        public Color Background, Background2, Foreground, MainAccent, SecondaryAccent;
        /// <summary>The raw dominant colours the theme was made from (largest first), for information.</summary>
        public Color[] Dominant;
    }

    /// <summary>
    /// vShell's Wallpaper Theme: finds the main colours of the main monitor's wallpaper and turns them into a theme (moved here from
    /// vShell, so vShell and the apps work out exactly the same colours).
    ///   1st dominant colour -> background (nudged slightly darker / lighter) and a second shade of it for the gradient,
    ///   2nd -> Main Accent, 3rd -> Secondary Accent (both adjusted until they stand out from the background),
    ///   text -> near-white or near-black, tinted with the background hue, always readable (contrast 4.5:1 or better).
    /// A downscaled copy of the picture is grouped into colours with k-means; the result is cached until the file changes.
    /// </summary>
    public static class WallpaperColor
    {
        static readonly object Gate = new object();
        static string _key;
        static WallpaperPalette _palette;

        /// <summary>The palette of the main monitor's wallpaper (cached per file); false when there's no wallpaper picture.</summary>
        public static bool TryGetPalette(out WallpaperPalette p)
        {
            return TryGetPalette(FindWallpaper(), out p);
        }

        /// <summary>The palette of this picture (vShell passes the one it shows on the main monitor).</summary>
        public static bool TryGetPalette(string path, out WallpaperPalette p)
        {
            p = null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            string key;
            try { key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks; }
            catch { return false; }
            lock (Gate)
            {
                if (key != _key)
                {
                    _key = key;
                    _palette = Compute(path);
                }
                p = _palette;
            }
            return p != null;
        }

        static string _seen;
        /// <summary>true when the main monitor's wallpaper (or which file it is) changed since the last call - the theme should then be rebuilt.</summary>
        public static bool Changed()
        {
            string path = FindWallpaper();
            string key = "";
            try { if (!string.IsNullOrEmpty(path)) key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks; } catch { }
            lock (Gate)
            {
                if (key == _seen) return false;
                bool first = _seen == null;
                _seen = key;
                return !first;
            }
        }

        // =================================================================
        // Which picture
        // =================================================================

        [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDesktopWallpaper
        {
            void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
            [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
            [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint monitorIndex);
            uint GetMonitorDevicePathCount();
            RECT GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        static readonly Guid DesktopWallpaperClsid = new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD");

        /// <summary>
        /// The main monitor's picture: what Windows shows on the monitor at 0,0 (vShell sets its per-monitor wallpapers and slideshows
        /// through Windows, so this is the picture vShell uses too), else the Windows wallpaper.
        /// </summary>
        public static string FindWallpaper()
        {
            try
            {
                var dw = ComObject.Create<IDesktopWallpaper>(DesktopWallpaperClsid);   // (not 'new': see ComObject)
                uint n = dw.GetMonitorDevicePathCount();
                for (uint i = 0; i < n; i++)
                {
                    try
                    {
                        string id = dw.GetMonitorDevicePathAt(i);
                        RECT r = dw.GetMonitorRECT(id);
                        if (r.Left != 0 || r.Top != 0 || r.Right <= r.Left) continue;   // not the main monitor (or not connected)
                        string f = dw.GetWallpaper(id);
                        if (!string.IsNullOrEmpty(f) && File.Exists(f)) return f;
                    }
                    catch { }
                }
            }
            catch { }
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                {
                    string p = k == null ? null : k.GetValue("WallPaper") as string;
                    if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p;
                }
                string t = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
                if (File.Exists(t)) return t;
            }
            catch { }
            return null;
        }
        // =================================================================
        // Finding the dominant colours
        // =================================================================
        sealed class Cluster
        {
            public double R, G, B;
            public int Count;
            public double Share;
            public Color Color { get { return Color.FromRgb(ClampB(R), ClampB(G), ClampB(B)); } }
        }

        static WallpaperPalette Compute(string path)
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(path);
                bi.DecodePixelWidth = 96;
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();
                bi.Freeze();
                var bmp = new FormatConvertedBitmap(bi, PixelFormats.Bgra32, null, 0);
                int w = bmp.PixelWidth, h = bmp.PixelHeight;
                var px = new byte[w * h * 4];
                bmp.CopyPixels(px, w * 4, 0);

                var pts = new List<double[]>(w * h);
                for (int i = 0; i < px.Length; i += 4)
                    if (px[i + 3] >= 128) pts.Add(new double[] { px[i + 2], px[i + 1], px[i] });
                if (pts.Count == 0) return null;

                List<Cluster> clusters = KMeans(pts, 8);
                var p = Build(clusters);
                Log.Write("Wallpaper theme from " + Path.GetFileName(path) + ": bg " + Hex(p.Background) + ", accent " + Hex(p.MainAccent) +
                          ", secondary " + Hex(p.SecondaryAccent) + ", text " + Hex(p.Foreground));
                return p;
            }
            catch (Exception ex) { Log.Write("WallpaperColor: " + ex.Message); return null; }
        }

        /// <summary>k-means in RGB with a fixed seed (the same picture always gives the same theme). Near-identical groups are merged.</summary>
        static List<Cluster> KMeans(List<double[]> pts, int k)
        {
            var rnd = new Random(7);
            var centres = new List<double[]>();
            centres.Add((double[])pts[rnd.Next(pts.Count)].Clone());
            // k-means++ start: each new centre far from the ones chosen so far
            var dist = new double[pts.Count];
            while (centres.Count < k)
            {
                double sum = 0;
                for (int i = 0; i < pts.Count; i++)
                {
                    double best = double.MaxValue;
                    foreach (var c in centres) best = Math.Min(best, D2(pts[i], c));
                    dist[i] = best;
                    sum += best;
                }
                if (sum <= 0) break;     // fewer distinct colours than k
                double r = rnd.NextDouble() * sum;
                int pick = pts.Count - 1;
                for (int i = 0; i < pts.Count; i++) { r -= dist[i]; if (r <= 0) { pick = i; break; } }
                centres.Add((double[])pts[pick].Clone());
            }

            var assign = new int[pts.Count];
            for (int iter = 0; iter < 14; iter++)
            {
                bool moved = false;
                for (int i = 0; i < pts.Count; i++)
                {
                    int bi = 0; double bd = double.MaxValue;
                    for (int c = 0; c < centres.Count; c++)
                    {
                        double d = D2(pts[i], centres[c]);
                        if (d < bd) { bd = d; bi = c; }
                    }
                    if (assign[i] != bi) { moved = true; assign[i] = bi; }
                }
                var sums = new double[centres.Count, 4];
                for (int i = 0; i < pts.Count; i++)
                {
                    int c = assign[i];
                    sums[c, 0] += pts[i][0]; sums[c, 1] += pts[i][1]; sums[c, 2] += pts[i][2]; sums[c, 3]++;
                }
                for (int c = 0; c < centres.Count; c++)
                    if (sums[c, 3] > 0) centres[c] = new[] { sums[c, 0] / sums[c, 3], sums[c, 1] / sums[c, 3], sums[c, 2] / sums[c, 3] };
                if (!moved && iter > 0) break;
            }

            var list = new List<Cluster>();
            var counts = new int[centres.Count];
            foreach (int a in assign) counts[a]++;
            for (int c = 0; c < centres.Count; c++)
                if (counts[c] > 0) list.Add(new Cluster { R = centres[c][0], G = centres[c][1], B = centres[c][2], Count = counts[c] });
            list.Sort(delegate (Cluster a, Cluster b) { return b.Count.CompareTo(a.Count); });

            // merge groups that are practically the same colour into the bigger one
            for (int i = 0; i < list.Count; i++)
                for (int j = list.Count - 1; j > i; j--)
                    if (Math.Sqrt(D2(new[] { list[i].R, list[i].G, list[i].B }, new[] { list[j].R, list[j].G, list[j].B })) < 36)
                    {
                        Cluster a = list[i], b = list[j];
                        int n = a.Count + b.Count;
                        a.R = (a.R * a.Count + b.R * b.Count) / n; a.G = (a.G * a.Count + b.G * b.Count) / n; a.B = (a.B * a.Count + b.B * b.Count) / n;
                        a.Count = n;
                        list.RemoveAt(j);
                    }
            list.Sort(delegate (Cluster a, Cluster b) { return b.Count.CompareTo(a.Count); });
            foreach (var c in list) c.Share = c.Count / (double)pts.Count;
            return list;
        }

        static double D2(double[] a, double[] b)
        {
            // weighted RGB distance (roughly how different two colours look)
            double dr = a[0] - b[0], dg = a[1] - b[1], db = a[2] - b[2];
            return 2 * dr * dr + 4 * dg * dg + 3 * db * db;
        }

        static double Dist(Color a, Color b)
        {
            return Math.Sqrt(D2(new double[] { a.R, a.G, a.B }, new double[] { b.R, b.G, b.B })) / 3.0;
        }

        // =================================================================
        // Turning them into a theme
        // =================================================================
        static WallpaperPalette Build(List<Cluster> clusters)
        {
            var p = new WallpaperPalette();
            var dom = new List<Color>();
            foreach (var c in clusters) dom.Add(c.Color);
            p.Dominant = dom.ToArray();

            // ---- background: the most common colour, nudged so it is not exactly the wallpaper ----
            Color baseBg = dom[0];
            double h, s, l;
            ToHsl(baseBg, out h, out s, out l);
            bool dark = Luminance(baseBg) < 0.22;
            if (dark) l = Math.Max(0.05, l - 0.05);                       // a little deeper
            else if (l > 0.6) l = Math.Min(0.96, l + 0.04);               // a little lighter
            else l = Luminance(baseBg) < 0.32 ? Math.Max(0.06, l - 0.12) : Math.Min(0.94, l + 0.12);   // mid tones: pushed apart so text reads
            s = Math.Min(s, 0.75);
            p.Background = FromHsl(h, s, l);
            dark = Luminance(p.Background) < 0.3;

            // gradient: another shade of the same colour (lighter on a dark theme, darker on a light one), hue turned a touch
            p.Background2 = FromHsl(h + 10, Math.Min(1, s * 1.05), dark ? Math.Min(0.5, l + 0.09) : Math.Max(0.45, l - 0.08));

            // ---- text: near-white or near-black in the background's hue, checked for contrast ----
            Color fg = dark ? FromHsl(h, Math.Min(s, 0.18), 0.93) : FromHsl(h, Math.Min(s, 0.3), 0.12);
            if (Contrast(fg, p.Background) < 4.5 || Contrast(fg, p.Background2) < 4.5)
                fg = dark ? Color.FromRgb(0xF4, 0xF4, 0xF6) : Color.FromRgb(0x14, 0x14, 0x18);
            p.Foreground = fg;

            // ---- accents: the 2nd and 3rd colours of the picture (greys, black and white do not make an accent) ----
            var picks = new List<Color>();
            for (int i = 1; i < clusters.Count && picks.Count < 2; i++)
            {
                Color c = dom[i];
                if (clusters[i].Share < 0.01) continue;                  // a speck, not a colour of the picture
                if (IsGreyish(c)) continue;
                if (Dist(c, baseBg) < 18) continue;                        // the background again
                bool same = false;
                foreach (var q in picks) if (Dist(c, q) < 30) same = true;
                if (!same) picks.Add(c);
            }
            // fewer than two usable colours (a single-colour or black-and-white picture): make them from the hues it has
            double ah, asat, al;
            if (picks.Count == 0)
            {
                ToHsl(baseBg, out ah, out asat, out al);
                // a colourful background: a lighter / darker version of its own hue; a grey one: a calm blue
                picks.Add(asat < 0.15 ? FromHsl(210, 0.6, 0.6) : FromHsl(ah, Math.Max(0.55, asat), dark ? 0.68 : 0.38));
            }
            if (picks.Count == 1)
            {
                ToHsl(picks[0], out ah, out asat, out al);
                picks.Add(FromHsl(ah + 45, Math.Max(0.5, asat), dark ? Math.Min(0.8, al + 0.1) : Math.Max(0.25, al - 0.1)));   // a neighbouring hue
            }
            p.MainAccent = Readable(picks[0], p.Background, 3.0);
            p.SecondaryAccent = Readable(picks[1], p.Background, 3.0);

            // the two accents must not look the same once adjusted
            if (Dist(p.MainAccent, p.SecondaryAccent) < 30)
            {
                ToHsl(p.SecondaryAccent, out ah, out asat, out al);
                p.SecondaryAccent = Readable(FromHsl(ah + 40, Math.Max(0.45, asat), al), p.Background, 3.0);
            }
            return p;
        }

        static bool IsGreyish(Color c)
        {
            double h, s, l;
            ToHsl(c, out h, out s, out l);
            return s < 0.15 || l < 0.07 || l > 0.95;
        }

        /// <summary>Move a colour's lightness away from the background until it reaches the contrast ratio (keeps its hue).</summary>
        static Color Readable(Color c, Color bg, double ratio)
        {
            if (Contrast(c, bg) >= ratio) return c;
            double h, s, l;
            ToHsl(c, out h, out s, out l);
            bool up = Luminance(bg) < 0.3;
            s = Math.Max(s, 0.35);           // a grey accent gets a little colour so it reads as an accent
            for (int i = 0; i < 40; i++)
            {
                l = up ? Math.Min(1, l + 0.025) : Math.Max(0, l - 0.025);
                Color t = FromHsl(h, s, l);
                if (Contrast(t, bg) >= ratio) return t;
            }
            return up ? Color.FromRgb(0xF0, 0xF0, 0xF0) : Color.FromRgb(0x18, 0x18, 0x18);
        }

        // =================================================================
        // Colour maths
        // =================================================================
        /// <summary>WCAG relative luminance, 0 (black) .. 1 (white).</summary>
        public static double Luminance(Color c)
        {
            return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
        }

        static double Lin(byte v)
        {
            double x = v / 255.0;
            return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }

        /// <summary>WCAG contrast ratio, 1 .. 21 (4.5 = comfortable text, 3 = large text / graphics).</summary>
        public static double Contrast(Color a, Color b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        static void ToHsl(Color c, out double h, out double s, out double l)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            l = (max + min) / 2;
            s = d <= 0 ? 0 : d / (1 - Math.Abs(2 * l - 1));
            if (d <= 0) h = 0;
            else if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
            if (h < 0) h += 360;
        }

        static Color FromHsl(double h, double s, double l)
        {
            h = ((h % 360) + 360) % 360;
            s = Math.Max(0, Math.Min(1, s)); l = Math.Max(0, Math.Min(1, l));
            double c = (1 - Math.Abs(2 * l - 1)) * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = l - c / 2, r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return Color.FromRgb(ClampB((r + m) * 255), ClampB((g + m) * 255), ClampB((b + m) * 255));
        }

        static byte ClampB(double v) { return (byte)Math.Max(0, Math.Min(255, Math.Round(v))); }

        public static string Hex(Color c) { return string.Format("#{0:X2}{1:X2}{2:X2}{3:X2}", c.A, c.R, c.G, c.B); }
    }
}
