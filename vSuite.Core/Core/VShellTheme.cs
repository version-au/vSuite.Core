using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows.Media;

namespace vSuite
{
    /// <summary>The parts of a vShell theme the apps use, as vShell saves them (colour strings, no WPF types).</summary>
    public sealed class VShellColours
    {
        // vShell's own defaults (src/Core/Settings.cs) for anything missing
        public string Background = "#E61E1E2E";
        public string Background2 = "#E6313244";
        public bool Gradient;
        public int GradientAngle = 90;
        public string Foreground = "#FFCDD6F4";
        public string MainAccent = "#FF89B4FA";
        public string SecondaryAccent = "#FFF5C2E7";
        /// <summary>"Off", "Blur" or "Acrylic" (vShell calls Acrylic "Frosted").</summary>
        public string BlurMode = "Off";
        /// <summary>
        /// vShell's Wallpaper Theme is on: the colours above have already been replaced by the ones worked out from the main monitor's
        /// wallpaper (the background keeps the opacity vShell saved), the same way vShell does it.
        /// </summary>
        public bool WallpaperTheme;
        // the widget style: the apps' panels are drawn like vShell widgets
        public double CornerRadius = 8;
        public bool ShowBorder = true;
        public double BorderThickness = 1;
        public int BorderOpacity = 65;
        /// <summary>Empty = the Main Accent; "@main" / "@secondary" / "@text" or a colour.</summary>
        public string BorderColor = "";

        /// <summary>Everything above in one string, to tell whether a re-read of settings.json changed the look.</summary>
        public string Signature
        {
            get
            {
                return string.Join("|", Background, Background2, Gradient, GradientAngle, Foreground, MainAccent, SecondaryAccent, BlurMode,
                                   WallpaperTheme, CornerRadius, ShowBorder, BorderThickness, BorderOpacity, BorderColor);
            }
        }

        /// <summary>Wallpaper Theme: the wallpaper's colours in place of the saved ones (the backgrounds keep their opacity).</summary>
        internal void UseWallpaper(WallpaperPalette p)
        {
            byte a1 = Alpha(Background), a2 = Alpha(Background2);
            Background = WallpaperColor.Hex(Color.FromArgb(a1, p.Background.R, p.Background.G, p.Background.B));
            Background2 = WallpaperColor.Hex(Color.FromArgb(a2, p.Background2.R, p.Background2.G, p.Background2.B));
            Foreground = WallpaperColor.Hex(p.Foreground);
            MainAccent = WallpaperColor.Hex(p.MainAccent);
            SecondaryAccent = WallpaperColor.Hex(p.SecondaryAccent);
        }

        static byte Alpha(string colour)
        {
            try
            {
                object o = ColorConverter.ConvertFromString(colour);
                if (o is Color) return ((Color)o).A;
            }
            catch { }
            return 0xE6;
        }
    }

    /// <summary>
    /// Reads the theme from vShell's settings (%APPDATA%\vShell\settings.json) and watches the file, so a theme change in vShell
    /// re-colours the app straight away. Changed is raised on a worker thread, only when the look actually changed.
    ///   - vShell installed (or its folder made) after the app started: picked up within half a minute, no restart needed.
    ///   - Wallpaper Theme on: the colours come from the wallpaper (WallpaperColor, as vShell), and a new wallpaper is noticed within
    ///     about 15 seconds.
    /// </summary>
    public sealed class VShellThemeSource : IDisposable
    {
        /// <summary>%APPDATA%\vShell, or the folder in PREFIX_VSHELL_DIR (for testing themes without touching vShell's real settings).</summary>
        public static string SettingsFolder
        {
            get
            {
                return App.Env("VSHELL_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vShell");
            }
        }

        public static string SettingsFile { get { return Path.Combine(SettingsFolder, "settings.json"); } }

        public VShellColours Current { get; private set; }
        /// <summary>vShell's settings were found (otherwise Current is vShell's default theme).</summary>
        public bool Found { get; private set; }
        public event Action Changed;

        FileSystemWatcher _watcher;
        readonly Timer _debounce, _poll;
        readonly object _gate = new object();
        bool _disposed;

        public VShellThemeSource()
        {
            bool found;
            Current = Read(out found) ?? new VShellColours();
            Found = found;
            _debounce = new Timer(_ => Reload(), null, Timeout.Infinite, Timeout.Infinite);
            Watch();
            // now and then: vShell's folder appearing, and (Wallpaper Theme) a new wallpaper
            _poll = new Timer(_ => Poll(), null, 15000, 15000);
        }

        void Watch()
        {
            lock (_gate)
            {
                if (_watcher != null || _disposed) return;
                try
                {
                    if (!Directory.Exists(SettingsFolder)) return;
                    _watcher = new FileSystemWatcher(SettingsFolder)
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                        IncludeSubdirectories = false
                    };
                    _watcher.Changed += OnFileEvent;
                    _watcher.Created += OnFileEvent;
                    _watcher.Renamed += OnFileEvent;
                    _watcher.EnableRaisingEvents = true;
                }
                catch (Exception ex) { Log.Write("Theme watch: " + ex.Message); }
            }
        }

        void Poll()
        {
            if (_watcher == null && Directory.Exists(SettingsFolder))
            {
                Log.Write("vShell's settings folder appeared - following its theme");
                Watch();
                Reload();
                return;
            }
            if (Current.WallpaperTheme && WallpaperColor.Changed()) Reload();
        }

        void OnFileEvent(object sender, FileSystemEventArgs e)
        {
            if (string.Equals(e.Name, "settings.json", StringComparison.OrdinalIgnoreCase))
                _debounce.Change(400, Timeout.Infinite);   // vShell can write several times in a row
        }

        void Reload()
        {
            bool found;
            VShellColours c = Read(out found);
            if (c == null) return;                         // half-written or unreadable: keep what we have
            Found = found;
            if (c.Signature == Current.Signature) return;  // vShell saved something else (a bar moved …)
            Current = c;
            Log.Write("vShell theme changed");
            var h = Changed;
            if (h != null) h();
        }

        /// <summary>vShell's theme, or vShell's defaults when it isn't installed; null when the file couldn't be read.</summary>
        static VShellColours Read(out bool found)
        {
            found = false;
            if (!File.Exists(SettingsFile)) return new VShellColours();
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    string text;
                    using (var fs = new FileStream(SettingsFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var sr = new StreamReader(fs))
                        text = sr.ReadToEnd();
                    using (var doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }))
                    {
                        found = true;
                        VShellColours c = FromJson(doc.RootElement);
                        if (c.WallpaperTheme)
                        {
                            WallpaperPalette p;
                            if (WallpaperColor.TryGetPalette(out p)) c.UseWallpaper(p);
                        }
                        return c;
                    }
                }
                catch (IOException) { Thread.Sleep(150); }
                catch (JsonException) { Thread.Sleep(150); }
                catch (Exception ex) { Log.Write("Theme read: " + ex.Message); return null; }
            }
            return null;
        }

        /// <summary>The theme from vShell's settings.json (public for tests).</summary>
        public static VShellColours FromJson(JsonElement root)
        {
            var c = new VShellColours();
            c.Background = Str(root, c.Background, "Background");
            c.Background2 = Str(root, c.Background2, "Background2");
            c.Gradient = Str(root, "Solid", "BgStyle") == "Gradient";
            c.GradientAngle = (int)Num(root, c.GradientAngle, "GradientAngle");
            c.GradientAngle = ((c.GradientAngle % 360) + 360) % 360;
            c.Foreground = Str(root, c.Foreground, "Foreground");
            c.MainAccent = Str(root, c.MainAccent, "Accent", "MainAccent");             // saved as "Accent" (its name before the rename)
            c.SecondaryAccent = Str(root, c.SecondaryAccent, "Spare", "SecondaryAccent"); // saved as "Spare"
            c.BlurMode = Str(root, "Off", "BlurMode");
            if (c.BlurMode != "Blur" && c.BlurMode != "Acrylic") c.BlurMode = "Off";
            // older vShell settings: "accent from the wallpaper" / "tint the background" turn the Wallpaper Theme on, as in vShell
            c.WallpaperTheme = Bool(root, false, "WallpaperTheme") || Bool(root, false, "AccentFromWallpaper") || Bool(root, false, "BackgroundFromWallpaper");

            JsonElement ws;
            if (root.TryGetProperty("WidgetStyle", out ws) && ws.ValueKind == JsonValueKind.Object)
            {
                c.CornerRadius = Math.Max(0, Math.Min(24, Num(ws, c.CornerRadius, "CornerRadius")));
                c.ShowBorder = Bool(ws, c.ShowBorder, "ShowBorder");
                c.BorderThickness = Math.Max(0, Math.Min(4, Num(ws, c.BorderThickness, "BorderThickness")));
                c.BorderOpacity = (int)Math.Max(0, Math.Min(100, Num(ws, c.BorderOpacity, "BorderOpacity")));
                c.BorderColor = Str(ws, "", "BorderColor");
            }
            if (!c.ShowBorder) c.BorderThickness = 0;
            return c;
        }

        static string Str(JsonElement o, string fallback, params string[] names)
        {
            foreach (string n in names)
            {
                JsonElement v;
                if (o.TryGetProperty(n, out v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()))
                    return v.GetString().Trim();
            }
            return fallback;
        }

        static double Num(JsonElement o, double fallback, string name)
        {
            JsonElement v;
            double d;
            if (o.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out d)) return d;
            return fallback;
        }

        static bool Bool(JsonElement o, bool fallback, string name)
        {
            JsonElement v;
            if (o.TryGetProperty(name, out v))
            {
                if (v.ValueKind == JsonValueKind.True) return true;
                if (v.ValueKind == JsonValueKind.False) return false;
            }
            return fallback;
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                if (_watcher != null) _watcher.Dispose();
            }
            _debounce.Dispose();
            _poll.Dispose();
        }
    }
}
