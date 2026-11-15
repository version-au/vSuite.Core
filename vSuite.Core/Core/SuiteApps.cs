using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace vSuite
{
    /// <summary>
    /// Another suite app's settings, as vShell's Settings > Updates > vSuite apps shows them: its settings file (%APPDATA%\Name\settings.json)
    /// and its "Include pre-releases". A change goes to the running copy (it saves it itself - otherwise its next save would undo it), or
    /// straight into the file when it isn't running.
    /// </summary>
    public static class SuiteApps
    {
        public static string DataFolder(string appName) { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appName); }

        public static string SettingsFile(string appName) { return Path.Combine(DataFolder(appName), "settings.json"); }

        /// <summary>An app's installer on the update site (Name-setup.exe in its folder), with the release it's from.</summary>
        public sealed class Installer { public string Url, FileName, Sha256 = ""; public long Size; public Version Version; }

        /// <summary>The newest installer for the app on the update site (the pre-release's too when asked), or null when there's none. Throws on failure.</summary>
        public static async Task<Installer> FindInstallerAsync(string appName, bool includePre)
        {
            return PickInstaller(await Updater.ReleasesAsync(appName, includePre), appName);
        }

        /// <summary>An app's installer file: Name-setup.exe (the same name in every release, so a web page can link to the latest one)
        /// or Name-setup-version.exe (older releases).</summary>
        public static bool IsInstallerName(string appName, string fileName)
        {
            if (string.IsNullOrEmpty(fileName) || !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
            return fileName.Equals(appName + "-setup.exe", StringComparison.OrdinalIgnoreCase)
                || fileName.StartsWith(appName + "-setup-", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The newest release's installer, or null (public for tests).</summary>
        public static Installer PickInstaller(System.Collections.Generic.IEnumerable<UpdateInfo> releases, string appName)
        {
            Installer best = null;
            foreach (UpdateInfo r in releases)
            {
                SiteFile f = r.Installer;
                if (f == null || !IsInstallerName(appName, f.Name) || (best != null && r.Version <= best.Version)) continue;
                best = new Installer { Url = f.Url, FileName = f.Name, Sha256 = f.Sha256, Size = f.Size, Version = r.Version };
            }
            return best;
        }

        /// <summary>Download an installer to %TEMP% (progress 0..1) and return the file. Throws on failure.</summary>
        public static async Task<string> DownloadInstallerAsync(Installer inst, Action<double> progress)
        {
            string file = Path.Combine(Path.GetTempPath(), inst.FileName);
            using (HttpClient http = Updater.MakeHttp(900))
            using (HttpResponseMessage r = await http.GetAsync(inst.Url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                if (!r.IsSuccessStatusCode) throw new InvalidOperationException("the download failed: HTTP " + (int)r.StatusCode);
                long total = r.Content.Headers.ContentLength ?? inst.Size;
                using (Stream src = await r.Content.ReadAsStreamAsync())
                using (var dst = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buf = new byte[81920];
                    long got = 0;
                    int n;
                    while ((n = await src.ReadAsync(buf, 0, buf.Length)) > 0)
                    {
                        await dst.WriteAsync(buf, 0, n);
                        got += n;
                        if (progress != null && total > 0) progress(Math.Min(1.0, got / (double)total));
                    }
                }
            }
            var info = new FileInfo(file);
            if (info.Length < 100 * 1024 || (inst.Size > 0 && info.Length != inst.Size)) { try { File.Delete(file); } catch { } throw new InvalidOperationException("the download was incomplete - try again"); }
            if (!string.IsNullOrEmpty(inst.Sha256) && !Updater.HashFile(file).Equals(inst.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(file); } catch { }
                throw new InvalidOperationException("the download doesn't match the release's SHA-256 checksum - try again in a few minutes");
            }
            using (var fs = File.OpenRead(file))
                if (fs.ReadByte() != 'M' || fs.ReadByte() != 'Z') { fs.Dispose(); try { File.Delete(file); } catch { } throw new InvalidOperationException("the download isn't a Windows program"); }
            return file;
        }

        /// <summary>The app's Include pre-releases (false when it has no settings yet).</summary>
        public static bool UpdatePre(string appName)
        {
            JsonElement v;
            return Read(appName, "UpdatePre", out v) && v.ValueKind == JsonValueKind.True;
        }

        /// <summary>The version the app won't install (skipped, or rolled back because it didn't start) - "0.1.41", or "".</summary>
        public static string UpdateSkip(string appName)
        {
            JsonElement v;
            return Read(appName, "UpdateSkip", out v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        }

        static bool Read(string appName, string field, out JsonElement value)
        {
            value = default(JsonElement);
            try
            {
                string f = SettingsFile(appName);
                if (!File.Exists(f)) return false;
                using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var doc = JsonDocument.Parse(fs))
                {
                    JsonElement v;
                    if (!doc.RootElement.TryGetProperty(field, out v)) return false;
                    value = v.Clone();
                    return true;
                }
            }
            catch { return false; }
        }

        /// <summary>Change the app's Include pre-releases: its running copy if there is one, else its settings file.</summary>
        public static void SetUpdatePre(string appName, bool on)
        {
            string cmd = Ui.UpdateCenter.PreCommand + " " + (on ? "on" : "off");
            if (Instance.SendTo(appName, DataFolder(appName), cmd)) { Log.Write("Suite: " + appName + " (running) - " + cmd); return; }
            try
            {
                string f = SettingsFile(appName);
                JsonObject o = File.Exists(f) ? JsonNode.Parse(File.ReadAllText(f)) as JsonObject : null;
                if (o == null) o = new JsonObject();
                o["UpdatePre"] = on;
                Directory.CreateDirectory(DataFolder(appName));
                string tmp = f + ".tmp";
                File.WriteAllText(tmp, o.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                File.Move(tmp, f, true);
                Log.Write("Suite: " + appName + " - " + cmd + " (in its settings file)");
            }
            catch (Exception ex) { Log.Write("Suite: " + appName + " " + cmd + ": " + ex.Message); }
        }
    }
}