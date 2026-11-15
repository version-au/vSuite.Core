using System;
using System.Windows;
using System.Windows.Media;
using vSuite;

namespace vSuite.Ui
{
    /// <summary>
    /// vShell's theme as WPF resources. The brushes are worked out the way vShell's own Theme.cs does it (hover = text at 0x22, active
    /// = 0x38, dim text = 0x99 …) and put in the app's resources under "vx.*" keys, which every style uses as DynamicResource - so
    /// Apply re-colours the whole window at once.
    /// </summary>
    public sealed class Theme
    {
        public Color Bg, Bg2, Fg, Accent, Secondary, Border;
        public bool Gradient;
        public int GradientAngle;
        public string Blur;
        public double Radius, BorderThickness;
        public int BorderOpacity;

        /// <summary>Light text = a dark theme (Windows' dark mode for the title bar, menus and backdrop).</summary>
        public bool IsDark { get { return Lum(Fg) > 0.5; } }

        public static Theme From(VShellColours c)
        {
            var t = new Theme();
            t.Bg = Parse(c.Background, Color.FromArgb(0xE6, 0x1E, 0x1E, 0x2E));
            t.Bg2 = Parse(c.Background2, Color.FromArgb(0xE6, 0x31, 0x32, 0x44));
            t.Fg = A(Parse(c.Foreground, Color.FromRgb(0xCD, 0xD6, 0xF4)), 0xFF);
            t.Accent = A(Parse(c.MainAccent, Color.FromRgb(0x89, 0xB4, 0xFA)), 0xFF);
            t.Secondary = A(Parse(c.SecondaryAccent, Color.FromRgb(0xF5, 0xC2, 0xE7)), 0xFF);
            t.Gradient = c.Gradient;
            t.GradientAngle = c.GradientAngle;
            t.Blur = c.BlurMode;
            t.Radius = c.CornerRadius;
            t.BorderThickness = c.BorderThickness;
            t.BorderOpacity = c.BorderOpacity;
            t.Border = Resolve(c.BorderColor, t);
            // text must stay readable even with an odd theme (text colour close to the background) - as vShell's settings did it
            if (Math.Abs(Lum(t.Fg) - Lum(t.Bg)) < 0.35) t.Fg = Lum(t.Bg) < 0.5 ? Color.FromRgb(0xE6, 0xE6, 0xEE) : Color.FromRgb(0x20, 0x20, 0x24);
            return t;
        }

        /// <summary>
        /// Put the brushes in r. seeThrough: the background keeps vShell's opacity - the window has the glass layer under its content (the
        /// wallpaper, blurred for Blur / Frosted, as vShell's bars show it); false = solid (an app's "Use the theme's opacity" off).
        /// </summary>
        public void Apply(ResourceDictionary r, bool seeThrough)
        {
            byte a1 = seeThrough ? Bg.A : (byte)0xFF, a2 = seeThrough ? Bg2.A : (byte)0xFF;
            r["vx.Bg"] = MakeBg(a1, a2);
            r["vx.Popup"] = B(A(Bg, 0xF5));
            r["vx.Panel"] = B(A(Fg, IsDark ? (byte)0x0D : (byte)0x08));   // cards: vShell settings' strength (5% / 3% of the text colour)
            r["vx.PanelBorder"] = B(A(Border, (byte)Math.Round(BorderOpacity * 255.0 / 100.0)));
            r["vx.PanelBorderThickness"] = new Thickness(BorderThickness);
            r["vx.Fg"] = B(Fg);
            r["vx.FgDim"] = B(A(Fg, 0x99));
            r["vx.Accent"] = B(Accent);
            // text on an accent-filled button: black or white, whichever reads better
            r["vx.OnAccent"] = B(0.2126 * Accent.R + 0.7152 * Accent.G + 0.0722 * Accent.B > 150 ? Color.FromRgb(0x11, 0x11, 0x1B) : Colors.White);
            r["vx.Secondary"] = B(Secondary);
            r["vx.Hover"] = B(A(Fg, 0x22));
            r["vx.Active"] = B(A(Fg, 0x38));
            r["vx.Selected"] = B(A(Accent, 0x50));
            r["vx.Track"] = B(A(Fg, 0x30));
            r["vx.Thumb"] = B(A(Fg, 0x60));
            r["vx.Line"] = B(A(Fg, 0x14));                       // lines between Options rows, card outlines
            r["vx.Side"] = IsDark ? B(Color.FromArgb(0x47, 0, 0, 0)) : B(A(Fg, 0x0D));   // Options' / Settings' page list, a shade off the window
            // form controls (Forms.xaml - text boxes, drop-downs, buttons, sliders), as vShell's settings
            r["vx.Ctrl"] = B(A(Fg, 0x17));
            r["vx.CtrlHover"] = B(A(Fg, 0x26));
            r["vx.CtrlBorder"] = B(A(Fg, 0x2B));
            r["vx.SelBg"] = B(A(Accent, 0x38));
            r["vx.Radius"] = new CornerRadius(Radius);
            r["vx.ItemRadius"] = new CornerRadius(Math.Min(Radius, 4));
            r["vx.MenuRadius"] = new CornerRadius(Math.Min(Radius, 10));
            double tr = Math.Min(Radius, 8);
            r["vx.TabRadius"] = new CornerRadius(tr, tr, 0, 0);
        }

        /// <summary>The window's outline colour (DWM has no alpha: the border colour blended over the background).</summary>
        public Color WindowBorder
        {
            get
            {
                double f = BorderOpacity / 100.0;
                return Color.FromRgb((byte)(Bg.R + (Border.R - Bg.R) * f), (byte)(Bg.G + (Border.G - Bg.G) * f), (byte)(Bg.B + (Border.B - Bg.B) * f));
            }
        }

        /// <summary>The colour Windows paints behind the title area (no alpha): the background, or the middle of its gradient.</summary>
        public Color CaptionColor
        {
            get
            {
                if (!Gradient) return Color.FromRgb(Bg.R, Bg.G, Bg.B);
                return Color.FromRgb((byte)((Bg.R + Bg2.R) / 2), (byte)((Bg.G + Bg2.G) / 2), (byte)((Bg.B + Bg2.B) / 2));
            }
        }

        Brush MakeBg(byte a1, byte a2)
        {
            if (!Gradient) return B(A(Bg, a1));
            double rad = GradientAngle * Math.PI / 180.0;
            double dx = Math.Cos(rad), dy = Math.Sin(rad);
            var g = new LinearGradientBrush(A(Bg, a1), A(Bg2, a2), new Point(0.5 - dx / 2, 0.5 - dy / 2), new Point(0.5 + dx / 2, 0.5 + dy / 2));
            g.Freeze();
            return g;
        }

        /// <summary>A vShell colour setting: empty = the Main Accent, "@main" / "@secondary" / "@text" = that colour, otherwise a colour.</summary>
        static Color Resolve(string spec, Theme t)
        {
            if (string.IsNullOrWhiteSpace(spec)) return t.Accent;
            string k = spec.Trim().ToLowerInvariant();
            if (k == "@main" || k == "@accent") return t.Accent;
            if (k == "@secondary" || k == "@spare") return t.Secondary;
            if (k == "@text") return t.Fg;
            return A(Parse(spec, t.Accent), 0xFF);
        }

        static Color Parse(string s, Color fallback)
        {
            try
            {
                object o = ColorConverter.ConvertFromString(s);
                if (o is Color) return (Color)o;
            }
            catch { }
            return fallback;
        }

        static double Lum(Color c) { return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0; }
        static Color A(Color c, byte a) { return Color.FromArgb(a, c.R, c.G, c.B); }
        static SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    }
}
