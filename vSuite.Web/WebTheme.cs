using System.Globalization;
using System.Text;

namespace vSuite.Web;

/// <summary>
/// vShell's theme as CSS variables, worked out the way vSuite.Core's Theme does it for the apps' vx.* brushes (hover = text at 0x22,
/// dim text = 0x99, panel = 0x0D / 0x08 …), so the page looks like a vSuite window. vShell's default theme until the app's vShell plugin sends yours
/// (PUT /api/v1/theme -> ThemeStore). Field names are what the plugins send - keep them.
/// </summary>
public sealed class WebTheme
{
    public string Background = "#1E1E2E", Background2 = "#313244", Foreground = "#CDD6F4", MainAccent = "#89B4FA", SecondaryAccent = "#F5C2E7", Border = "";
    public bool Gradient;
    public int GradientAngle = 90;
    public double Radius = 8;
    public int BorderOpacity = 65;

    public static readonly WebTheme Default = new();

    readonly record struct Rgb(int R, int G, int B)
    {
        public string A(double a) { return "rgba(" + R + "," + G + "," + B + "," + a.ToString("0.###", CultureInfo.InvariantCulture) + ")"; }
        public string Hex { get { return "#" + R.ToString("X2") + G.ToString("X2") + B.ToString("X2"); } }
        public double Lum { get { return (0.299 * R + 0.587 * G + 0.114 * B) / 255.0; } }
    }

    static Rgb Parse(string s, Rgb fallback)
    {
        if (string.IsNullOrWhiteSpace(s)) return fallback;
        s = s.Trim().TrimStart('#');
        if (s.Length == 8) s = s.Substring(2);           // vShell writes #AARRGGBB
        if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, null, out int v)) return fallback;
        return new Rgb((v >> 16) & 255, (v >> 8) & 255, v & 255);
    }

    static string Px(double v) { return v.ToString("0.##", CultureInfo.InvariantCulture) + "px"; }

    public string Css(string textSize)
    {
        var bg = Parse(Background, new Rgb(0x1E, 0x1E, 0x2E));
        var bg2 = Parse(Background2, new Rgb(0x31, 0x32, 0x44));
        var fg = Parse(Foreground, new Rgb(0xCD, 0xD6, 0xF4));
        var acc = Parse(MainAccent, new Rgb(0x89, 0xB4, 0xFA));
        var sec = Parse(SecondaryAccent, new Rgb(0xF5, 0xC2, 0xE7));
        if (Math.Abs(fg.Lum - bg.Lum) < 0.35) fg = bg.Lum < 0.5 ? new Rgb(0xE6, 0xE6, 0xEE) : new Rgb(0x20, 0x20, 0x24);
        var border = string.IsNullOrWhiteSpace(Border) ? acc : Parse(Border, acc);
        bool dark = fg.Lum > 0.5;
        string onAcc = 0.2126 * acc.R + 0.7152 * acc.G + 0.0722 * acc.B > 150 ? "#11111B" : "#FFFFFF";

        var v = new StringBuilder(":root{");
        void Set(string k, string val) { v.Append("--").Append(k).Append(':').Append(val).Append(';'); }
        Set("bg", bg.Hex);
        Set("bg-fill", Gradient ? "linear-gradient(" + (GradientAngle + 90) + "deg," + bg.Hex + "," + bg2.Hex + ")" : bg.Hex);
        Set("popup", bg.A(0.96));
        Set("panel", fg.A(dark ? 0x0D / 255.0 : 0x08 / 255.0));
        Set("panel-border", border.A(BorderOpacity / 100.0));
        Set("fg", fg.Hex);
        Set("fg-dim", fg.A(0x99 / 255.0));
        Set("accent", acc.Hex);
        Set("on-accent", onAcc);
        Set("secondary", sec.Hex);
        Set("hover", fg.A(0x22 / 255.0));
        Set("active", fg.A(0x38 / 255.0));
        Set("selected", acc.A(0x50 / 255.0));
        Set("track", fg.A(0x30 / 255.0));
        Set("thumb", fg.A(0x60 / 255.0));
        Set("line", fg.A(0x14 / 255.0));
        Set("side", dark ? "rgba(0,0,0,0.28)" : fg.A(0x0D / 255.0));
        Set("ctrl", fg.A(0x17 / 255.0));
        Set("ctrl-hover", fg.A(0x26 / 255.0));
        Set("ctrl-border", fg.A(0x2B / 255.0));
        Set("sel-bg", acc.A(0x38 / 255.0));
        Set("code", dark ? "rgba(0,0,0,0.22)" : "rgba(0,0,0,0.04)");
        // status colours, made for the theme: lighter on a dark one, darker on a light one (as vPad's Syntax.Paint)
        Set("ok", dark ? "#8FD69A" : "#2E7D3E");
        Set("warn", dark ? "#F1C76E" : "#9A6100");
        Set("bad", dark ? "#F2849A" : "#B83250");
        Set("str", dark ? "#A6E3A1" : "#2E7D3E");
        Set("radius", Px(Radius));
        Set("item-radius", Px(Math.Min(Radius, 4)));
        Set("menu-radius", Px(Math.Min(Radius, 10)));
        Set("tab-radius", Px(Math.Min(Radius, 8)));
        Set("fs", textSize == "larger" ? "14px" : "12.5px");
        v.Append("color-scheme:").Append(dark ? "dark" : "light").Append(";}");
        return v.ToString();
    }
}
