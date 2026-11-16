using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace vSuite.Server;

/// <summary>Where the app keeps its data: PREFIX_DATA_DIR, else /var/lib/&lt;app&gt; (Linux) or %APPDATA%\&lt;App&gt; (Windows, for trying it out).</summary>
public static class Paths
{
    public static string Data { get; private set; }

    public static void Init(string dir = null)
    {
        if (string.IsNullOrWhiteSpace(dir)) dir = ServerApp.Env("DATA_DIR");
        if (string.IsNullOrWhiteSpace(dir))
            dir = OperatingSystem.IsWindows()
                ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ServerApp.Name)
                : "/var/lib/" + ServerApp.Lower;
        Directory.CreateDirectory(dir);
        Data = dir;
    }

    public static string File(string name) { return System.IO.Path.Combine(Data, name); }
}

/// <summary>JSON files written safely: to a .tmp beside them, then moved over (never half a file).</summary>
public static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// The file's object; a new one when there's no file yet. A file that's there but can't be read (cut short by a power cut, a
    /// typo from editing it by hand) is never taken for "nothing yet": a copy is kept as name.broken-&lt;time&gt;, and the last good
    /// save (name.bak) is used instead. Neither readable: a new object - or, with must (the settings and the hosts, where "empty"
    /// would mean "no login yet" and be saved over the real file), the app stops and says which file.
    /// </summary>
    public static T Load<T>(string path, bool must = false) where T : class, new()
    {
        if (!System.IO.File.Exists(path)) return new T();
        string name = System.IO.Path.GetFileName(path);
        try { return Read<T>(path); }
        catch (Exception ex)
        {
            Log.Write("Couldn't read " + name + ": " + ex.Message);
            string kept = path + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            try { System.IO.File.Copy(path, kept, true); if (path.EndsWith("settings.json")) Private(kept); Log.Write("A copy of it is kept as " + System.IO.Path.GetFileName(kept)); }
            catch (Exception ex2) { Log.Write("Couldn't keep a copy of " + name + ": " + ex2.Message); }
            string bak = path + ".bak";
            if (System.IO.File.Exists(bak))
                try
                {
                    T old = Read<T>(bak);
                    Log.Write("Using " + name + ".bak (the save before the last one) instead");
                    return old;
                }
                catch (Exception ex3) { Log.Write("Couldn't read " + name + ".bak either: " + ex3.Message); }
            if (must)
                throw new InvalidDataException(name + " in " + System.IO.Path.GetDirectoryName(path) + " can't be read (" + ex.Message + "), and neither can its .bak. " +
                    "Put a good copy back, or move the file away to start again - " + ServerApp.Name + " won't start with it as it is, so nothing is saved over it.");
            return new T();
        }
    }

    static T Read<T>(string path) where T : class, new()
    {
        string text = System.IO.File.ReadAllText(path, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("the file is empty");
        return JsonSerializer.Deserialize<T>(text, Options) ?? throw new InvalidDataException("the file holds nothing");
    }

    public static void Save<T>(string path, T value, bool secret = false)
    {
        string tmp = path + ".tmp";
        byte[] bytes = new UTF8Encoding(false).GetBytes(JsonSerializer.Serialize(value, Options));
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(bytes, 0, bytes.Length);
            fs.Flush(true);     // on the disk before it takes the old file's place: a power cut can't leave an empty file
        }
        if (secret) Private(tmp);
        // the file as it was stays as name.bak (it was read or written by us, so it's good)
        try { if (System.IO.File.Exists(path)) System.IO.File.Copy(path, path + ".bak", true); } catch { }
        System.IO.File.Move(tmp, path, true);
    }

    /// <summary>Only the owner may read it (Linux; Windows keeps its own folder permissions).</summary>
    public static void Private(string path)
    {
        if (!OperatingSystem.IsWindows())
            try { System.IO.File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { }
    }
}

/// <summary>&lt;app&gt;.log in the data folder (rotates at 1 MB to &lt;app&gt;.old.log), and the console (journalctl / docker logs).</summary>
public static class Log
{
    static readonly object Gate = new();

    public static void Write(string text)
    {
        string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text;
        Console.WriteLine(line);
        if (Paths.Data == null) return;
        lock (Gate)
        {
            try
            {
                string path = Paths.File(ServerApp.Lower + ".log");
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > 1024 * 1024) System.IO.File.Move(path, Paths.File(ServerApp.Lower + ".old.log"), true);
                System.IO.File.AppendAllText(path, line + Environment.NewLine);
            }
            catch { }
        }
    }
}
