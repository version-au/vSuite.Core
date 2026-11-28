using System.IO.Compression;
using System.Text.Json;
using vSuite.Server;

namespace vSuite.Server.Tests;

/// <summary>The shared backups (1.0.51): what goes in, restoring at the next start, other apps' / newer / older backups, Also copy to.</summary>
[Collection("paths")]       // Paths and ServerApp are static
public class BackupTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "vsuite-backup-test-" + Guid.NewGuid().ToString("N"));
    readonly string _data, _copy;
    sealed class Settings : IBackupSettings { public string BackupCopyTo { get; set; } }
    readonly Settings _s = new();

    public BackupTests()
    {
        _data = Path.Combine(_dir, "data");
        _copy = Path.Combine(_dir, "nas");
        Directory.CreateDirectory(_copy);
        ServerApp.Init("vTest", "VTEST", "vt_");
        Paths.Init(_data);
        File.WriteAllText(Paths.File("settings.json"), "{\"a\":1}");
        File.WriteAllText(Paths.File("secret.key"), "KEY");
        File.WriteAllText(Paths.File("id_vtest"), "SSH");
        File.WriteAllText(Paths.File("other.log"), "log");
        File.WriteAllText(Paths.File("cache.bin"), "cache");
        Directory.CreateDirectory(Paths.File("versions/web"));
        File.WriteAllText(Paths.File("versions/web/1.json"), "v1");
        File.WriteAllText(Paths.File("versions/web/old.log"), "log");
        Directory.CreateDirectory(Paths.File("app.new"));
        File.WriteAllText(Paths.File("app.new/vtest"), "exe");
        Directory.CreateDirectory(Paths.File("app"));
        File.WriteAllText(Paths.File("app/vtest"), "running exe");
        File.WriteAllText(Paths.File("update-failed"), "0.9");
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    static List<string> Names(string zip) { using var z = ZipFile.OpenRead(zip); return z.Entries.Select(e => e.FullName).OrderBy(n => n).ToList(); }

    [Fact]
    public void A_backup_is_the_whole_data_folder_but_logs_backups_and_updates()
    {
        var b = new Backups("1.2.3", () => _s) { Skip = new[] { "cache.bin" } };
        var e = b.Create("manual");
        Assert.StartsWith("vtest_backup_v1.2.3_", e.Name);
        Assert.Equal(new[] { "id_vtest", "secret.key", "settings.json", "versions/web/1.json", Backups.Note }.OrderBy(n => n), Names(e.Path));
        // a second one doesn't take the first in
        var e2 = b.Create("manual");
        Assert.DoesNotContain(Names(e2.Path), n => n.StartsWith("backups"));
        Assert.Equal(2, b.List().Count);
        var n = b.Check(e.Path);
        Assert.Equal("vTest", n.App);
        Assert.Equal("1.2.3", n.Version);
    }

    [Fact]
    public void Restoring_puts_it_all_back_at_the_next_start_and_keeps_what_was_there()
    {
        var b = new Backups("1.2.3", () => _s);
        var e = b.Create("manual");
        File.WriteAllText(Paths.File("settings.json"), "{\"a\":2}");
        File.WriteAllText(Paths.File("secret.key"), "NEW KEY");      // a reinstall made a new one
        File.WriteAllText(Paths.File("extra.json"), "x");
        Directory.Delete(Paths.File("versions"), true);

        b.Restore(e.Path);
        Assert.True(File.Exists(Paths.File("restore.zip")));
        Assert.Equal("NEW KEY", File.ReadAllText(Paths.File("secret.key")));     // nothing changes until the next start
        Backups.ApplyPending("1.2.3");

        Assert.False(File.Exists(Paths.File("restore.zip")));
        Assert.Equal("{\"a\":1}", File.ReadAllText(Paths.File("settings.json")));
        Assert.Equal("KEY", File.ReadAllText(Paths.File("secret.key")));
        Assert.Equal("SSH", File.ReadAllText(Paths.File("id_vtest")));
        Assert.Equal("v1", File.ReadAllText(Paths.File("versions/web/1.json")));
        Assert.False(File.Exists(Paths.File("extra.json")));
        Assert.False(File.Exists(Paths.File(Backups.Note)));
        Assert.Equal("log", File.ReadAllText(Paths.File("other.log")));           // logs stay
        Assert.Equal("running exe", File.ReadAllText(Paths.File("app/vtest")));     // the program itself stays (1.0.51 cleared it)
        Assert.True(File.Exists(Paths.File("update-failed")));
        // what was there is kept, with the new key and the extra file
        var before = b.List().Single(x => x.Name.Contains("_before_restore_"));
        Assert.Contains("extra.json", Names(before.Path));
        Assert.Contains(b.List(), x => x.Name == e.Name);                          // the backups stay too
    }

    [Fact]
    public void Other_apps_newer_versions_and_non_backups_are_refused_and_older_ones_taken()
    {
        var b = new Backups("1.2.3", () => _s) { Legacy = "vtest.db" };
        string Zip(string name, Action<ZipArchive> fill) { string p = Path.Combine(_dir, name); using (var z = ZipFile.Open(p, ZipArchiveMode.Create)) fill(z); return p; }
        void NoteOf(ZipArchive z, string app, string ver) { using var s = new StreamWriter(z.CreateEntry(Backups.Note).Open()); s.Write(JsonSerializer.Serialize(new { App = app, Version = ver })); }

        Assert.Contains("vOther", Assert.Throws<IOException>(() => b.Check(Zip("o.zip", z => NoteOf(z, "vOther", "1.0.0")))).Message);
        Assert.Contains("newer than this one", Assert.Throws<IOException>(() => b.Check(Zip("n.zip", z => NoteOf(z, "vTest", "1.3.0")))).Message);
        Assert.NotNull(b.Check(Zip("ok.zip", z => NoteOf(z, "vtest", "1.0.9"))));
        Assert.Null(b.Check(Zip("legacy.zip", z => z.CreateEntry("vtest.db"))));
        Assert.Contains("isn't a vTest backup", Assert.Throws<IOException>(() => b.Check(Zip("empty.zip", z => z.CreateEntry("other.txt")))).Message);
        File.WriteAllText(Path.Combine(_dir, "text.zip"), "not a zip");
        Assert.Contains("not a zip", Assert.Throws<IOException>(() => b.Check(Path.Combine(_dir, "text.zip"))).Message);
        Assert.False(File.Exists(Paths.File("restore.zip")));
    }

    [Fact]
    public void An_older_backup_goes_over_what_is_there_and_keeps_the_key()
    {
        string p = Path.Combine(_dir, "old.zip");
        using (var z = ZipFile.Open(p, ZipArchiveMode.Create))
        {
            using (var s = new StreamWriter(z.CreateEntry("vtest.db").Open())) s.Write("old db");
            using (var s = new StreamWriter(z.CreateEntry("settings.json").Open())) s.Write("{\"a\":0}");
        }
        new Backups("1.2.3", () => _s) { Legacy = "vtest.db" }.Restore(p);
        Backups.ApplyPending("1.2.3");
        Assert.Equal("old db", File.ReadAllText(Paths.File("vtest.db")));
        Assert.Equal("{\"a\":0}", File.ReadAllText(Paths.File("settings.json")));
        Assert.Equal("KEY", File.ReadAllText(Paths.File("secret.key")));
        Assert.Equal("v1", File.ReadAllText(Paths.File("versions/web/1.json")));
    }

    [Fact]
    public void Nothing_comes_out_outside_the_data_folder()
    {
        string p = Path.Combine(_dir, "evil.zip");
        using (var z = ZipFile.Open(p, ZipArchiveMode.Create))
        {
            using (var s = new StreamWriter(z.CreateEntry(Backups.Note).Open())) s.Write("{\"App\":\"vTest\",\"Version\":\"1.0.0\"}");
            using (var s = new StreamWriter(z.CreateEntry("../outside.txt").Open())) s.Write("x");
            using (var s = new StreamWriter(z.CreateEntry("settings.json").Open())) s.Write("{}");
        }
        new Backups("1.2.3", () => _s).Restore(p);
        Backups.ApplyPending("1.2.3");
        Assert.False(File.Exists(Path.Combine(_dir, "outside.txt")));
        Assert.Equal("{}", File.ReadAllText(Paths.File("settings.json")));
    }

    [Fact]
    public void Each_backup_is_copied_to_the_folder_keeping_the_newest_seven()
    {
        File.WriteAllText(Path.Combine(_copy, "vother_backup_v1.0.0_x.zip"), "someone else's");
        _s.BackupCopyTo = _copy;
        var b = new Backups("1.2.3", () => _s);
        for (int i = 0; i < 9; i++)
        {
            var e = b.Create("manual");
            File.SetLastWriteTimeUtc(Path.Combine(_copy, e.Name), DateTime.UtcNow.AddMinutes(i - 20));
            Thread.Sleep(1100);     // names go by the second
        }
        Assert.Null(b.CopyProblem);
        Assert.NotNull(b.CopiedAt);
        Assert.Equal(7, Directory.GetFiles(_copy, "vtest_backup_v*.zip").Length);
        Assert.True(File.Exists(Path.Combine(_copy, "vother_backup_v1.0.0_x.zip")));
        Assert.Empty(Directory.GetFiles(_copy, "*.part"));

        _s.BackupCopyTo = Path.Combine(_dir, "not-mounted");
        b.Create("manual");
        Assert.Contains("is the share mounted", b.CopyProblem);
        Assert.Contains("There's no folder", Backups.CheckFolder(_s.BackupCopyTo));
        Assert.Contains("whole path", Backups.CheckFolder("relative/dir"));
        Assert.Null(Backups.CheckFolder(_copy));
        Assert.Null(Backups.CheckFolder(""));
    }
}
