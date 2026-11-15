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

    public static T Load<T>(string path) where T : class, new()
    {
        try
        {
            if (System.IO.File.Exists(path))
                return JsonSerializer.Deserialize<T>(System.IO.File.ReadAllText(path, Encoding.UTF8), Options) ?? new T();
        }
        catch (Exception ex) { Log.Write("Couldn't read " + System.IO.Path.GetFileName(path) + ": " + ex.Message); }
        return new T();
    }

    public static void Save<T>(string path, T value, bool secret = false)
    {
        string tmp = path + ".tmp";
        System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options), new UTF8Encoding(false));
        if (secret) Private(tmp);
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
