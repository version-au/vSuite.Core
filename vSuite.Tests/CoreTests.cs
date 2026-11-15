using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using vSuite;
using Xunit;

// App is static (one app per process), so the tests run one at a time
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace vSuite.Tests
{
    public class CoreTests
    {
        static string Scratch()
        {
            string d = Path.Combine(Path.GetTempPath(), "vsuite-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        // ---------------- the updater ----------------

        [Theory]
        [InlineData("v0.1.21", "0.1.21")]
        [InlineData("0.1.21-beta2", "0.1.21")]
        [InlineData("vXplorer 0.1.21", "0.1.21")]
        [InlineData("v2", "2.0.0")]
        [InlineData("v0.38.166", "0.38.166")]
        public void ParseVersion_reads_tags(string tag, string expected)
        {
            Assert.Equal(Version.Parse(expected), Updater.ParseVersion(tag));
        }

        [Theory]
        [InlineData("nonsense")]
        [InlineData("")]
        [InlineData(null)]
        public void ParseVersion_rejects_non_versions(string tag)
        {
            Assert.Null(Updater.ParseVersion(tag));
        }

        [Fact]
        public void FirstHex64_finds_the_hash_in_any_format()
        {
            string h = new string('a', 32) + new string('B', 32);
            Assert.Equal(h.ToLowerInvariant(), Updater.FirstHex64(h + "  vXplorer.exe"));
            Assert.Equal(h.ToLowerInvariant(), Updater.FirstHex64("SHA256 hash of file:\r\n" + string.Join(" ", Enumerable.Range(0, 32).Select(i => h.Substring(i * 2, 2))) + "\r\nCertUtil: done"));
            Assert.Null(Updater.FirstHex64("no hash here"));
            Assert.Null(Updater.FirstHex64(h + "0"));   // 65 hex digits is not a SHA-256
        }

        const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        /// <summary>A latest.json as build.cmd writes it.</summary>
        static string Latest(string app, string version, string install, string installer, string extra = "", params string[] files)
        {
            string list = string.Join(",", files.Select(f => "\"" + f + "\":{\"size\":123456,\"sha256\":\"" + Sha.ToUpperInvariant() + "\"}"));
            return "{\"app\":\"" + app + "\",\"version\":\"" + version + "\",\"published\":\"2026-11-07T10:00:00Z\",\"notes\":\"## " + version + "\\n- a change\"," +
                   (install != null ? "\"install\":\"" + install + "\"," : "") + (installer != null ? "\"installer\":\"" + installer + "\"," : "") + extra +
                   "\"files\":{" + list + "}}";
        }

        static UpdateInfo Rel(string json, bool pre = false)
        {
            UpdateInfo u = Updater.ParseRelease(json, "https://site/" + (pre ? "app/pre/" : "app/"));
            u.PreRelease = pre;
            return u;
        }

        [Fact]
        public void ParseRelease_reads_latest_json()
        {
            App.Init("vShell", "VSHELL", "https://site");
            UpdateInfo u = Rel(Latest("vShell", "0.38.190", "vShell.exe", "vShell-setup.exe", "\"plugin_api\":2,", "vShell.exe", "vShell-setup.exe", "sub/evil.exe"));
            Assert.Equal(new Version(0, 38, 190), u.Version);
            Assert.Equal("v0.38.190", u.Tag);
            Assert.Equal("vShell 0.38.190", u.Title);
            Assert.Equal("https://site/app/vShell.exe", u.FileUrl);
            Assert.Equal("vShell.exe", u.FileName);
            Assert.Equal(Sha, u.Sha256);                              // lower case
            Assert.Equal(123456, u.FileSize);
            Assert.Equal("https://site/app/vShell-setup.exe", u.Installer.Url);
            Assert.Equal("2", u.Fields["plugin_api"]);
            Assert.Equal("https://site/app/", u.Folder);
            Assert.Equal(2, u.Files.Count);                           // nothing outside its folder
            Assert.Equal(new DateTime(2026, 11, 7, 10, 0, 0), u.Published);
            Assert.Contains("a change", u.Notes);
            Assert.Throws<InvalidOperationException>(() => Updater.ParseRelease("{\"app\":\"vShell\"}", "https://site/app/"));
            Assert.Equal("https://site/", Updater.ReleasesPage);
            Assert.Equal("https://site/vshell/pre/", Updater.SiteFolder("vShell", true));
        }

        [Fact]
        public void PickNewest_one_exe_app_takes_the_exe_with_its_checksum()
        {
            App.Init("vXplorer", "VXPLORER", "https://site");
            var releases = new[]
            {
                Rel(Latest("vXplorer", "0.1.40", "vXplorer.exe", null, "", "vXplorer.exe"), true),
                Rel(Latest("vXplorer", "0.1.35", "vXplorer.exe", null, "", "vXplorer.exe")),
            };
            UpdateInfo u = Updater.PickNewest(releases, new Version(0, 1, 33));
            Assert.Equal(new Version(0, 1, 40), u.Version);
            Assert.True(u.PreRelease);
            Assert.Equal(new Version(0, 1, 35), Updater.PickNewest(new[] { releases[1] }, new Version(0, 1, 33)).Version);
            Assert.Null(Updater.PickNewest(new[] { releases[1] }, new Version(0, 1, 35)));   // up to date
            // nothing to install: no install file, or one without a checksum, or another app's file
            Assert.Null(Updater.PickNewest(new[] { Rel(Latest("vXplorer", "0.1.50", null, null, "", "vXplorer.exe")) }, new Version(0, 1, 33)));
            Assert.Null(Updater.PickNewest(new[] { Rel(Latest("vXplorer", "0.1.50", "vView.exe", null, "", "vView.exe")) }, new Version(0, 1, 33)));
            UpdateInfo noHash = Rel("{\"version\":\"0.1.50\",\"install\":\"vXplorer.exe\",\"files\":{\"vXplorer.exe\":{\"size\":5}}}");
            Assert.Null(Updater.PickNewest(new[] { noHash }, new Version(0, 1, 33)));
        }

        /// <summary>A tiny update site on localhost: path -> (status, body).</summary>
        static System.Net.HttpListener Site(out string url, System.Collections.Generic.Dictionary<string, (int, string)> pages)
        {
            for (int tries = 0; ; tries++)
            {
                int port = new Random().Next(20000, 60000);
                var l = new System.Net.HttpListener();
                l.Prefixes.Add("http://localhost:" + port + "/");
                try { l.Start(); }
                catch (System.Net.HttpListenerException) when (tries < 5) { continue; }
                url = "http://localhost:" + port;
                System.Threading.Tasks.Task.Run(() =>
                {
                    while (l.IsListening)
                    {
                        System.Net.HttpListenerContext c;
                        try { c = l.GetContext(); } catch { return; }
                        (int, string) page;
                        if (!pages.TryGetValue(c.Request.Url.AbsolutePath, out page)) page = (404, "");
                        c.Response.StatusCode = page.Item1;
                        byte[] b = System.Text.Encoding.UTF8.GetBytes(page.Item2);
                        c.Response.OutputStream.Write(b, 0, b.Length);
                        c.Response.Close();
                    }
                });
                return l;
            }
        }

        [Fact]
        public async System.Threading.Tasks.Task ReleasesAsync_reads_the_release_and_the_pre_release_from_the_site()
        {
            App.Init("vXplorer", "VXTEST", "https://unused");
            var pages = new System.Collections.Generic.Dictionary<string, (int, string)>
            {
                ["/vxplorer/latest.json"] = (200, Latest("vXplorer", "0.1.48", "vXplorer.exe", "vXplorer-setup.exe", "", "vXplorer.exe", "vXplorer-setup.exe")),
            };
            string url;
            using (System.Net.HttpListener l = Site(out url, pages))
            {
                Environment.SetEnvironmentVariable("VXTEST_UPDATE_SITE", url + "/");
                try
                {
                    var list = (await Updater.ReleasesAsync("vXplorer", true));   // no pre/ folder: just the release
                    Assert.Single(list);
                    Assert.Equal(url + "/vxplorer/vXplorer.exe", list[0].FileUrl);
                    Assert.False(list[0].PreRelease);

                    pages["/vxplorer/pre/latest.json"] = (200, Latest("vXplorer", "0.1.49", "vXplorer.exe", null, "", "vXplorer.exe"));
                    list = (await Updater.ReleasesAsync("vXplorer", true));
                    Assert.Equal(2, list.Count);
                    Assert.Equal(new Version(0, 1, 49), list[0].Version);  // newest first
                    Assert.True(list[0].PreRelease);
                    Assert.Equal(url + "/vxplorer/pre/vXplorer.exe", list[0].FileUrl);
                    Assert.Single((await Updater.ReleasesAsync("vXplorer", false)));
                    Assert.Equal(new Version(0, 1, 49), (await Updater.NewestReleaseAsync("vXplorer", true)));
                    Assert.Equal(new Version(0, 1, 48), (await SuiteApps.FindInstallerAsync("vXplorer", true)).Version);

                    // a broken pre-release folder doesn't stop the release; a broken release does
                    pages["/vxplorer/pre/latest.json"] = (500, "");
                    Assert.Single((await Updater.ReleasesAsync("vXplorer", true)));
                    pages["/vxplorer/latest.json"] = (503, "");
                    var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Updater.ReleasesAsync("vXplorer", false));
                    Assert.Contains("503", ex.Message);
                    // nothing on the site for the app: a check says so
                    Assert.Empty((await Updater.ReleasesAsync("vView", true)));
                }
                finally { Environment.SetEnvironmentVariable("VXTEST_UPDATE_SITE", null); l.Stop(); }
            }
        }

        [Fact]
        public void ChangesBetween_takes_every_version_since_yours()
        {
            string log = "# vShell changelog\r\n\r\nNewest first.\r\n\r\n## 0.38.191 (Build 4)\r\n- Three\r\n\r\n## 0.38.190\r\n- Two\r\n- Two b\r\n\r\n## 0.38.189\r\n- One\r\n";
            string c = Updater.ChangesBetween(log, new Version(0, 38, 189), new Version(0, 38, 191));
            Assert.Equal("## 0.38.191 (Build 4)\n- Three\n\n## 0.38.190\n- Two\n- Two b", c);
            Assert.Equal("## 0.38.190\n- Two\n- Two b", Updater.ChangesBetween(log, new Version(0, 38, 189), new Version(0, 38, 190)));   // not past the release
            Assert.Equal("", Updater.ChangesBetween(log, new Version(0, 38, 191), new Version(0, 38, 191)));
            Assert.Equal("## 0.38.191 (Build 4)\n- Three", Updater.ChangesBetween(log, new Version(0, 1, 0), new Version(0, 38, 191), 1));
        }

        [Fact]
        public void PickNewest_folder_app_takes_the_zip()
        {
            App.Init("vView", "VVIEW", "https://site", InstallStyle.Folder, "libvlc");
            UpdateInfo u = Updater.PickNewest(new[] { Rel(Latest("vView", "0.1.17", "vView.zip", "vView-setup.exe", "", "vView.zip", "vView-setup.exe")) }, new Version(0, 1, 2));
            Assert.Equal("https://site/app/vView.zip", u.FileUrl);
            Assert.Equal(Sha, u.Sha256);
        }

        [Fact]
        public void Installer_names_with_and_without_the_version()
        {
            Assert.True(SuiteApps.IsInstallerName("vShell", "vShell-setup.exe"));
            Assert.True(SuiteApps.IsInstallerName("vShell", "vShell-setup-0.38.188.exe"));
            Assert.True(SuiteApps.IsInstallerName("vshell", "VSHELL-SETUP.EXE"));
            Assert.False(SuiteApps.IsInstallerName("vShell", "vShell.exe"));
            Assert.False(SuiteApps.IsInstallerName("vShell", "vShell-setup.exe.sha256"));
            Assert.False(SuiteApps.IsInstallerName("vView", "vShell-setup.exe"));
            Assert.False(SuiteApps.IsInstallerName("vShell", "vShell-setupx.exe"));
        }

        [Fact]
        public void PickInstaller_takes_the_newest_release_with_an_installer()
        {
            App.Init("vShell", "VSHELL", "https://site");
            UpdateInfo pre = Rel(Latest("vView", "0.1.18", "vView.zip", "vView-setup.exe", "", "vView.zip", "vView-setup.exe"), true);
            UpdateInfo stable = Rel(Latest("vView", "0.1.17", "vView.zip", "vView-setup.exe", "", "vView.zip", "vView-setup.exe"));
            SuiteApps.Installer i = SuiteApps.PickInstaller(new[] { stable }, "vView");
            Assert.Equal(new Version(0, 1, 17), i.Version);
            Assert.Equal("https://site/app/vView-setup.exe", i.Url);
            Assert.Equal("vView-setup.exe", i.FileName);
            Assert.Equal(Sha, i.Sha256);
            Assert.Equal(new Version(0, 1, 18), SuiteApps.PickInstaller(new[] { pre, stable }, "vView").Version);
            Assert.Null(SuiteApps.PickInstaller(new[] { Rel(Latest("vView", "0.1.17", "vView.zip", null, "", "vView.zip")) }, "vView"));
            Assert.Null(SuiteApps.PickInstaller(new[] { stable }, "vXplorer"));   // another app's installer
        }

        [Fact]
        public void A_build_folder_never_updates_itself()
        {
            // the test host isn't Name.exe, so neither style finds an install to update
            App.Init("vXplorer", "VXPLORER", "r");
            Assert.Null(Updater.ExePath);
            Assert.NotNull(Updater.CantUpdateReason);
            App.Init("vView", "VVIEW", "r", InstallStyle.Folder, "libvlc");
            Assert.Null(Updater.InstallDir);
            Assert.NotNull(Updater.CantUpdateReason);
        }

        // ---------------- reading vShell's theme ----------------

        static VShellColours Parse(string json)
        {
            using (var d = JsonDocument.Parse(json)) return VShellThemeSource.FromJson(d.RootElement);
        }

        [Fact]
        public void Theme_reads_vShells_saved_names()
        {
            VShellColours c = Parse("{\"Background\":\"#CC101010\",\"Accent\":\"#FF00FF00\",\"Spare\":\"#FFFF0000\",\"BgStyle\":\"Gradient\",\"GradientAngle\":-90," +
                                    "\"BlurMode\":\"Acrylic\",\"WidgetStyle\":{\"CornerRadius\":40,\"ShowBorder\":false,\"BorderOpacity\":50}}");
            Assert.Equal("#CC101010", c.Background);
            Assert.Equal("#FF00FF00", c.MainAccent);
            Assert.Equal("#FFFF0000", c.SecondaryAccent);
            Assert.True(c.Gradient);
            Assert.Equal(270, c.GradientAngle);
            Assert.Equal("Acrylic", c.BlurMode);
            Assert.Equal(24, c.CornerRadius);          // capped like vShell
            Assert.Equal(0, c.BorderThickness);        // no border
            Assert.Equal(50, c.BorderOpacity);
        }

        [Fact]
        public void Theme_defaults_when_values_are_missing_or_odd()
        {
            VShellColours c = Parse("{\"BlurMode\":\"Sparkly\"}");
            Assert.Equal(new VShellColours().Signature, c.Signature.Replace("Sparkly", "Off"));
            Assert.Equal("Off", c.BlurMode);
        }

        [Theory]
        [InlineData("{\"WallpaperTheme\":true}")]
        [InlineData("{\"AccentFromWallpaper\":true}")]
        [InlineData("{\"BackgroundFromWallpaper\":true}")]
        public void Theme_sees_the_wallpaper_theme_and_its_older_names(string json)
        {
            Assert.True(Parse(json).WallpaperTheme);
        }

        // ---------------- Wallpaper Theme colours ----------------

        static string Picture(params (Color colour, int share)[] bands)
        {
            int w = 200, h = 100, total = bands.Sum(b => b.share);
            var px = new byte[w * h * 4];
            int x0 = 0;
            foreach (var b in bands)
            {
                int bw = w * b.share / total;
                for (int y = 0; y < h; y++)
                    for (int x = x0; x < Math.Min(w, x0 + bw); x++)
                    {
                        int i = (y * w + x) * 4;
                        px[i] = b.colour.B; px[i + 1] = b.colour.G; px[i + 2] = b.colour.R; px[i + 3] = 255;
                    }
                x0 += bw;
            }
            for (int y = 0; y < h; y++)   // rounding leftovers: the first colour
                for (int x = x0; x < w; x++) { int i = (y * w + x) * 4; px[i] = bands[0].colour.B; px[i + 1] = bands[0].colour.G; px[i + 2] = bands[0].colour.R; px[i + 3] = 255; }
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
            string path = Path.Combine(Scratch(), "wall.png");
            using (var fs = File.Create(path))
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                enc.Save(fs);
            }
            return path;
        }

        [Fact]
        public void Wallpaper_theme_is_readable_and_takes_the_pictures_colours()
        {
            App.Init("vSuiteTest", "VSUITETEST", "r");
            Environment.SetEnvironmentVariable("VSUITETEST_DATA_DIR", Scratch());
            string pic = Picture((Color.FromRgb(0x10, 0x20, 0x40), 70), (Color.FromRgb(0xE0, 0x60, 0x20), 20), (Color.FromRgb(0x30, 0xC0, 0x60), 10));
            WallpaperPalette p;
            Assert.True(WallpaperColor.TryGetPalette(pic, out p));
            Assert.True(WallpaperColor.Contrast(p.Foreground, p.Background) >= 4.5, "text must be readable on the background");
            Assert.True(WallpaperColor.Contrast(p.MainAccent, p.Background) >= 3.0);
            Assert.True(WallpaperColor.Contrast(p.SecondaryAccent, p.Background) >= 3.0);
            Assert.True(p.Background.B > p.Background.R, "a dark blue picture gives a dark blue background");
            Assert.True(p.MainAccent.R > p.MainAccent.B, "the orange band is the main accent");

            // the same picture always gives the same theme
            WallpaperPalette again;
            Assert.True(WallpaperColor.TryGetPalette(Picture((Color.FromRgb(0x10, 0x20, 0x40), 70), (Color.FromRgb(0xE0, 0x60, 0x20), 20), (Color.FromRgb(0x30, 0xC0, 0x60), 10)), out again));
            Assert.Equal(p.Background, again.Background);
            Assert.Equal(p.MainAccent, again.MainAccent);
        }

        [Fact]
        public void Wallpaper_theme_keeps_the_saved_background_opacity()
        {
            VShellColours c = Parse("{\"Background\":\"#99101010\",\"Background2\":\"#80202020\",\"WallpaperTheme\":true}");
            var p = new WallpaperPalette
            {
                Background = Color.FromRgb(1, 2, 3), Background2 = Color.FromRgb(4, 5, 6), Foreground = Colors.White,
                MainAccent = Colors.Orange, SecondaryAccent = Colors.Green
            };
            typeof(VShellColours).GetMethod("UseWallpaper", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(c, new object[] { p });
            Assert.Equal("#99010203", c.Background);
            Assert.Equal("#80040506", c.Background2);
            Assert.Equal("#FFFFFFFF", c.Foreground);
        }

        // ---------------- the log, folders, times ----------------

        [Fact]
        public void Log_counts_repeated_lines_and_lives_in_the_apps_folder()
        {
            string dir = Scratch();
            App.Init("vLogTest", "VLOGTEST", "r");
            Environment.SetEnvironmentVariable("VLOGTEST_DATA_DIR", dir);
            Assert.Equal(dir, Log.Folder);
            Log.Write("first");
            for (int i = 0; i < 5; i++) Log.Write("again");
            Log.Write("last");
            Log.Flush();
            string[] lines = File.ReadAllLines(Path.Combine(dir, "vlogtest.log"));
            Assert.Equal(4, lines.Length);
            Assert.EndsWith("first", lines[0]);
            Assert.EndsWith("again", lines[1]);
            Assert.Contains("repeated 4 more times", lines[2]);
            Assert.EndsWith("last", lines[3]);
        }

        [Fact]
        public void Times_are_twelve_hour()
        {
            string s = TimeText.Stamp(new DateTime(2026, 11, 3, 14, 5, 0));
            Assert.EndsWith("2:05 PM", s);
            Assert.Equal("", TimeText.Stamp(DateTime.MinValue));
        }

        [Fact]
        public void Instance_ids_differ_per_settings_folder()
        {
            App.Init("vIdTest", "VIDTEST", "r");
            Environment.SetEnvironmentVariable("VIDTEST_DATA_DIR", @"C:\a");
            int a = Instance.Id;
            Environment.SetEnvironmentVariable("VIDTEST_DATA_DIR", @"C:\b");
            Assert.NotEqual(a, Instance.Id);
            Assert.Equal("vIdTest.Instance", Instance.Prop);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern bool SetProp(IntPtr h, string name, IntPtr value);

        [Fact]
        public void Tray_menu_opens_dressed_in_the_theme_and_closes()
        {
            App.Init("vTrayMenuTest", "VTRAYMENUTEST", "r");
            Exception failed = null;
            var t = new System.Threading.Thread(() =>
            {
                try
                {
                    using (var tray = new vSuite.Ui.TrayIcon(new string('x', 200)))
                    {
                        Assert.Equal(127, tray.Tip.Length);
                        tray.Menu = () => { var m = new System.Windows.Controls.ContextMenu(); m.Items.Add(new System.Windows.Controls.MenuItem { Header = "Open" }); return m; };
                        tray.MenuTheme = () => vSuite.Ui.Theme.From(new VShellColours());
                        tray.ShowMenu();
                        Assert.NotNull(tray.OpenMenu);
                        Assert.True(tray.OpenMenu.Resources.Contains("vx.Fg"));          // the theme's brushes on the menu itself
                        tray.CloseMenu();
                        Assert.Null(tray.OpenMenu);
                        tray.SetIcon(new System.Windows.Media.Imaging.WriteableBitmap(32, 32, 96, 96, System.Windows.Media.PixelFormats.Pbgra32, null));
                    }
                }
                catch (Exception ex) { failed = ex; }
            });
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.Start();
            t.Join(15000);
            if (failed != null) throw failed;
        }

        [Fact]
        public void Instance_hands_off_to_a_window_hidden_in_the_tray_only()
        {
            App.Init("vTrayTest", "VTRAYTEST", "r");
            Environment.SetEnvironmentVariable("VTRAYTEST_DATA_DIR", Scratch());
            IntPtr hwnd = IntPtr.Zero;
            string got = null;
            var ready = new System.Threading.ManualResetEventSlim();
            System.Windows.Threading.Dispatcher disp = null;
            var t = new System.Threading.Thread(() =>
            {
                // a hidden window (never shown) marked as the app's, as WindowIcons marks the main window
                var src = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("tray test") { WindowStyle = 0 });
                src.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
                {
                    if (msg != 0x4A) return IntPtr.Zero;
                    got = Instance.Received(l);
                    handled = true;
                    return new IntPtr(1);
                });
                hwnd = src.Handle;
                SetProp(hwnd, Instance.Prop, new IntPtr(Instance.Id));
                disp = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                ready.Set();
                System.Windows.Threading.Dispatcher.Run();
            });
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
            Assert.True(ready.Wait(5000));
            try
            {
                Assert.False(Instance.SendTo("vTrayTest", Log.Folder, "hidden"));     // hidden, not in the tray: not found
                Instance.InTray(hwnd, true);
                Assert.True(Instance.SendTo("vTrayTest", Log.Folder, "from the tray"));
                Assert.Equal("from the tray", got);
                Instance.InTray(hwnd, false);
                Assert.False(Instance.SendTo("vTrayTest", Log.Folder, "again"));
            }
            finally { disp.InvokeShutdown(); }
        }
    }
}
