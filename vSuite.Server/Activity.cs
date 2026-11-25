using System.Net.Http.Json;

namespace vSuite.Server;

/// <summary>What the Notifier needs from an event: its group, its words, whether it's a problem, and the vShell mark it sets.</summary>
public interface INotifyEvent
{
    /// <summary>Its notification group (a <see cref="NotifyGroup"/> key) - null: not told anywhere, only listed.</summary>
    string Group { get; }
    string Title { get; }
    string Detail { get; }
    /// <summary>A problem goes to Gotify at a higher priority.</summary>
    bool Problem { get; }
    /// <summary>Set by the Notifier before it's saved: vShell's plugin shows it.</summary>
    bool ToVShell { get; set; }
}

/// <summary>
/// Something a server app did or saw - its Activity page, vShell's plugin and vTrace (GET /api/v1/events), and notifications. An app
/// with more to say about an event subclasses it (vDeploy: the stack and guest) and uses <see cref="ActivityLog{T}"/> with its type.
/// </summary>
public class ActivityEvent : INotifyEvent
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; } = DateTimeOffset.Now;
    /// <summary>The app's own word for what happened ("down", "upgrade", "change") - its Activity page picks the icon by it.</summary>
    public string Kind { get; set; }
    /// <summary>The thing it's about (a host, a change - its id), or null.</summary>
    public string HostId { get; set; }
    public string Host { get; set; }
    /// <summary>One line: "Upgraded web".</summary>
    public string Title { get; set; }
    /// <summary>More: why it failed, what changed.</summary>
    public string Detail { get; set; }
    /// <summary>ok, info, warn, bad - the icon's colour.</summary>
    public string Look { get; set; } = "info";
    /// <summary>Its output is kept (activity/&lt;id&gt;.log, <see cref="ActivityLog{T}.Body"/>) - the newest 50.</summary>
    public bool HasBody { get; set; }
    /// <summary>Its notification group (<see cref="Notifier"/>) - null: not told anywhere, only listed.</summary>
    public string Group { get; set; }
    /// <summary>vShell's plugin shows it (its group went to vShell or both when it happened).</summary>
    public bool ToVShell { get; set; }
    /// <summary>The page it's about ("/hosts/web").</summary>
    public string Link { get; set; }

    public virtual bool Problem { get { return Look is "warn" or "bad"; } }
}

/// <summary>
/// activity.json in the data folder: the newest 1,000 events, newest first; the output of the newest 50 in activity/&lt;id&gt;.log.
/// Reads vDeploy's older file too (oldest first, "last" instead of "next") and saves it the new way.
/// </summary>
public class ActivityLog<T> where T : ActivityEvent
{
    sealed class FileShape
    {
        public long Next { get; set; } = 1;
        /// <summary>vDeploy's older files: the last id given.</summary>
        public long Last { get; set; }
        public List<T> Events { get; set; } = new();
    }

    public const int Keep = 1000, KeepBodies = 50;
    readonly object _gate = new();
    readonly string _path, _dir;
    readonly FileShape _f;
    /// <summary>After an event is saved (the Notifier sends it to Gotify).</summary>
    public event Action<T> Written;
    /// <summary>Before an event is saved (the Notifier marks ToVShell).</summary>
    public Action<T> Before { get; set; }

    public ActivityLog(string path = null)
    {
        _path = path ?? Paths.File("activity.json");
        _dir = Path.Combine(Path.GetDirectoryName(_path)!, "activity");
        _f = JsonFile.Load<FileShape>(_path);
        _f.Events ??= new();
        // an older file (vDeploy before vSuite.Core 1.0.47): oldest first and the last id given
        if (_f.Events.Count > 1 && _f.Events[0].Id < _f.Events[^1].Id) _f.Events.Reverse();
        long top = _f.Events.Count > 0 ? _f.Events.Max(e => e.Id) : 0;
        _f.Next = Math.Max(_f.Next, Math.Max(_f.Last, top) + 1);
        _f.Last = 0;
    }

    /// <summary>Every event, newest first.</summary>
    public List<T> All { get { lock (_gate) return _f.Events.ToList(); } }

    /// <summary>Adds an event (its id and, if unset, its time) with its output if it has one; the oldest beyond 1,000 go.</summary>
    public T Write(T e, string body = null)
    {
        try { Before?.Invoke(e); } catch (Exception ex) { Log.Write("Before an activity event: " + ex.Message); }
        lock (_gate)
        {
            e.Id = _f.Next++;
            if (e.At == default) e.At = DateTimeOffset.Now;
            if (!string.IsNullOrEmpty(body)) SaveBody(e, body);
            _f.Events.Insert(0, e);
            if (_f.Events.Count > Keep) _f.Events.RemoveRange(Keep, _f.Events.Count - Keep);
            TrimBodies();
            try { JsonFile.Save(_path, _f); } catch (Exception ex) { Log.Write("Couldn't save the activity: " + ex.Message); }
        }
        try { Written?.Invoke(e); } catch (Exception ex) { Log.Write("After an activity event: " + ex.Message); }
        return e;
    }

    /// <summary>Keeps the output of an event already in the log (moving an older app's inline output into a file).</summary>
    public void KeepBody(long id, string body)
    {
        if (string.IsNullOrEmpty(body)) return;
        lock (_gate)
        {
            var e = _f.Events.FirstOrDefault(x => x.Id == id);
            if (e == null) return;
            SaveBody(e, body);
            TrimBodies();
            try { JsonFile.Save(_path, _f); } catch (Exception ex) { Log.Write("Couldn't save the activity: " + ex.Message); }
        }
    }

    void SaveBody(T e, string body)
    {
        try
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(Path.Combine(_dir, e.Id + ".log"), body);
            e.HasBody = true;
        }
        catch (Exception ex) { Log.Write("Couldn't keep an activity's output: " + ex.Message); }
    }

    // only the newest bodies stay
    void TrimBodies()
    {
        foreach (var old in _f.Events.Where(x => x.HasBody).Skip(KeepBodies).ToList())
        {
            try { File.Delete(Path.Combine(_dir, old.Id + ".log")); } catch { }
            old.HasBody = false;
        }
    }

    /// <summary>The newest event's id (0 when there are none) - where an app that polls has got to.</summary>
    public long Last { get { lock (_gate) return _f.Events.Count == 0 ? 0 : _f.Events[0].Id; } }

    /// <summary>Events after this id, oldest first (at most limit, 1 to 1,000).</summary>
    public List<T> After(long id, int limit = 200)
    {
        lock (_gate) return _f.Events.Where(e => e.Id > id).Reverse().Take(Math.Clamp(limit, 1, Keep)).ToList();
    }

    /// <summary>The newest n (that match, if a test is given), newest first.</summary>
    public List<T> Newest(int n = 200, Func<T, bool> where = null)
    {
        lock (_gate) return _f.Events.Where(where ?? (_ => true)).Take(n).ToList();
    }

    public T Get(long id) { lock (_gate) return _f.Events.FirstOrDefault(e => e.Id == id); }

    /// <summary>An event's kept output, or null.</summary>
    public string Body(long id)
    {
        try { string f = Path.Combine(_dir, id + ".log"); return File.Exists(f) ? File.ReadAllText(f) : null; } catch { return null; }
    }
}

/// <summary>The log of plain <see cref="ActivityEvent"/>s - what most apps use.</summary>
public sealed class ActivityLog : ActivityLog<ActivityEvent>
{
    public ActivityLog(string path = null) : base(path) { }
}

/// <summary>The settings the Notifier reads: where each group goes, and Gotify (the token kept with <see cref="Secrets"/>).</summary>
public interface INotifySettings
{
    /// <summary>Group key -> route ("off", "gotify", "vshell", "both"); a group not in it has its default.</summary>
    Dictionary<string, string> Notify { get; set; }
    string GotifyUrl { get; set; }
    /// <summary>Protected (<see cref="Secrets.Protect"/>).</summary>
    string GotifyToken { get; set; }
}

/// <summary>A kind of notification: its key, what Options › Notifications says about it, and where it goes until you choose.</summary>
public sealed record NotifyGroup(string Key, string Title, string Hint, string Default);

/// <summary>
/// Where each kind of notification goes: off, Gotify, vShell (its plugin reads the events marked ToVShell) or both. Every event is on
/// the Activity page whatever is chosen. Options › Notifications is <c>vSuite.Web.Parts.NotificationsOptions</c>.
/// </summary>
public sealed class Notifier
{
    public static readonly KeyValuePair<string, string>[] Routes = { new("off", "Off"), new("gotify", "Gotify"), new("vshell", "vShell"), new("both", "Both") };

    readonly Func<INotifySettings> _settings;
    readonly HttpClient _http;

    /// <summary>The app's name - Gotify's titles start "vProxy: ".</summary>
    public string App { get; }
    /// <summary>The app's groups, in the order Options lists them.</summary>
    public IReadOnlyList<NotifyGroup> Groups { get; }

    public Notifier(string app, IReadOnlyList<NotifyGroup> groups, Func<INotifySettings> settings, HttpClient http = null)
    {
        App = app;
        Groups = groups;
        _settings = settings;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    /// <summary>Hooks it to a log: marks ToVShell before an event is saved, sends to Gotify after.</summary>
    public void Watch<T>(ActivityLog<T> log) where T : ActivityEvent
    {
        log.Before = e => Mark(e);
        log.Written += e => Send(e);
    }

    public static string RouteOf(INotifySettings s, IReadOnlyList<NotifyGroup> groups, string group)
    {
        if (group == null) return "off";
        if (s?.Notify != null && s.Notify.TryGetValue(group, out var r) && Routes.Any(x => x.Key == r)) return r;
        return groups.FirstOrDefault(g => g.Key == group)?.Default ?? "off";
    }

    /// <summary>Where a group goes now.</summary>
    public string RouteOf(string group) { return RouteOf(_settings(), Groups, group); }

    /// <summary>Before an event is saved: does vShell show it?</summary>
    public void Mark(INotifyEvent e) { e.ToVShell = RouteOf(e.Group) is "vshell" or "both"; }

    /// <summary>After it's saved: to Gotify when its group goes there and Gotify is set up (in the background - never waits).</summary>
    public void Send(INotifyEvent e)
    {
        var s = _settings();
        if (RouteOf(s, Groups, e.Group) is not ("gotify" or "both") || !GotifyReady(s)) return;
        _ = Task.Run(async () =>
        {
            try { await Gotify(e.Title, e.Detail, e.Problem ? 8 : 4); }
            catch (Exception ex) { Log.Write("Gotify didn't take a notification: " + ex.Message); }
        });
    }

    public static bool GotifyReady(INotifySettings s) { return !string.IsNullOrWhiteSpace(s?.GotifyUrl) && !string.IsNullOrWhiteSpace(s?.GotifyToken); }
    public bool GotifyReady() { return GotifyReady(_settings()); }

    /// <summary>A message to Gotify (also Options' Send a test). Throws a <see cref="HostProblem"/> with the reason when it doesn't take it.</summary>
    public async Task Gotify(string title, string message, int priority = 5)
    {
        var s = _settings();
        if (!GotifyReady(s)) throw new HostProblem("Gotify isn't set up - its address and an app token.");
        string url = s.GotifyUrl.Trim().TrimEnd('/');
        if (!url.Contains("://")) url = "http://" + url;
        using var req = new HttpRequestMessage(HttpMethod.Post, url + "/message");
        req.Headers.Add("X-Gotify-Key", Secrets.Unprotect(s.GotifyToken));
        req.Content = JsonContent.Create(new { title = App + ": " + title, message = string.IsNullOrWhiteSpace(message) ? title : message, priority });
        HttpResponseMessage res;
        try { res = await _http.SendAsync(req); }
        catch (HttpRequestException ex) { throw new HostProblem("Couldn't reach Gotify at " + url + ": " + ex.Message, ex); }
        catch (TaskCanceledException ex) { throw new HostProblem("Gotify at " + url + " didn't answer within 15 seconds.", ex); }
        using (res)
            if (!res.IsSuccessStatusCode)
                throw new HostProblem((int)res.StatusCode is 401 or 403 ? "Gotify refused the app token." : "Gotify said " + (int)res.StatusCode + ".");
    }
}
