using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace vSuite.Server;

/// <summary>The settings self-update reads: offer pre-releases, and the newest version already told about.</summary>
public interface ISelfUpdateSettings
{
    bool UpdatePre { get; }
    string UpdateTold { get; }
}

/// <summary>What the update site says (&lt;app&gt;/latest.json - written by vSuite.Core's site-release.ps1).</summary>
public sealed class SiteRelease
{
    public string App { get; set; }
    public string Version { get; set; }
    public DateTimeOffset? Published { get; set; }
    public bool Prerelease { get; set; }
    public string Notes { get; set; }
    public Dictionary<string, SiteFile> Files { get; set; } = new();
}

public sealed class SiteFile
{
    public long Size { get; set; }
    public string Sha256 { get; set; }
}

/// <summary>
/// A server app updating itself from the vSuite update site (https://vsuite.vrsion.com/<app>/, pre-releases in pre/). It never needs root
/// or Docker's socket: the new build is downloaded (size and SHA-256 checked) and unpacked into the data folder as app.new, then the app
/// stops with exit code 3. The start script (deploy/run.sh - systemd's ExecStart in the LXC, the image's entrypoint in Docker) puts
/// app.new in place of app, keeps the old one as app.old, and marks app.trying. Once the new version has run for a minute it confirms
/// (app.trying and app.old go); if it stops before that, the start script puts app.old back and leaves update-failed for the page.
/// Started some other way (no PREFIX_LAUNCHER), it only checks and tells.
/// </summary>
public sealed class SelfUpdate
{
    public const int ExitForUpdate = 3;
    public static readonly TimeSpan ConfirmAfter = TimeSpan.FromMinutes(1);
    static readonly TimeSpan Every = TimeSpan.FromHours(6);

    readonly Func<ISelfUpdateSettings> _settings;
    readonly HttpClient _http;
    readonly string _site;
    readonly SemaphoreSlim _one = new(1, 1);

    public SiteRelease Newest { get; private set; }
    public DateTimeOffset? Checked { get; private set; }
    public string Problem { get; private set; }
    /// <summary>What it's doing: null, "checking", "downloading", "restarting".</summary>
    public string Working { get; private set; }
    /// <summary>The update to this version didn't start, so the one before was put back (from the start script's update-failed).</summary>
    public string Failed { get; private set; }
    /// <summary>Just updated from this version (confirmed after a minute).</summary>
    public string UpdatedFrom { get; private set; }
    public event Action Changed;
    /// <summary>A newer version was found that hasn't been told about yet: (version, its notes).</summary>
    public event Action<SiteRelease> Found;
    /// <summary>The update is unpacked: stop the app (with ExitForUpdate) so the start script swaps it in.</summary>
    public Action Restart { get; set; }

    public SelfUpdate(Func<ISelfUpdateSettings> settings, HttpClient http = null, string site = null)
    {
        _settings = settings;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _site = (site ?? ServerApp.Env("UPDATE_SITE") ?? "https://vsuite.vrsion.com/" + ServerApp.Lower + "/").TrimEnd('/') + "/";
    }

    /// <summary>"lxc" or "docker" when started by the start script (it sets PREFIX_LAUNCHER) - only then can it update itself.</summary>
    public static string Launcher { get { return ServerApp.Env("LAUNCHER"); } }
    public static bool CanUpdate => Launcher != null && Paths.Data != null;

    static string AppDir => Paths.File("app");
    static string NewDir => Paths.File("app.new");
    static string Trying => Paths.File("app.trying");
    static string FailedFile => Paths.File("update-failed");

    /// <summary>The file for this machine's CPU on the site.</summary>
    public static string FileName => ServerApp.Lower + "-linux-" + (RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64") + ".tar.gz";

    string Folder(bool pre) { return _site + (pre ? "pre/" : ""); }

    public static bool Newer(string version, string than)
    {
        static Version V(string s) { s = (s ?? "").Trim().TrimStart('v'); int cut = s.IndexOfAny(new[] { '-', '+', ' ' }); if (cut > 0) s = s.Substring(0, cut); return System.Version.TryParse(s, out var v) ? v : new Version(0, 0); }
        return V(version) > V(than);
    }

    public bool UpdateReady(string current) { return Newest != null && Newer(Newest.Version, current); }

    void Raise() { try { Changed?.Invoke(); } catch { } }

    /// <summary>Ask the site (the release, and the pre-release when they're wanted - whichever is newer).</summary>
    public async Task CheckAsync(string current)
    {
        if (!await _one.WaitAsync(0)) return;
        try
        {
            Working = "checking"; Problem = null; Raise();
            SiteRelease best = await Read(false);
            if (_settings().UpdatePre)
            {
                try { var pre = await Read(true); if (pre != null && (best == null || Newer(pre.Version, best.Version))) best = pre; }
                catch { }   // no pre-release up at the moment
            }
            Newest = best;
            Checked = DateTimeOffset.Now;
            if (best != null && Newer(best.Version, current) && best.Version != _settings().UpdateTold) Found?.Invoke(best);
        }
        catch (Exception ex) { Problem = "Couldn't ask the update site: " + Message(ex); Checked = DateTimeOffset.Now; }
        finally { Working = null; _one.Release(); Raise(); }
    }

    async Task<SiteRelease> Read(bool pre)
    {
        string json = await _http.GetStringAsync(Folder(pre) + "latest.json?t=" + DateTime.UtcNow.Ticks);
        var r = JsonSerializer.Deserialize<SiteRelease>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (r?.Version == null) throw new InvalidDataException("latest.json has no version");
        r.Prerelease = pre;
        return r;
    }

    /// <summary>The changelog's sections newer than `current` (the site's changelog.md), newest first, as (heading, lines).</summary>
    public async Task<List<(string Heading, List<string> Lines)>> ChangesSince(string current)
    {
        var list = new List<(string, List<string>)>();
        if (Newest == null) return list;
        string text = await _http.GetStringAsync(Folder(Newest.Prerelease) + "changelog.md?t=" + DateTime.UtcNow.Ticks);
        return ParseChanges(text, current);
    }

    public static List<(string Heading, List<string> Lines)> ParseChanges(string changelog, string current)
    {
        var list = new List<(string, List<string>)>();
        List<string> lines = null;
        foreach (string raw in (changelog ?? "").Replace("\r", "").Split('\n'))
        {
            if (raw.StartsWith("## "))
            {
                string heading = raw.Substring(3).Trim();
                string version = heading.Split(' ')[0];
                if (!Newer(version, current)) break;
                lines = new List<string>();
                list.Add((heading, lines));
            }
            else if (lines != null && raw.TrimStart().StartsWith("- ")) lines.Add(raw.TrimStart().Substring(2).Trim());
            else if (lines != null && raw.StartsWith("  ") && lines.Count > 0) lines[^1] += " " + raw.Trim();
        }
        return list;
    }

    /// <summary>Download, check and unpack the newest into app.new, then restart into it.</summary>
    public async Task UpdateAsync(string current)
    {
        if (!CanUpdate) throw new InvalidOperationException("This " + ServerApp.Name + " wasn't started by its start script, so it can't update itself - run the install again.");
        var r = Newest ?? throw new InvalidOperationException("Check for updates first.");
        if (!Newer(r.Version, current)) throw new InvalidOperationException("This is already the newest " + ServerApp.Name + ".");
        if (!r.Files.TryGetValue(FileName, out var want)) throw new InvalidOperationException(r.Version + " has no " + FileName + " on the update site.");
        if (!await _one.WaitAsync(0)) throw new InvalidOperationException(ServerApp.Name + " is already checking or updating.");
        string tmp = Paths.File("update.tar.gz");
        try
        {
            Working = "downloading"; Problem = null; Raise();
            using (var res = await _http.GetAsync(Folder(r.Prerelease) + FileName, HttpCompletionOption.ResponseHeadersRead))
            {
                res.EnsureSuccessStatusCode();
                await using var file = File.Create(tmp);
                await res.Content.CopyToAsync(file);
            }
            var info = new FileInfo(tmp);
            if (info.Length != want.Size) throw new InvalidDataException("the download is " + info.Length + " bytes, the site says " + want.Size);
            string sha;
            await using (var f = File.OpenRead(tmp)) sha = Convert.ToHexString(await SHA256.HashDataAsync(f)).ToLowerInvariant();
            if (!string.Equals(sha, want.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("the download's SHA-256 doesn't match the site's");

            Unpack(tmp, NewDir);
            if (!File.Exists(Path.Combine(NewDir, ServerApp.Lower))) throw new InvalidDataException("the download doesn't hold " + ServerApp.Name);
            File.WriteAllText(Path.Combine(NewDir, "version.txt"), r.Version);
            Log.Write(ServerApp.Name + " " + r.Version + " is downloaded - restarting into it");
            Working = "restarting"; Raise();
            await Task.Delay(1500);   // the page shows "Restarting…" first
            Restart?.Invoke();
        }
        catch (Exception ex)
        {
            Working = null;
            Problem = "The update didn't work: " + Message(ex);
            try { if (Directory.Exists(NewDir)) Directory.Delete(NewDir, true); } catch { }
            Log.Write(Problem);
            Raise();
            throw;
        }
        finally { try { File.Delete(tmp); } catch { } _one.Release(); }
    }

    /// <summary>A .tar.gz into a fresh folder, with Linux permissions (built on Windows, the archive has none worth keeping).</summary>
    public static void Unpack(string tarGz, string dir)
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        using (var gz = new GZipStream(File.OpenRead(tarGz), CompressionMode.Decompress))
            TarFile.ExtractToDirectory(gz, dir, overwriteFiles: true);
        if (OperatingSystem.IsWindows()) return;
        const UnixFileMode rx = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
        const UnixFileMode r = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
        foreach (var d in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories)) File.SetUnixFileMode(d, rx);
        File.SetUnixFileMode(dir, rx);
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(f);
            File.SetUnixFileMode(f, name == ServerApp.Lower || name == "run.sh" || ServerApp.Executables.Contains(name) ? rx : r);
        }
    }

    /// <summary>At start: a failed update (the start script put the old one back), or a new one to confirm after a minute.</summary>
    public async Task StartAsync(string current, CancellationToken stop)
    {
        try
        {
            if (File.Exists(FailedFile))
            {
                string v = File.ReadAllText(FailedFile).Trim();
                Failed = v.Length > 0 ? v : "the new version";
                File.Delete(FailedFile);
                Log.Write("The update to " + ServerApp.Name + " " + Failed + " didn't start - " + current + " was put back");
            }
            if (File.Exists(Trying))
            {
                string from = File.ReadAllText(Trying).Trim();
                await Task.Delay(ConfirmAfter, stop);
                File.Delete(Trying);
                try { string old = Paths.File("app.old"); if (Directory.Exists(old)) Directory.Delete(old, true); } catch { }
                UpdatedFrom = from.Length > 0 ? from : "an older version";
                Log.Write(ServerApp.Name + " " + current + " is running (updated from " + UpdatedFrom + ")");
                Raise();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Write("After an update: " + ex.Message); }
    }

    /// <summary>Check a minute after start, then every six hours.</summary>
    public async Task RunAsync(string current, CancellationToken stop)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stop);
            while (!stop.IsCancellationRequested)
            {
                await CheckAsync(current);
                await Task.Delay(Every, stop);
            }
        }
        catch (OperationCanceledException) { }
    }

    public void ClearNotes() { Failed = null; UpdatedFrom = null; Raise(); }

    static string Message(Exception ex) { return ex is HttpRequestException h && h.StatusCode != null ? "the site said " + (int)h.StatusCode : ex.Message; }
}
