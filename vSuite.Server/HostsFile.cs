using System.Text.Json;
using System.Text.Json.Serialization;

namespace vSuite.Server;

/// <summary>
/// The hosts as a file one vSuite server app exports and another imports (Options > General > Hosts in vDeploy, vPatch and vTrace):
/// where each host is and the SSH host key the app saw - never a user, password or key. The app importing signs in its own way; the
/// host key lets it check it reached the same machine before a password is sent. vDeploy's older export ("vdeploy-hosts") reads too.
/// </summary>
public static class HostsFile
{
    public const string Kind = "vsuite-hosts";
    public const int Format = 1;

    public sealed class Entry
    {
        public string Name { get; set; }
        public string Address { get; set; }
        public int Port { get; set; } = 22;
        /// <summary>"SHA256:…" as the exporting app saw it, or null.</summary>
        public string HostKey { get; set; }
        public string Os { get; set; }
        public string Arch { get; set; }
    }

    public sealed class Doc
    {
        public string Kind { get; set; } = HostsFile.Kind;
        public int Format { get; set; } = HostsFile.Format;
        public string From { get; set; }
        public DateTimeOffset Made { get; set; }
        public List<Entry> Hosts { get; set; } = new();
    }

    static readonly JsonSerializerOptions Out = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The file's text: these hosts, from this app ("vTrace 0.1.0").</summary>
    public static string Json(IEnumerable<Entry> hosts, string from, DateTimeOffset made)
    {
        var doc = new Doc
        {
            From = from,
            Made = made,
            Hosts = hosts.Select(h => new Entry
            {
                Name = h.Name, Address = h.Address, Port = h.Port is > 0 and <= 65535 ? h.Port : 22,
                HostKey = string.IsNullOrWhiteSpace(h.HostKey) ? null : h.HostKey,
                Os = string.IsNullOrWhiteSpace(h.Os) ? null : h.Os,
                Arch = string.IsNullOrWhiteSpace(h.Arch) ? null : h.Arch,
            }).ToList(),
        };
        return JsonSerializer.Serialize(doc, Out);
    }

    /// <summary>"vtrace-hosts-2026-11-15.json"</summary>
    public static string FileName(string app, DateTimeOffset made) { return (app ?? "vsuite").ToLowerInvariant() + "-hosts-" + made.ToString("yyyy-MM-dd") + ".json"; }

    /// <summary>The hosts in a file, where it came from ("vDeploy 0.1.17"), or a problem in words (not a hosts file, a newer format, broken JSON).</summary>
    public static (List<Entry> Hosts, string From, string Problem) Read(string json)
    {
        Doc d;
        try { d = JsonSerializer.Deserialize<Doc>(json ?? "", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch (JsonException) { return (null, null, "That file isn't JSON - choose a file another vSuite app exported (…-hosts-….json)."); }
        if (d == null || (d.Kind != Kind && d.Kind != "vdeploy-hosts"))
            return (null, null, "That isn't a hosts file - choose one that vDeploy, vPatch or vTrace exported (Options > General > Hosts).");
        if (d.Format > Format) return (null, null, "That file is from a newer app (format " + d.Format + ") - update this one, then try again.");
        var hosts = (d.Hosts ?? new()).Where(h => h != null && !string.IsNullOrWhiteSpace(h.Address)).ToList();
        foreach (var h in hosts)
        {
            h.Address = h.Address.Trim();
            h.Name = string.IsNullOrWhiteSpace(h.Name) ? h.Address : h.Name.Trim();
            if (h.Port < 1 || h.Port > 65535) h.Port = 22;
            if (string.IsNullOrWhiteSpace(h.HostKey)) h.HostKey = null;
        }
        return (hosts, d.From, hosts.Count == 0 ? "There are no hosts in that file." : null);
    }

    /// <summary>The same place: the address (any case) and the port.</summary>
    public static bool Same(Entry a, string address, int port)
    {
        return a != null && string.Equals(a.Address?.Trim(), address?.Trim(), StringComparison.OrdinalIgnoreCase) && a.Port == (port is > 0 and <= 65535 ? port : 22);
    }
}
