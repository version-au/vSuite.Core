using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace vSuite
{
    /// <summary>A file of a release on the update site: where it is, its size and its SHA-256 (from latest.json).</summary>
    public sealed class SiteFile
    {
        public string Name = "", Url = "", Sha256 = "";
        public long Size;
    }

    /// <summary>A release on the update site (an app's latest.json).</summary>
    public sealed class UpdateInfo
    {
        public Version Version;
        public string Tag = "", Title = "", Notes = "", PageUrl = "";
        /// <summary>From the pre-release folder (Name/pre/).</summary>
        public bool PreRelease;
        public DateTime Published;
        /// <summary>The file to install: Name.exe (one-exe apps) or Name.zip (folder apps), its SHA-256 and its size.</summary>
        public string FileName = "", FileUrl = "", Sha256 = "";
        public long FileSize;
        /// <summary>The app's installer (Name-setup.exe), or null.</summary>
        public SiteFile Installer;
        /// <summary>Every file latest.json lists, by name.</summary>
        public Dictionary<string, SiteFile> Files = new Dictionary<string, SiteFile>(StringComparer.OrdinalIgnoreCase);
        /// <summary>The site folder the release is in (ends with "/") - vShell's plugins are under it.</summary>
        public string Folder = "";
        /// <summary>latest.json's other plain values, as text ("plugin_api" -> "2").</summary>
        public Dictionary<string, string> Fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Updates from the suite's web site (App.UpdateSite), for every app in the suite. Only each app's newest release is there:
    ///
    ///   site/name/latest.json        the release (version, notes, and each file's size and SHA-256 - build.cmd writes it)
    ///   site/name/Name.exe           (or Name.zip for a folder app) and Name-setup.exe
    ///   site/name/pre/...            the same for the newest pre-release (Include pre-releases)
    ///
    /// latest.json: { "app": "vShell", "version": "0.38.190", "published": "2026-11-07T10:00:00Z", "notes": "markdown",
    ///                "install": "vShell.exe", "installer": "vShell-setup.exe", "files": { "vShell.exe": { "size": 1, "sha256": "…" }, … } }
    /// Any other plain value in it (vShell's "plugin_api") is kept in UpdateInfo.Fields.
    ///
    /// One-exe apps (vXplorer, vShell): the release's file is Name.exe. The new exe is downloaded beside the running one and
    /// checked, the running exe is renamed to Name.old.exe (Windows allows renaming a running exe) and the new one put in its place; the
    /// OLD copy starts as a watchdog (--update-watchdog, see RunWatchdog) and the app closes. The watchdog starts the new exe and waits
    /// for it to say it started (update-ok); if it quits first, the old exe goes back. The exe keeps its path, so shortcuts and the
    /// installer's entries keep working.
    ///
    /// Folder apps (vView): the release's file is a zip (Name.zip). The zip is unpacked beside the install as "Name.update", then a
    /// small PowerShell watchdog waits for the app to close, renames the install to "Name.old" and "Name.update" into its place (copying
    /// the uninstaller across), starts the new exe and waits for update-ok - else puts the old folder back.
    ///
    /// Only an installed / released copy updates itself, never a build folder (bin\...). PREFIX_UPDATE_SITE (tests only) points the check
    /// at another site.
    /// </summary>
    public static class Updater
    {
        const string PendingFile = "update-pending.txt", OkFile = "update-ok.txt", FailedFile = "update-failed.txt";

        public static Version Current
        {
            get
            {
                var asm = System.Reflection.Assembly.GetEntryAssembly() ?? typeof(Updater).Assembly;
                Version v = asm.GetName().Version ?? new Version(0, 0, 0);
                return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            }
        }

        public static string CurrentText { get { return Text(Current); } }

        public static string Text(Version v) { return v.Major + "." + v.Minor + "." + v.Build; }

        static string ExeName { get { return App.ExeName; } }

        /// <summary>One-exe apps: the running exe when it's the published single file (installed, or dist\Name.exe); null otherwise.</summary>
        public static string ExePath
        {
            get
            {
                if (App.Install != InstallStyle.SingleExe) return null;
                string p = Environment.ProcessPath;
                if (string.IsNullOrEmpty(p) || !Path.GetFileName(p).Equals(ExeName, StringComparison.OrdinalIgnoreCase)) return null;
                if (File.Exists(Path.Combine(Path.GetDirectoryName(p), App.Name + ".dll"))) return null;   // a build folder, not the single file
                return p;
            }
        }

        /// <summary>Folder apps: the folder the app runs from, when it's an installed / released copy; null otherwise.</summary>
        public static string InstallDir
        {
            get
            {
                if (App.Install != InstallStyle.Folder) return null;
                string p = Environment.ProcessPath;
                if (string.IsNullOrEmpty(p) || !Path.GetFileName(p).Equals(ExeName, StringComparison.OrdinalIgnoreCase)) return null;
                string dir = Path.GetDirectoryName(p);
                if (dir.IndexOf("\\bin\\", StringComparison.OrdinalIgnoreCase) >= 0) return null;   // a build folder
                if (!string.IsNullOrEmpty(App.FolderMarker) && !Directory.Exists(Path.Combine(dir, App.FolderMarker))) return null;
                return dir;
            }
        }

        /// <summary>Why this copy can't update itself, or null when it can.</summary>
        public static string CantUpdateReason
        {
            get
            {
                if (App.Install == InstallStyle.SingleExe ? ExePath != null : InstallDir != null) return null;
                return "This copy of " + App.Name + " runs from a build folder (" + Path.GetDirectoryName(Environment.ProcessPath ?? "") + "), so it " +
                       "can't update itself - updates work for the installed " + App.Name + " (or the one build.cmd makes in dist).";
            }
        }

        /// <summary>true while an update is being finished (this run was just started by the watchdog).</summary>
        public static bool IsUpdateRestart { get { return PendingFresh(); } }

        /// <summary>The suite's web site (What's new, and where a release page would be).</summary>
        public static string ReleasesPage { get { return App.UpdateSite + "/"; } }

        /// <summary>An app's folder on the update site ("https://vsuite.vrsion.com/vshell/", or ".../vshell/pre/"), ending with "/".</summary>
        public static string SiteFolder(string appName, bool pre)
        {
            return App.UpdateSite + "/" + appName.ToLowerInvariant() + "/" + (pre ? "pre/" : "");
        }

        /// <summary>"v0.1.21", "0.1.21-beta2", "vXplorer 0.1.21" -> 0.1.21 (or null).</summary>
        public static Version ParseVersion(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;
            string t = tag.Trim();
            int start = 0;
            while (start < t.Length && !char.IsDigit(t[start])) start++;
            int end = start;
            while (end < t.Length && (char.IsDigit(t[end]) || t[end] == '.')) end++;
            string core = t.Substring(start, end - start).Trim('.');
            Version v;
            if (!Version.TryParse(core.IndexOf('.') < 0 ? core + ".0" : core, out v)) return null;
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }

        internal static HttpClient MakeHttp(int seconds)
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(seconds) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", App.Name + "/" + CurrentText);
            // a cache between here and the site (a proxy, a CDN) checks with the site first: a new latest.json shows straight away
            http.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
            return http;
        }

        // =================================================================
        // Check
        // =================================================================

        /// <summary>The newest release above the running version (pre-releases only when asked), or null when up to date. Throws on failure.</summary>
        public static async Task<UpdateInfo> CheckAsync(bool includePre)
        {
            List<UpdateInfo> releases = await ReleasesAsync(App.Name, includePre);
            if (releases.Count == 0) throw new InvalidOperationException("there's no " + App.Name + " release on the update site yet");
            UpdateInfo u = PickNewest(releases, Current);
            if (u != null) await AddChangesAsync(u, Current);
            return u;
        }

        /// <summary>
        /// The release's changelog.md (the app's CHANGELOG.md, beside latest.json): u.Notes becomes every version's changes since 'from',
        /// so someone a few versions behind sees all of them. Without one (or when it can't be read) latest.json's notes stay.
        /// </summary>
        static async Task AddChangesAsync(UpdateInfo u, Version from)
        {
            try
            {
                using (HttpClient http = MakeHttp(15))
                using (HttpResponseMessage r = await http.GetAsync(u.Folder + "changelog.md"))
                {
                    if (!r.IsSuccessStatusCode) return;
                    string changes = ChangesBetween(await r.Content.ReadAsStringAsync(), from, u.Version);
                    if (changes.Length > 0) u.Notes = changes;
                }
            }
            catch (Exception ex) { Log.Write("Update check: changelog.md: " + ex.Message); }
        }

        /// <summary>
        /// From a changelog (markdown, a "## 0.38.190 …" heading per version): the sections above 'from' up to 'to', as they are (newest
        /// first, headings kept) - at most 'most' of them. "" when there are none.
        /// </summary>
        public static string ChangesBetween(string changelog, Version from, Version to, int most = 15)
        {
            var sb = new StringBuilder();
            bool inside = false;
            int sections = 0;
            foreach (string raw in (changelog ?? "").Replace("\r", "").Split('\n'))
            {
                if (raw.StartsWith("## "))
                {
                    Version v = ParseVersion(raw.Substring(3));
                    inside = v != null && v > from && v <= to && sections < most;
                    if (inside)
                    {
                        // one blank line between sections, whatever the changelog has
                        string so = sb.ToString().TrimEnd();
                        sb.Clear().Append(so);
                        if (so.Length > 0) sb.Append("\n\n");
                        sections++;
                    }
                }
                else if (raw.StartsWith("# ")) inside = false;
                if (inside) sb.Append(raw.TrimEnd()).Append('\n');
            }
            return sb.ToString().Trim();
        }

        /// <summary>
        /// An app's releases on the update site, newest first: the release (Name/latest.json) and, when asked, the pre-release
        /// (Name/pre/latest.json) - whichever are there. Throws on failure (a pre-release folder that can't be read only goes in the log).
        /// </summary>
        public static async Task<List<UpdateInfo>> ReleasesAsync(string appName, bool includePre)
        {
            var list = new List<UpdateInfo>();
            using (HttpClient http = MakeHttp(15))
            {
                UpdateInfo stable = await ReadReleaseAsync(http, appName, false);
                if (stable != null) list.Add(stable);
                if (includePre)
                {
                    try
                    {
                        UpdateInfo pre = await ReadReleaseAsync(http, appName, true);
                        if (pre != null) list.Add(pre);
                    }
                    catch (Exception ex)
                    {
                        if (stable == null) throw;
                        Log.Write("Update check: " + appName + "'s pre-release: " + ex.Message);
                    }
                }
            }
            list.Sort((a, b) => b.Version.CompareTo(a.Version));
            return list;
        }

        /// <summary>One latest.json (null when the folder has none). Throws on failure.</summary>
        static async Task<UpdateInfo> ReadReleaseAsync(HttpClient http, string appName, bool pre)
        {
            string folder = SiteFolder(appName, pre);
            HttpResponseMessage r;
            try { r = await http.GetAsync(folder + "latest.json"); }
            catch (TaskCanceledException) { throw new InvalidOperationException("the update site didn't answer in time"); }
            catch (HttpRequestException ex) { throw new InvalidOperationException("couldn't reach the update site (" + ex.Message + ")"); }
            using (r)
            {
                if (r.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
                if (!r.IsSuccessStatusCode) throw new InvalidOperationException("the update site answered HTTP " + (int)r.StatusCode);
                UpdateInfo u = ParseRelease(await r.Content.ReadAsStringAsync(), folder);
                u.PreRelease = pre;
                return u;
            }
        }

        /// <summary>Is this release file the one this app installs from? (Name.exe, or a Name….zip.)</summary>
        static bool IsInstallFile(string name)
        {
            if (App.Install == InstallStyle.SingleExe) return name.Equals(ExeName, StringComparison.OrdinalIgnoreCase);
            return name.StartsWith(App.Name, StringComparison.OrdinalIgnoreCase) && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The newest release above 'current' that this app can install (its file, with a SHA-256), or null (public for tests).</summary>
        public static UpdateInfo PickNewest(IEnumerable<UpdateInfo> releases, Version current)
        {
            UpdateInfo best = null;
            foreach (UpdateInfo u in releases)
            {
                if (u.Version <= current) continue;
                if (best != null && u.Version <= best.Version) continue;
                if (u.FileUrl.Length == 0 || u.Sha256.Length == 0 || !IsInstallFile(u.FileName)) continue;   // nothing this app can install
                best = u;
            }
            return best;
        }

        /// <summary>A latest.json from the site folder 'folder' (ending with "/"). Throws when it isn't a release (public for tests).</summary>
        public static UpdateInfo ParseRelease(string json, string folder)
        {
            using (JsonDocument d = JsonDocument.Parse(json))
            {
                JsonElement e = d.RootElement;
                if (e.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("the update site's latest.json isn't a release");
                Version v = ParseVersion(Str(e, "version"));
                if (v == null) throw new InvalidOperationException("the update site's latest.json has no version");
                string app = Str(e, "app");
                var u = new UpdateInfo { Version = v, Tag = "v" + Text(v), Notes = Str(e, "notes"), Folder = folder };
                u.Title = (app.Length > 0 ? app : App.Name) + " " + Text(v);
                DateTime pub;
                if (DateTime.TryParse(Str(e, "published"), System.Globalization.CultureInfo.InvariantCulture,
                                      System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out pub)) u.Published = pub;
                string page = Str(e, "page");
                if (page.Length > 0) u.PageUrl = page.Contains("://") ? page : new Uri(new Uri(folder), page).ToString();

                JsonElement files;
                if (e.TryGetProperty("files", out files) && files.ValueKind == JsonValueKind.Object)
                    foreach (JsonProperty f in files.EnumerateObject())
                    {
                        if (f.Name.Length == 0 || f.Name.IndexOfAny(new[] { '/', '\\' }) >= 0) continue;   // only files in this folder
                        var sf = new SiteFile { Name = f.Name, Url = folder + Uri.EscapeDataString(f.Name) };
                        if (f.Value.ValueKind == JsonValueKind.Object)
                        {
                            sf.Sha256 = (FirstHex64(Str(f.Value, "sha256")) ?? "");
                            JsonElement sz;
                            if (f.Value.TryGetProperty("size", out sz) && sz.ValueKind == JsonValueKind.Number) sf.Size = sz.GetInt64();
                        }
                        u.Files[f.Name] = sf;
                    }

                SiteFile install;
                if (u.Files.TryGetValue(Str(e, "install"), out install))
                {
                    u.FileName = install.Name;
                    u.FileUrl = install.Url;
                    u.Sha256 = install.Sha256;
                    u.FileSize = install.Size;
                }
                SiteFile setup;
                if (u.Files.TryGetValue(Str(e, "installer"), out setup)) u.Installer = setup;

                foreach (JsonProperty p in e.EnumerateObject())
                {
                    switch (p.Value.ValueKind)
                    {
                        case JsonValueKind.String: u.Fields[p.Name] = p.Value.GetString() ?? ""; break;
                        case JsonValueKind.Number: u.Fields[p.Name] = p.Value.GetRawText(); break;
                        case JsonValueKind.True: u.Fields[p.Name] = "true"; break;
                        case JsonValueKind.False: u.Fields[p.Name] = "false"; break;
                    }
                }
                return u;
            }
        }

        /// <summary>
        /// The newest version of another app on the update site that its updater can install (vShell's Settings lists the suite's apps),
        /// the pre-release too when asked; null when there's none. Throws on failure.
        /// </summary>
        public static async Task<Version> NewestReleaseAsync(string appName, bool includePre)
        {
            Version best = null;
            foreach (UpdateInfo u in await ReleasesAsync(appName, includePre))
                if (u.FileUrl.Length > 0 && u.Sha256.Length > 0 && (best == null || u.Version > best)) best = u.Version;
            return best;
        }

        static string Str(JsonElement e, string name)
        {
            JsonElement v;
            return e.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        }

        static bool Bool(JsonElement e, string name)
        {
            JsonElement v;
            return e.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.True;
        }

        // =================================================================
        // Download, check, stage, start the watchdog
        // =================================================================

        /// <summary>
        /// Something the app does with the release before the swap (vShell: bring its plugins along); returns "" or a note for the log.
        /// </summary>
        public static Func<UpdateInfo, Task<string>> BeforeSwap;

        /// <summary>
        /// Whether the app can close for the update now (vXplorer: not while a copy is running - closing would cut it off): null = yes,
        /// otherwise why not. Asked before anything is swapped; the update then stops with that reason and nothing changes.
        /// </summary>
        public static Func<string> CanSwap;

        static string HeldBack()
        {
            try { return CanSwap == null ? null : CanSwap(); }
            catch { return null; }
        }

        /// <summary>
        /// Download, check and stage the new version, then start the watchdog. Returns null when the caller should now close the app (the
        /// watchdog starts the new version), or a one-line reason (nothing was changed).
        /// </summary>
        public static async Task<string> DownloadAndStageAsync(UpdateInfo u, Action<double> progress)
        {
            string reason = CantUpdateReason ?? HeldBack();
            if (reason != null) return reason;
            string result = App.Install == InstallStyle.SingleExe ? await StageExe(u, progress) : await StageFolder(u, progress);
            if (result != null) Log.Write("Update install: " + result);
            return result;
        }

        /// <summary>
        /// Download the release's file to 'dest', with progress up to 'upTo', and check its size and checksum. null = fine. It all runs off
        /// the caller's thread (ConfigureAwait(false)): on a window's thread every chunk's continuation and progress call was queued ahead of
        /// drawing, so the notice's bar stood still until the end - and the checksum froze the window. Progress is reported each 0.1 %.
        /// </summary>
        static async Task<string> Download(UpdateInfo u, string dest, Action<double> progress, double upTo)
        {
            // every release lists its file's SHA-256 (build.cmd writes it into latest.json): without one nothing is installed - the checksum
            // is the only proof the download is the release's file
            string expected = u.Sha256;
            if (string.IsNullOrEmpty(expected)) return "The release has no SHA-256 checksum, so it wasn't installed.";
            using (HttpClient http = MakeHttp(900))
            {
                using (HttpResponseMessage r = await http.GetAsync(u.FileUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    if (!r.IsSuccessStatusCode) return "The download failed: HTTP " + (int)r.StatusCode;
                    long total = r.Content.Headers.ContentLength ?? u.FileSize;
                    using (Stream src = await r.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        var buf = new byte[81920];
                        long got = 0;
                        int n, shown = -1;
                        while ((n = await src.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false)) > 0)
                        {
                            await dst.WriteAsync(buf, 0, n).ConfigureAwait(false);
                            got += n;
                            if (progress == null || total <= 0) continue;
                            int step = (int)Math.Min(1000, 1000 * got / total);
                            if (step == shown) continue;
                            shown = step;
                            progress(Math.Min(upTo, upTo * got / total));
                        }
                    }
                }
            }
            var info = new FileInfo(dest);
            if (info.Length < 200 * 1024 || (u.FileSize > 0 && info.Length != u.FileSize)) { TryDelete(dest); return "The download was incomplete - try again."; }
            if (!HashFile(dest).Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(dest);
                // also what happens while a new release is still being copied to the site: the next check gets the finished one
                return "The download doesn't match the release's SHA-256 checksum, so it wasn't installed - try again in a few minutes.";
            }
            return null;
        }

        static void NotePending(UpdateInfo u)
        {
            Directory.CreateDirectory(Log.Folder);
            TryDelete(Path.Combine(Log.Folder, OkFile));
            File.WriteAllText(Path.Combine(Log.Folder, PendingFile), CurrentText + "\n" + Text(u.Version));
        }

        static async Task<string> StageExe(UpdateInfo u, Action<double> progress)
        {
            string exe = ExePath;
            string dir = Path.GetDirectoryName(exe);
            string fresh = Path.Combine(dir, App.Name + ".new.exe"), old = Path.Combine(dir, App.Name + ".old.exe");
            try
            {
                string probe = Path.Combine(dir, App.Name.ToLowerInvariant() + "-write-test.tmp");
                try { File.WriteAllText(probe, "x"); File.Delete(probe); }
                catch { return App.Name + " can't write to its folder (" + dir + "). Install it with the installer, or update it by hand."; }

                string err = await Download(u, fresh, progress, 1.0);
                if (err != null) return err;
                using (var fs = File.OpenRead(fresh))
                    if (fs.ReadByte() != 'M' || fs.ReadByte() != 'Z') { TryDelete(fresh); return "The download isn't a Windows program."; }
                if (progress != null) progress(1.0);

                if (BeforeSwap != null)
                {
                    string note = await BeforeSwap(u);
                    if (!string.IsNullOrEmpty(note)) Log.Write("Update: " + note);
                }
                string held = HeldBack();
                if (held != null) { TryDelete(fresh); return held; }

                // swap: running exe -> Name.old.exe, new -> Name.exe
                TryDelete(old);
                if (File.Exists(old)) return "An older " + App.Name + ".old.exe is still in use - close " + App.Name + ", open it again and retry.";
                File.Move(exe, old);
                try { File.Move(fresh, exe); }
                catch (Exception ex)
                {
                    try { File.Move(old, exe); } catch { }
                    return App.Name + " couldn't put the new version in place: " + ex.Message;
                }

                // the watchdog (the old exe) starts the new one once this process has gone, and goes back if it doesn't start
                NotePending(u);
                var psi = new ProcessStartInfo(old) { UseShellExecute = false, WorkingDirectory = dir };
                psi.ArgumentList.Add("--update-watchdog");
                psi.ArgumentList.Add(Environment.ProcessId.ToString());
                psi.ArgumentList.Add(exe);
                Process.Start(psi);
                Log.Write("Update: staged " + u.Tag + ", restarting");
                return null;
            }
            catch (Exception ex)
            {
                TryDelete(fresh);
                return "The update failed: " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message);
            }
        }

        static async Task<string> StageFolder(UpdateInfo u, Action<double> progress)
        {
            string dir = InstallDir;
            string parent = Path.GetDirectoryName(dir), name = Path.GetFileName(dir);
            string staging = Path.Combine(parent, name + ".update");
            string zip = Path.Combine(Path.GetTempPath(), App.Name + "-update-" + Text(u.Version) + ".zip");
            try
            {
                string probe = Path.Combine(parent, App.Name.ToLowerInvariant() + "-write-test.tmp");
                try { File.WriteAllText(probe, "x"); File.Delete(probe); }
                catch { return App.Name + " can't write beside its folder (" + parent + "). Install it with the installer, or update it by hand."; }

                string err = await Download(u, zip, progress, 0.95);
                if (err != null) return err;

                // unpack beside the install (the same drive, so the swap is two quick renames)
                await Task.Run(() =>
                {
                    if (Directory.Exists(staging)) Directory.Delete(staging, true);
                    ZipFile.ExtractToDirectory(zip, staging);
                });
                TryDelete(zip);
                if (!File.Exists(Path.Combine(staging, ExeName)) || (!string.IsNullOrEmpty(App.FolderMarker) && !Directory.Exists(Path.Combine(staging, App.FolderMarker))))
                {
                    try { Directory.Delete(staging, true); } catch { }
                    return "The download doesn't hold a complete " + App.Name + ".";
                }
                if (progress != null) progress(1.0);

                if (BeforeSwap != null)
                {
                    string note = await BeforeSwap(u);
                    if (!string.IsNullOrEmpty(note)) Log.Write("Update: " + note);
                }
                string held = HeldBack();
                if (held != null) { try { Directory.Delete(staging, true); } catch { } return held; }

                // the watchdog: a PowerShell script that swaps the folders once the app has closed, and goes back if the new one fails
                NotePending(u);
                string script = Path.Combine(Log.Folder, "update.ps1");
                File.WriteAllText(script, FolderWatchdog, new UTF8Encoding(true));
                var psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Log.Folder };
                foreach (string a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script,
                                             "-ProcessId", Environment.ProcessId.ToString(), "-Dir", dir, "-Staging", staging,
                                             "-Data", Log.Folder, "-To", Text(u.Version), "-Exe", ExeName, "-Log", Log.FilePath, "-App", App.Name })
                    psi.ArgumentList.Add(a);
                Process.Start(psi);
                Log.Write("Update: staged " + u.Tag + " in " + staging + ", restarting");
                return null;
            }
            catch (Exception ex)
            {
                TryDelete(zip);
                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
                return "The update failed: " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message);
            }
        }

        /// <summary>
        /// The folder watchdog. Waits for the app to close, swaps the folders (retrying while something still holds the old one), copies
        /// the uninstaller across, starts the new exe, and waits up to a minute for update-ok. If the new one quits first, the old folder
        /// goes back and starts. Writes to the app's log.
        /// </summary>
        const string FolderWatchdog = @"param([int]$ProcessId, [string]$Dir, [string]$Staging, [string]$Data, [string]$To, [string]$Exe, [string]$Log, [string]$App)
function Note($t) { try { Add-Content -Path $Log -Value ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff') + '  Update watchdog: ' + $t) -Encoding UTF8 } catch {} }
function Swap($from, $to) { for ($i = 0; $i -lt 60; $i++) { try { Rename-Item -LiteralPath $from -NewName (Split-Path $to -Leaf) -ErrorAction Stop; return $true } catch { Start-Sleep -Milliseconds 500 } }; return $false }
$old = $Dir + '.old'; $failed = $Dir + '.failed'
$ok = Join-Path $Data 'update-ok.txt'; $pending = Join-Path $Data 'update-pending.txt'; $failFile = Join-Path $Data 'update-failed.txt'
$exePath = Join-Path $Dir $Exe
# 1. wait for the app that asked for the update to close
#    (one that still shows a window is asking something: it is left alone; only one with no window left is ended)
try { $p = Get-Process -Id $ProcessId -ErrorAction Stop; $n = 0; $said = $false
  while (-not $p.WaitForExit(1000)) { $n++; if ($n -lt 20) { continue }; $p.Refresh()
    if ($p.MainWindowHandle -ne [IntPtr]::Zero -and $p.Responding) { if (-not $said) { $said = $true; Note ""the old $App still has a window open - waiting for it to close"" }; continue }
    Note ""the old $App did not close within 20 s - ending it""; $p.Kill(); $p.WaitForExit(5000) | Out-Null; break } } catch {}
Start-Sleep -Milliseconds 700
if (Test-Path -LiteralPath $pending) { try { (Get-Item -LiteralPath $pending).LastWriteTimeUtc = [DateTime]::UtcNow } catch {} }
# 2. the swap: install -> .old, the new one -> install
if (Test-Path -LiteralPath $old) { Remove-Item -LiteralPath $old -Recurse -Force -ErrorAction SilentlyContinue }
if (-not (Swap $Dir $old)) { Note ""couldn't move $Dir aside (a window or program still has it open) - staying on the old version""; Set-Content -Path $failFile -Value $To; Remove-Item $pending -ErrorAction SilentlyContinue; Start-Process -FilePath $exePath; exit 1 }
if (-not (Swap $Staging $Dir)) { Note 'couldn''t put the new version in place - going back'; Swap $old $Dir | Out-Null; Set-Content -Path $failFile -Value $To; Remove-Item $pending -ErrorAction SilentlyContinue; Start-Process -FilePath $exePath; exit 1 }
# the installer's uninstaller lives in the install folder: bring it over
Get-ChildItem -LiteralPath $old -Filter 'unins*' -ErrorAction SilentlyContinue | Copy-Item -Destination $Dir -Force -ErrorAction SilentlyContinue
# 3. start the new one; it writes update-ok once it's up; if it quits first, go back
$child = Start-Process -FilePath $exePath -WorkingDirectory $Dir -PassThru
$until = (Get-Date).AddSeconds(60)
while ((Get-Date) -lt $until) {
  if (Test-Path -LiteralPath $ok) { Remove-Item $ok, $pending -ErrorAction SilentlyContinue; Note ""now on $To""; Start-Sleep 3; Remove-Item -LiteralPath $old -Recurse -Force -ErrorAction SilentlyContinue; exit 0 }
  if ($child.HasExited) { break }
  Start-Sleep -Milliseconds 500
}
if (-not $child.HasExited) { Remove-Item $pending -ErrorAction SilentlyContinue; Note 'the new version is slow to start - leaving it running'; exit 0 }
Note 'the new version quit while starting - going back to the previous one'
if (Test-Path -LiteralPath $failed) { Remove-Item -LiteralPath $failed -Recurse -Force -ErrorAction SilentlyContinue }
Swap $Dir $failed | Out-Null
Swap $old $Dir | Out-Null
Set-Content -Path $failFile -Value $To
Remove-Item $pending -ErrorAction SilentlyContinue
Start-Process -FilePath $exePath -WorkingDirectory $Dir
exit 1
";

        public static string HashFile(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(path))
            {
                byte[] h = sha.ComputeHash(fs);
                var sb = new StringBuilder(64);
                foreach (byte b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>The first 64-character hex run in a text (sha256sum / certutil / Get-FileHash output all work).</summary>
        public static string FirstHex64(string text)
        {
            if (text == null) return null;
            string t = text.Replace(" ", "");      // certutil prints the hash with spaces on old Windows versions
            int run = 0;
            for (int i = 0; i < t.Length; i++)
            {
                run = Uri.IsHexDigit(t[i]) ? run + 1 : 0;
                if (run == 64 && (i + 1 >= t.Length || !Uri.IsHexDigit(t[i + 1]))) return t.Substring(i - 63, 64).ToLowerInvariant();
            }
            return null;
        }

        static bool PendingFresh()
        {
            try
            {
                string f = Path.Combine(Log.Folder, PendingFile);
                return File.Exists(f) && (DateTime.UtcNow - File.GetLastWriteTimeUtc(f)).TotalMinutes < 5;
            }
            catch { return false; }
        }

        static void TryDelete(string p)
        {
            try { if (File.Exists(p)) File.Delete(p); } catch { }
        }

        // =================================================================
        // The one-exe watchdog (runs in the OLD exe, no window)
        // =================================================================

        /// <summary>Name.old.exe --update-watchdog &lt;pid&gt; &lt;path to Name.exe&gt;. Each one-exe app calls this from Main.</summary>
        public static int RunWatchdog(string[] args)
        {
            try
            {
                int pid = int.Parse(args[1]);
                string exe = args[2];
                string dir = Path.GetDirectoryName(exe);
                string old = Path.Combine(dir, App.Name + ".old.exe");
                string ok = Path.Combine(Log.Folder, OkFile);
                string pending = Path.Combine(Log.Folder, PendingFile);

                // 1. wait for the copy that asked for the update to close (its windows save as they go). One that still shows a
                //    window is asking something ("Save the changes?") or was told to stay open: it's left alone however long that
                //    takes, and the new version starts when it closes. Only one with no window left (stuck while closing) is ended.
                try
                {
                    using (Process p = Process.GetProcessById(pid))
                    {
                        bool said = false;
                        for (int waited = 0; !p.WaitForExit(1000); waited++)
                        {
                            if (waited < 20) continue;
                            bool window = true;
                            try { p.Refresh(); window = p.MainWindowHandle != IntPtr.Zero && p.Responding; } catch { }   // not answering = stuck, not asking
                            if (window)
                            {
                                if (!said) { said = true; Log.Write("Update: the previous " + App.Name + " still has a window open - waiting for it to close"); }
                                continue;
                            }
                            Log.Write("Update: the previous " + App.Name + " didn't close within 20 s - ending it");
                            try { p.Kill(); } catch { }
                            p.WaitForExit(5000);
                            break;
                        }
                    }
                }
                catch { }
                Thread.Sleep(500);
                try { if (File.Exists(pending)) File.SetLastWriteTimeUtc(pending, DateTime.UtcNow); } catch { }    // the wait may have been long

                // 2. start the new version
                Process child = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = dir });

                // 3. it writes update-ok once it's up; if it quits first, put the old one back
                DateTime until = DateTime.UtcNow.AddSeconds(60);
                while (DateTime.UtcNow < until)
                {
                    if (File.Exists(ok)) { TryDelete(ok); TryDelete(pending); return 0; }
                    if (child == null || child.HasExited) break;
                    Thread.Sleep(500);
                }
                if (child != null && !child.HasExited) { TryDelete(pending); return 0; }   // still running, just slow: leave it

                Log.Write("Update: the new version quit while starting - going back to the previous one");
                string bad = Path.Combine(dir, App.Name + ".failed.exe");
                TryDelete(bad);
                try { File.Move(exe, bad); } catch { }
                File.Copy(old, exe, true);   // the watchdog IS Name.old.exe: copy it rather than move it
                string to = "";
                try { string[] lines = File.ReadAllLines(pending); if (lines.Length > 1) to = lines[1]; } catch { }
                File.WriteAllText(Path.Combine(Log.Folder, FailedFile), to);
                TryDelete(pending);
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = dir });
                return 1;
            }
            catch (Exception ex)
            {
                Log.Write("Update watchdog: " + ex.Message);
                return 2;
            }
            finally { Log.Flush(); }
        }

        // =================================================================
        // In the new version
        // =================================================================

        /// <summary>Once the app has started properly: tells a waiting watchdog the update worked, and tidies leftovers later.</summary>
        public static void ReportStarted()
        {
            try
            {
                string pending = Path.Combine(Log.Folder, PendingFile);
                if (PendingFresh())
                {
                    File.WriteAllText(Path.Combine(Log.Folder, OkFile), CurrentText);
                    Log.Write("Update: now running " + CurrentText);
                }
                else TryDelete(pending);   // left over from an update that was interrupted
            }
            catch { }
            Task.Delay(TimeSpan.FromMinutes(2)).ContinueWith(delegate
            {
                string exe = ExePath;
                if (exe != null)
                {
                    string dir = Path.GetDirectoryName(exe);
                    TryDelete(Path.Combine(dir, App.Name + ".old.exe"));
                    TryDelete(Path.Combine(dir, App.Name + ".new.exe"));
                }
                string folder = InstallDir;
                if (folder != null)
                    foreach (string leftover in new[] { folder + ".old", folder + ".failed" })
                        try { if (Directory.Exists(leftover)) Directory.Delete(leftover, true); } catch { }
            });
        }

        /// <summary>A version that was installed but wouldn't start (and was rolled back), once; null when none.</summary>
        public static string TakeFailedVersion()
        {
            string f = Path.Combine(Log.Folder, FailedFile);
            try
            {
                if (!File.Exists(f)) return null;
                string v = File.ReadAllText(f).Trim();
                File.Delete(f);
                return v.Length > 0 ? v : "the new version";
            }
            catch { return null; }
        }
    }
}
