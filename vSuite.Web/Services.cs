namespace vSuite.Web;

public sealed class TabItem
{
    public string Key { get; set; }    // which tab (Tabs.KeyOf)
    public string Path { get; set; }   // the page it shows now, with its query
    public string Title { get; set; }
    public string Icon { get; set; }
}

/// <summary>
/// The open tabs of one browser tab (remembered in its localStorage by the layout). A tab is a key (KeyOf: which pages share a tab)
/// and the page it shows now. Order is fixed by RankOf: a new tab goes at the end of its group; dragging stays in the group. An app
/// subclasses it with its own KeyOf / RankOf (vDeploy: one tab per host, its stacks inside it).
/// </summary>
public class Tabs
{
    public List<TabItem> Items { get; } = new();
    public string Active { get; private set; }
    public event Action Changed;

    protected void Raise() { Changed?.Invoke(); }

    public static string PathOf(string uriOrPath)
    {
        string p = uriOrPath ?? "/";
        int scheme = p.IndexOf("://", StringComparison.Ordinal);
        if (scheme > 0) { int slash = p.IndexOf('/', scheme + 3); p = slash < 0 ? "/" : p.Substring(slash); }
        int q = p.IndexOfAny(new[] { '?', '#' });
        if (q >= 0) p = p.Substring(0, q);
        return p.Length == 0 ? "/" : p;
    }

    /// <summary>The page's address inside the app, with its query ("/stacks/nuc01/arr?c=sonarr") - what a tab goes back to.</summary>
    public static string RelOf(string uriOrPath)
    {
        string p = uriOrPath ?? "/";
        int scheme = p.IndexOf("://", StringComparison.Ordinal);
        if (scheme > 0) { int slash = p.IndexOf('/', scheme + 3); p = slash < 0 ? "/" : p.Substring(slash); }
        int hash = p.IndexOf('#');
        if (hash >= 0) p = p.Substring(0, hash);
        return p.Length == 0 ? "/" : p;
    }

    /// <summary>Which tab a page belongs to. Here: Options (every page of it) is one tab; each other address its own.</summary>
    public virtual string KeyOf(string uriOrPath)
    {
        string path = PathOf(uriOrPath);
        return path.Trim('/').Split('/')[0] == "options" ? "options" : path;
    }

    /// <summary>Where a tab sits, left to right (lower first). Here: Options first, then the rest.</summary>
    public virtual int RankOf(string key) { return key == "options" ? 0 : 2; }

    /// <summary>A tab was closed or forgotten (an app that keeps something per tab drops it).</summary>
    protected virtual void Closed(string key) { }
    /// <summary>A saved tab is coming back (before it's merged).</summary>
    protected virtual void Restoring(TabItem t) { }

    /// <summary>The first place, and one past the last, a tab of this rank can be (not counting `skip`).</summary>
    (int Lo, int Hi) Group(int rank, TabItem skip = null)
    {
        var rest = Items.Where(x => x != skip).ToList();
        int lo = rest.Count(x => RankOf(x.Key) < rank);
        return (lo, lo + rest.Count(x => RankOf(x.Key) == rank));
    }

    /// <summary>A page shows: its tab (made if needed, at the end of its group) becomes the chosen one and remembers this page.</summary>
    public void Show(string uri, string title, string icon)
    {
        string key = KeyOf(uri);
        var t = Items.FirstOrDefault(x => x.Key == key);
        if (t == null) { t = new TabItem { Key = key }; Items.Insert(Group(RankOf(key)).Hi, t); }
        t.Path = RelOf(uri);
        t.Title = title;
        t.Icon = icon;
        Active = key;
        Raise();
    }

    /// <summary>Close a tab; returns where to go if it was the chosen one (null = stay).</summary>
    public string Close(string key)
    {
        int i = Items.FindIndex(x => x.Key == key);
        if (i < 0) return null;
        Items.RemoveAt(i);
        Closed(key);
        string go = null;
        if (key == Active)
        {
            go = Items.Count == 0 ? Empty : Items[Math.Min(i, Items.Count - 1)].Path;
            Active = null;
        }
        Raise();
        return go;
    }

    public void Move(string key, int to)
    {
        int i = Items.FindIndex(x => x.Key == key);
        if (i < 0) return;
        var t = Items[i];
        var (lo, hi) = Group(RankOf(key), t);
        Items.RemoveAt(i);
        Items.Insert(Math.Max(lo, Math.Min(to, hi)), t);
        Raise();
    }

    /// <summary>The page shown when every tab is closed (the app has a page at /empty).</summary>
    public const string Empty = "/empty";

    /// <summary>The tabs this browser had open, in their order; the page showing now keeps its place among them (or goes last).</summary>
    public void Restore(IEnumerable<TabItem> saved)
    {
        var list = (saved ?? Enumerable.Empty<TabItem>()).Where(t => t?.Path != null && PathOf(t.Path) != Empty).ToList();
        foreach (var t in list) Restoring(t);
        var merged = new List<TabItem>();
        foreach (var t in list)
        {
            t.Key ??= KeyOf(t.Path);   // saved before tabs had keys
            if (merged.Any(x => x.Key == t.Key)) continue;
            merged.Add(Items.FirstOrDefault(x => x.Key == t.Key) ?? t);
        }
        foreach (var t in Items) if (merged.All(x => x.Key != t.Key)) merged.Add(t);
        Items.Clear();
        Items.AddRange(merged.OrderBy(t => RankOf(t.Key)));   // stable: saved order within each group
        Raise();
    }

    /// <summary>A tab's page is gone (a host removed): drop it.</summary>
    public void Forget(Func<string, bool> gone)
    {
        var drop = Items.Where(t => t.Key != Active && gone(t.Path)).ToList();
        foreach (var t in drop) { Items.Remove(t); Closed(t.Key); }
        if (drop.Count > 0) Raise();
    }
}

/// <summary>The status line under the page: facts on the left, a short note on the right.</summary>
public sealed class StatusLine
{
    public string Left { get; private set; }
    public string Right { get; private set; }
    public event Action Changed;

    public void Set(string left, string right)
    {
        if (left == Left && right == Right) return;
        Left = left;
        Right = right;
        Changed?.Invoke();
    }
}

/// <summary>Times in the viewer's time zone (the browser says which), 12-hour.</summary>
public sealed class Clock
{
    TimeZoneInfo _zone = TimeZoneInfo.Local;
    public event Action Changed;

    public void SetZone(string ianaId)
    {
        try { _zone = TimeZoneInfo.FindSystemTimeZoneById(ianaId); ZoneId = ianaId; Changed?.Invoke(); } catch { }
    }

    /// <summary>The viewer's time zone (IANA, "Australia/Sydney"), once the browser has said; null before.</summary>
    public string ZoneId { get; private set; }

    public DateTimeOffset Local(DateTimeOffset t) { return TimeZoneInfo.ConvertTime(t, _zone); }
    public string Time(DateTimeOffset? t) { return t == null ? "" : Local(t.Value).ToString("h:mm tt"); }

    public string Stamp(DateTimeOffset? t)
    {
        if (t == null) return "";
        var l = Local(t.Value);
        var today = Local(DateTimeOffset.Now).Date;
        if (l.Date == today) return l.ToString("h:mm tt");
        if (l.Date == today.AddDays(-1)) return "Yesterday " + l.ToString("h:mm tt");
        return l.ToString("MMM d, h:mm tt");
    }

    public static string Ago(DateTimeOffset? t)
    {
        if (t == null) return "";
        var d = DateTimeOffset.Now - t.Value;
        if (d.TotalSeconds < 10) return "just now";
        if (d.TotalMinutes < 1) return (int)d.TotalSeconds + " s ago";
        if (d.TotalHours < 1) return (int)d.TotalMinutes + " min ago";
        if (d.TotalDays < 1) return (int)d.TotalHours + " h ago";
        return (int)d.TotalDays + (d.TotalDays < 2 ? " day ago" : " days ago");
    }
}

/// <summary>A dangerous action waiting for its confirmation (ConfirmDialog, in the layout).</summary>
public sealed class ConfirmRequest
{
    public string Title { get; set; }
    public string Body { get; set; }
    public string Name { get; set; }           // what to type
    public string Action { get; set; }         // the button: "Remove media-vm"
    public string Mode { get; set; }           // "type", "password" or "both"; null = Options > Sign-in > Dangerous actions
    public bool Danger { get; set; } = true;   // the button in the warning colour
    public Func<Task> Run { get; set; }
}

public sealed class Dialogs
{
    public ConfirmRequest Confirm { get; private set; }
    public event Action Changed;

    public void Ask(ConfirmRequest r) { Confirm = r; Changed?.Invoke(); }
    public void Close() { Confirm = null; Changed?.Invoke(); }
}
