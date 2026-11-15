using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace vSuite.Ui
{
    /// <summary>
    /// The app's icon, drawn from the theme so the window / taskbar icon and the mark in the window follow theme changes. Each app gives
    /// its own drawing (<see cref="Drawer"/>: vXplorer's folder, vView's play shape, each holding vShell's three dots);
    /// `App.exe --make-icon file.ico` writes it in vShell's default colours for the exe's own (fixed) icon.
    /// </summary>
    public static class AppIcon
    {
        /// <summary>vShell's default theme (Main Accent, Secondary Accent, text).</summary>
        public static readonly Color DefaultMain = Color.FromRgb(0x89, 0xB4, 0xFA), DefaultSecondary = Color.FromRgb(0xF5, 0xC2, 0xE7),
                                     DefaultText = Color.FromRgb(0xCD, 0xD6, 0xF4), DefaultBackground = Color.FromRgb(0x1E, 0x1E, 0x2E);

        static readonly int[] IcoSizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

        /// <summary>
        /// The app's drawing on a 64 x 64 grid: (main accent, secondary accent, text, background, small) -> a frozen Drawing. small = 16-24 px:
        /// a heavier outline and bigger dots. Set once at start-up.
        /// </summary>
        public static Func<Color, Color, Color, Color, bool, Drawing> Drawer;

        public static Drawing Draw(Color main, Color secondary, Color text, Color background, bool small)
        {
            if (Drawer == null) return new DrawingGroup();
            return Drawer(main, secondary, text, background, small);
        }

        /// <summary>The icon for use inside the window (the mark at the top left).</summary>
        public static ImageSource Image(Theme t, bool small)
        {
            var img = new DrawingImage(Draw(t.Accent, t.Secondary, t.Fg, t.Bg, small));
            img.Freeze();
            return img;
        }

        /// <summary>The window's icon (taskbar, Alt+Tab): every size drawn separately, so small ones use the heavier variant.</summary>
        public static ImageSource WindowIcon(Theme t)
        {
            byte[] ico = Ico(t.Accent, t.Secondary, t.Fg, t.Bg);
            var decoder = new IconBitmapDecoder(new MemoryStream(ico), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            return decoder.Frames[0];
        }

        /// <summary>An .ico file (PNG frames, 16 to 256 px) of the icon in these colours.</summary>
        public static byte[] Ico(Color main, Color secondary, Color text, Color background)
        {
            var frames = new byte[IcoSizes.Length][];
            for (int i = 0; i < IcoSizes.Length; i++) frames[i] = Png(Draw(main, secondary, text, background, IcoSizes[i] <= 24), IcoSizes[i]);

            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)IcoSizes.Length);
                int offset = 6 + 16 * IcoSizes.Length;
                for (int i = 0; i < IcoSizes.Length; i++)
                {
                    int s = IcoSizes[i];
                    w.Write((byte)(s >= 256 ? 0 : s)); w.Write((byte)(s >= 256 ? 0 : s));
                    w.Write((byte)0); w.Write((byte)0);
                    w.Write((ushort)1); w.Write((ushort)32);
                    w.Write(frames[i].Length); w.Write(offset);
                    offset += frames[i].Length;
                }
                foreach (byte[] f in frames) w.Write(f);
                w.Flush();
                return ms.ToArray();
            }
        }

        static byte[] Png(Drawing d, int px)
        {
            var dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(px / 64.0, px / 64.0));
                dc.DrawDrawing(d);
            }
            var rtb = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var ms = new MemoryStream())
            {
                enc.Save(ms);
                return ms.ToArray();
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr CreateIconFromResourceEx(byte[] bits, uint size, bool icon, uint version, int cx, int cy, uint flags);

        /// <summary>
        /// A native icon (HICON) drawn at exactly px - for WM_SETICON. WPF hands Windows only one frame of Window.Icon (16 px), which
        /// vShell's taskbar then stretched for its big buttons and pins: blurry. The caller destroys it (DestroyIcon).
        /// </summary>
        public static IntPtr Handle(Theme t, int px)
        {
            byte[] png = Png(Draw(t.Accent, t.Secondary, t.Fg, t.Bg, px <= 24), px);
            return CreateIconFromResourceEx(png, (uint)png.Length, true, 0x00030000, px, px, 0);
        }

        /// <summary>Write the exe's icon (vShell's default colours) - `App.exe --make-icon path`.</summary>
        public static void Save(string path) { File.WriteAllBytes(path, Ico(DefaultMain, DefaultSecondary, DefaultText, DefaultBackground)); }

        /// <summary>An opaque frozen brush (for drawings).</summary>
        public static SolidColorBrush Brush(Color c) { var b = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)); b.Freeze(); return b; }
    }
}
