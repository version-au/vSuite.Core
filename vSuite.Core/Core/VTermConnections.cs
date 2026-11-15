using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace vSuite
{
    /// <summary>
    /// A saved SSH connection as vTerm keeps it (its settings.json "Connections") - the same JSON names as vTerm's TermConnection, read
    /// only. vPad uses it for its Servers; vShell reads the same file into its own (plugin API) TermConnection through Read&lt;T&gt;.
    /// </summary>
    public sealed class VTermConnection
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Folder { get; set; } = "";
        public string Host { get; set; } = "";
        public int Port { get; set; } = 22;
        public string User { get; set; } = "";
        /// <summary>"Password", "Key" (KeyFile) or "Agent" (the default keys in ~/.ssh).</summary>
        public string Auth { get; set; } = "Password";
        public string KeyFile { get; set; } = "";
        /// <summary>The password / key passphrase, encrypted for this Windows user (Secret: "dpapi:…").</summary>
        public string Secret { get; set; } = "";
        public string Jump { get; set; } = "";
        public string Command { get; set; } = "";
        public string Color { get; set; } = "";
        /// <summary>The folder vTerm's file panel starts in ("" = home).</summary>
        public string SftpPath { get; set; } = "";
        /// <summary>The server key vTerm trusted (SHA256).</summary>
        public string HostKey { get; set; } = "";

        /// <summary>user@host</summary>
        public string Target { get { return (string.IsNullOrWhiteSpace(User) ? "" : User.Trim() + "@") + (Host ?? "").Trim(); } }
        public string Shown { get { return string.IsNullOrWhiteSpace(Name) ? Target : Name; } }

        public void Normalize()
        {
            Id = Id ?? ""; Name = Name ?? ""; Folder = (Folder ?? "").Trim(); Host = (Host ?? "").Trim(); User = (User ?? "").Trim();
            if (Port <= 0 || Port > 65535) Port = 22;
            if (Auth != "Key" && Auth != "Agent") Auth = "Password";
            KeyFile = KeyFile ?? ""; Secret = Secret ?? ""; Jump = (Jump ?? "").Trim(); Command = Command ?? ""; Color = Color ?? "";
            SftpPath = SftpPath ?? ""; HostKey = HostKey ?? "";
        }
    }

    /// <summary>
    /// vTerm's saved connections, read from its settings file (%APPDATA%\vTerm, or VTERM_DATA_DIR) - again only when the file changed. Never
    /// written: vTerm owns them. Before 1.0.27 vShell and vPad each had their own copy of this.
    /// </summary>
    public static class VTermConnections
    {
        static readonly object _lock = new object();
        static string _readFile, _json;
        static DateTime _readTime;
        static readonly Dictionary<Type, Tuple<string, object>> _parsed = new Dictionary<Type, Tuple<string, object>>();

        /// <summary>vTerm's settings file (VTERM_DATA_DIR for tests).</summary>
        public static string SettingsFile
        {
            get
            {
                string dir = Environment.GetEnvironmentVariable("VTERM_DATA_DIR");
                return string.IsNullOrWhiteSpace(dir) ? SuiteApps.SettingsFile("vTerm") : Path.Combine(dir, "settings.json");
            }
        }

        /// <summary>vTerm has its settings here (it's installed and has started).</summary>
        public static bool Exists { get { return File.Exists(SettingsFile); } }

        /// <summary>The "Connections" array as JSON (read again when the file changed); null = no file (or no list in it).</summary>
        static string ConnectionsJson()
        {
            string file = SettingsFile;
            if (!File.Exists(file)) return null;
            lock (_lock)
            {
                DateTime t = File.GetLastWriteTimeUtc(file);
                if (_json != null && t == _readTime && file == _readFile) return _json;
                string text;
                // shared read: vTerm may be writing it
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs))
                    text = sr.ReadToEnd();
                string json = null;
                using (JsonDocument doc = JsonDocument.Parse(text))
                {
                    JsonElement c;
                    if (doc.RootElement.TryGetProperty("Connections", out c) && c.ValueKind == JsonValueKind.Array) json = c.GetRawText();
                }
                _json = json;
                _readTime = t;
                _readFile = file;
                return json;
            }
        }

        /// <summary>
        /// The connections as the caller's own type (the same JSON names - vShell's plugin API TermConnection), or null when vTerm has no
        /// settings yet. The same list object until the file changes. Throws when the file can't be read.
        /// </summary>
        public static List<T> Read<T>() where T : class
        {
            string json = ConnectionsJson();
            if (json == null) return null;
            lock (_lock)
            {
                Tuple<string, object> p;
                if (_parsed.TryGetValue(typeof(T), out p) && ReferenceEquals(p.Item1, json)) return (List<T>)p.Item2;
                List<T> list = JsonSerializer.Deserialize<List<T>>(json) ?? new List<T>();
                list.RemoveAll(x => x == null);
                _parsed[typeof(T)] = Tuple.Create(json, (object)list);
                return list;
            }
        }

        /// <summary>Every saved connection with an address and an id, by folder and name (empty when vTerm isn't set up or can't be read).</summary>
        public static List<VTermConnection> All
        {
            get
            {
                try
                {
                    List<VTermConnection> raw = Read<VTermConnection>();
                    if (raw == null) return new List<VTermConnection>();
                    foreach (VTermConnection c in raw) c.Normalize();
                    return raw.Where(c => c.Host.Length > 0 && c.Id.Length > 0)
                              .OrderBy(c => c.Folder, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Shown, StringComparer.OrdinalIgnoreCase).ToList();
                }
                catch (Exception ex) { Log.Write("vTerm's connections: " + ex.Message); return new List<VTermConnection>(); }
            }
        }

        public static VTermConnection Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return All.FirstOrDefault(c => c.Id == id);
        }
    }
}
