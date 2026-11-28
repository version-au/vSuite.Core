using System.IO.Compression;
using System.Text.Json;

namespace vSuite.Server;

/// <summary>An app's settings that say where backups are also copied (Options › Backups › Also copy to). Null or empty: nowhere.</summary>
public interface IBackupSettings
{
    string BackupCopyTo { get; set; }
}

/// <summary>
/// Backups of a server app's whole data folder (1.0.51): settings, its data files, secret.key and its SSH key - everything a reinstall
/// needs - as a plain zip with a note of the app and version (vsuite-backup.json). Daily (the last 7) and by hand (the last 10), in the
/// data folder's backups folder, and copied to Options › Backups › Also copy to when set (a NAS share mounted in the container), where the
/// newest 7 of this app's are kept. Restoring puts the zip aside; at the next start (<see cref="ApplyPending"/>, before anything reads the
/// data) what's there is kept as a "before restore" backup and the zip's files go in its place.
/// </summary>
public sealed class Backups
{
    public const string Note = "vsuite-backup.json";

    /// <summary>Never backed up: the backups themselves, a restore waiting, logs, half-written and temporary files, sockets, an update being unpacked.</summary>
    static readonly string[] AlwaysSkip = { "backups", "restore.zip", "app.new", "app.old", "*.log", "*.tmp", "*.part", "*.sock", "*.pid" };

    readonly string _version;
    readonly Func<IBackupSettings> _settings;

    /// <summary>The app's own names not to back up (top-level files / folders, or *.ext anywhere) - caches, things made again by themselves.</summary>
    public string[] Skip { get; init; } = Array.Empty<string>();
    /// <summary>Files the app writes into the zip itself (vMediarr: its SQLite database copied safely while in use - and skipped above).</summary>
    public Action<ZipArchive> AddOwn { get; init; }
    /// <summary>An older backup without the note still counts as this app's when it has this file in it (vMediarr's older backups: "vmediarr.db").</summary>
    public string Legacy { get; init; }

    /// <summary>The last copy to the Also copy to folder: when, and the problem if it failed.</summary>
    public DateTime? CopiedAt { get; private set; }
    public string CopyProblem { get; private set; }

    public Backups(string version, Func<IBackupSettings> settings)
    {
        _version = version;
        _settings = settings;
    }

    public static string Folder { get { return Paths.File("backups"); } }
    static string Pending { get { return Paths.File("restore.zip"); } }

    public sealed record Entry(string Name, string Type, long Size, DateTime Time, string Path);

    public List<Entry> List()
    {
        var list = new List<Entry>();
        foreach (var type in new[] { "scheduled", "manual" })
        {
            string dir = Path.Combine(Folder, type);
            if (!Directory.Exists(dir)) continue;
            foreach (var f in new DirectoryInfo(dir).GetFiles("*.zip")) list.Add(new Entry(f.Name, type, f.Length, f.LastWriteTimeUtc, f.FullName));
        }
        return list.OrderByDescending(e => e.Time).ToList();
    }

    public Entry Find(string type, string name) { return List().FirstOrDefault(e => e.Type == type && e.Name == name); }

    /// <summary>A backup now ("scheduled" or "manual"); the oldest of that kind past 7 / 10 go. Then copied to Also copy to, when set.</summary>
    public Entry Create(string type = "manual")
    {
        string dir = Path.Combine(Folder, type);
        Directory.CreateDirectory(dir);
        string stem = ServerApp.Lower + "_backup_v" + _version + "_" + DateTime.Now.ToString("yyyy.MM.dd_HH.mm.ss");
        string name = stem + ".zip";
        for (int n = 2; File.Exists(Path.Combine(dir, name)); n++) name = stem + "_" + n + ".zip";      // two in the same second
        string zip = Path.Combine(dir, name);
        Write(zip + ".part", _version, Skip, AddOwn);
        File.Move(zip + ".part", zip, true);
        int keep = type == "scheduled" ? 7 : 10;
        foreach (var old in List().Where(e => e.Type == type).Skip(keep)) try { File.Delete(old.Path); } catch { }
        Log.Write("Backup made: " + name);
        Copy(zip);
        var f = new FileInfo(zip);
        return new Entry(name, type, f.Length, f.LastWriteTimeUtc, zip);
    }

    /// <summary>The whole data folder (less what's skipped) and the note, as a zip.</summary>
    static void Write(string zip, string version, string[] skip, Action<ZipArchive> addOwn)
    {
        var all = AlwaysSkip.Concat(skip ?? Array.Empty<string>()).ToArray();
        using var z = ZipFile.Open(zip, ZipArchiveMode.Create);
        var note = z.CreateEntry(Note);
        using (var s = note.Open())
            JsonSerializer.Serialize(s, new BackupNote { App = ServerApp.Name, Version = version, Made = DateTime.UtcNow, Host = Environment.MachineName }, JsonFile.Options);
        string root = Paths.Data;
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (Skipped(rel, all)) continue;
            try { z.CreateEntryFromFile(file, rel, CompressionLevel.Optimal); }
            catch (IOException ex) { Log.Write("Backup: left out " + rel + " (" + ex.Message + ")"); }       // gone or locked just now
        }
        addOwn?.Invoke(z);
    }

    /// <summary>A top-level name ("backups", "share/…") or *.ext at any depth.</summary>
    internal static bool Skipped(string rel, string[] skip)
    {
        string top = rel.Split('/')[0], file = rel[(rel.LastIndexOf('/') + 1)..];
        foreach (string s in skip)
        {
            if (s.StartsWith("*.")) { if (file.EndsWith(s[1..], StringComparison.OrdinalIgnoreCase)) return true; }
            else if (string.Equals(top, s, StringComparison.Ordinal) || rel == s) return true;
        }
        return false;
    }

    /// <summary>The newest backup into the Also copy to folder (as .part, then renamed), keeping this app's newest 7 there.</summary>
    void Copy(string zip)
    {
        string to = _settings?.Invoke()?.BackupCopyTo?.Trim();
        if (string.IsNullOrEmpty(to)) { CopyProblem = null; return; }
        try
        {
            if (!Directory.Exists(to)) throw new IOException("There's no folder " + to + " (is the share mounted?).");
            string dest = Path.Combine(to, Path.GetFileName(zip));
            File.Copy(zip, dest + ".part", true);
            File.Move(dest + ".part", dest, true);
            foreach (var old in new DirectoryInfo(to).GetFiles(ServerApp.Lower + "_backup_v*.zip").OrderByDescending(f => f.LastWriteTimeUtc).Skip(7))
                try { old.Delete(); } catch { }
            CopiedAt = DateTime.UtcNow;
            CopyProblem = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CopyProblem = "Couldn't copy it to " + to + ": " + ex.Message;
            Log.Write("Backup: " + CopyProblem);
        }
    }

    /// <summary>Whether a folder can take backups (Options › Backups › Also copy to): null if it can, else why not, in words.</summary>
    public static string CheckFolder(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return null;
        if (!Path.IsPathRooted(dir)) return "That needs to be a whole path, such as /mnt/backups.";
        if (!Directory.Exists(dir)) return "There's no folder " + dir + " on this machine (is the share mounted into the container?).";
        string test = Path.Combine(dir, "." + ServerApp.Lower + "-write-test");
        try { File.WriteAllText(test, "ok"); File.Delete(test); return null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ServerApp.Name + " can't write there: " + ex.Message; }
    }

    // ---------- restoring ----------

    public sealed class BackupNote
    {
        public string App { get; set; }
        public string Version { get; set; }
        public DateTime Made { get; set; }
        public string Host { get; set; }
    }

    /// <summary>What's in a backup - its note, or null for an older one of this app's (<see cref="Legacy"/>). Throws in words when it isn't this app's.</summary>
    public BackupNote Check(string zipPath)
    {
        ZipArchive z;
        try { z = ZipFile.OpenRead(zipPath); }
        catch (InvalidDataException) { throw new IOException("That isn't a backup (not a zip file)."); }
        using (z)
        {
            var e = z.GetEntry(Note);
            if (e == null)
            {
                if (Legacy != null && z.GetEntry(Legacy) != null) return null;
                throw new IOException("That isn't a " + ServerApp.Name + " backup.");
            }
            BackupNote n;
            using (var s = e.Open()) n = JsonSerializer.Deserialize<BackupNote>(s, JsonFile.Options);
            if (!string.Equals(n?.App, ServerApp.Name, StringComparison.OrdinalIgnoreCase))
                throw new IOException("That's a backup of " + (n?.App ?? "another app") + ", not " + ServerApp.Name + ".");
            if (Newer(n.Version, _version))
                throw new IOException("That backup is from " + ServerApp.Name + " " + n.Version + ", newer than this one (" + _version + "). Update first, then restore it.");
            return n;
        }
    }

    static bool Newer(string a, string b)
    {
        return Version.TryParse(a, out var va) && Version.TryParse(b, out var vb) && va > vb;
    }

    /// <summary>Checks a backup and puts it aside for the next start. The caller restarts the app.</summary>
    public void Restore(string zipPath)
    {
        Check(zipPath);
        File.Copy(zipPath, Pending, true);
        Log.Write("Restoring " + Path.GetFileName(zipPath) + " at the next start");
    }

    /// <summary>
    /// At start - after Paths.Init, before anything reads the data folder (settings, secret.key): a backup waiting to be restored goes in
    /// place. What was there is kept first as backups/manual/&lt;app&gt;_before_restore_….zip; then everything but the backups and the logs
    /// is cleared and the zip's files come out (never outside the data folder; owner-only on Linux).
    /// </summary>
    public static void ApplyPending(string version)
    {
        if (!File.Exists(Pending)) return;
        try
        {
            string dir = Path.Combine(Folder, "manual");
            Directory.CreateDirectory(dir);
            string keep = Path.Combine(dir, ServerApp.Lower + "_before_restore_" + DateTime.Now.ToString("yyyy.MM.dd_HH.mm.ss") + ".zip");
            Write(keep, version, null, null);

            string root = Path.GetFullPath(Paths.Data);
            foreach (var d in Directory.EnumerateDirectories(root))
                if (!Skipped(Path.GetFileName(d), AlwaysSkip)) Directory.Delete(d, true);
            foreach (var f in Directory.EnumerateFiles(root))
                if (!Skipped(Path.GetFileName(f), AlwaysSkip)) File.Delete(f);

            using (var z = ZipFile.OpenRead(Pending))
                foreach (var e in z.Entries)
                {
                    if (e.FullName == Note || e.FullName.EndsWith('/') || Skipped(e.FullName, AlwaysSkip)) continue;
                    string dest = Path.GetFullPath(Path.Combine(root, e.FullName));
                    if (!dest.StartsWith(root + Path.DirectorySeparatorChar)) continue;       // never outside the data folder
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    e.ExtractToFile(dest, true);
                    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(dest, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            Log.Write("Restored a backup (what was there is in backups/manual)");
        }
        catch (Exception ex) { Log.Write("Restoring the backup failed: " + ex.Message); }
        finally { try { File.Delete(Pending); } catch { } }
    }

    // ---------- every day ----------

    public async Task RunLoop(CancellationToken stop)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(10), stop); } catch (OperationCanceledException) { return; }
        while (!stop.IsCancellationRequested)
        {
            var last = List().FirstOrDefault(e => e.Type == "scheduled");
            if (last == null || last.Time < DateTime.UtcNow.AddDays(-1))
                try { Create("scheduled"); } catch (Exception ex) { Log.Write("Backup: " + ex.Message); }
            try { await Task.Delay(TimeSpan.FromHours(1), stop); } catch (OperationCanceledException) { return; }
        }
    }
}
