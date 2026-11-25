using System.Net;
using vSuite.Server;

namespace vSuite.Server.Tests;

/// <summary>The shared Activity log and Notifier (1.0.47): ids, order, bodies, vDeploy's older file, routes, Gotify.</summary>
public class ActivityTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "vsuite-server-test-" + Guid.NewGuid().ToString("N"));
    string File_ => Path.Combine(_dir, "activity.json");
    public ActivityTests() { Directory.CreateDirectory(_dir); }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    sealed class Settings : INotifySettings
    {
        public Dictionary<string, string> Notify { get; set; } = new();
        public string GotifyUrl { get; set; }
        public string GotifyToken { get; set; }
        public string DiscordWebhook { get; set; }
    }

    static readonly NotifyGroup[] Groups = { new("down", "A host stops answering", "", "both"), new("changes", "A change", "", "off"), new("news", "News", "", "vshell") };

    [Fact]
    public void Events_get_ids_newest_first_and_survive_a_restart()
    {
        var log = new ActivityLog(File_);
        for (int i = 1; i <= 3; i++) log.Write(new ActivityEvent { Kind = "x", Title = "e" + i });
        Assert.Equal(new[] { "e3", "e2", "e1" }, log.All.Select(e => e.Title));
        Assert.Equal(3, log.Last);
        Assert.Equal(new[] { "e2", "e3" }, log.After(1).Select(e => e.Title));      // oldest first
        Assert.Equal(new[] { "e3" }, log.Newest(1).Select(e => e.Title));
        Assert.Equal("e2", log.Get(2).Title);

        var again = new ActivityLog(File_);
        Assert.Equal(4, again.Write(new ActivityEvent { Title = "e4" }).Id);
    }

    [Fact]
    public void Only_the_newest_bodies_stay()
    {
        var log = new ActivityLog(File_);
        for (int i = 0; i < ActivityLog.KeepBodies + 2; i++) log.Write(new ActivityEvent { Title = "run" }, "output " + i);
        Assert.Equal(ActivityLog.KeepBodies, log.All.Count(e => e.HasBody));
        Assert.Null(log.Body(1));
        Assert.Equal("output " + (ActivityLog.KeepBodies + 1), log.Body(ActivityLog.KeepBodies + 2));
    }

    // vDeploy's events carried more, and its file was oldest first with "last"
    sealed class DeployEvent : ActivityEvent
    {
        public string Stack { get; set; }
        public bool Ok { get; set; } = true;
        public string Body { get; set; }
        public override bool Problem { get { return !Ok; } }
    }

    [Fact]
    public void An_older_vdeploy_file_is_read_and_its_outputs_moved()
    {
        File.WriteAllText(File_, "{\"last\":7,\"events\":[{\"id\":6,\"kind\":\"deploy\",\"stack\":\"web\",\"title\":\"old\",\"ok\":true,\"body\":\"compose up\"},{\"id\":7,\"kind\":\"deploy\",\"stack\":\"db\",\"title\":\"newer\",\"ok\":false}]}");
        var log = new ActivityLog<DeployEvent>(File_);
        Assert.Equal(new[] { "newer", "old" }, log.All.Select(e => e.Title));
        Assert.True(log.Get(7).Problem);
        foreach (var e in log.All.Where(e => e.Body != null)) { log.KeepBody(e.Id, e.Body); e.Body = null; }
        Assert.Equal("compose up", log.Body(6));
        Assert.Equal(8, log.Write(new DeployEvent { Stack = "web", Title = "next" }).Id);
        Assert.Equal("web", new ActivityLog<DeployEvent>(File_).Get(8).Stack);
    }

    [Fact]
    public void Routes_default_per_group_and_mark_vshell()
    {
        var s = new Settings();
        var n = new Notifier("vTest", Groups, () => s);
        Assert.Equal("both", n.RouteOf("down"));
        Assert.Equal("off", n.RouteOf("unknown"));
        Assert.Equal("off", n.RouteOf(null));
        s.Notify["down"] = "gotify";
        s.Notify["changes"] = "nonsense";
        Assert.Equal("gotify", n.RouteOf("down"));
        Assert.Equal("off", n.RouteOf("changes"));

        var log = new ActivityLog(File_);
        n.Watch(log);
        Assert.False(log.Write(new ActivityEvent { Group = "down" }).ToVShell);     // gotify only
        Assert.True(log.Write(new ActivityEvent { Group = "news" }).ToVShell);
        Assert.False(log.Write(new ActivityEvent()).ToVShell);
    }

    sealed class FakeGotify : HttpMessageHandler
    {
        public List<(string Url, string Key, string Body)> Got { get; } = new();
        public HttpStatusCode Answer { get; set; } = HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Got.Add((r.RequestUri.ToString(), r.Headers.GetValues("X-Gotify-Key").First(), await r.Content.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(Answer);
        }
    }

    [Fact]
    public async Task Gotify_gets_the_app_name_and_says_why_it_failed()
    {
        var s = new Settings { GotifyUrl = "gotify.lan:8070/", GotifyToken = "Atoken" };   // a plain token reads back as itself
        var fake = new FakeGotify();
        var n = new Notifier("vTest", Groups, () => s, new HttpClient(fake));
        Assert.True(n.GotifyReady());
        await n.Gotify("a test", "hello");
        Assert.Equal("http://gotify.lan:8070/message", fake.Got[0].Url);
        Assert.Equal("Atoken", fake.Got[0].Key);
        Assert.Contains("\"title\":\"vTest: a test\"", fake.Got[0].Body);

        fake.Answer = HttpStatusCode.Unauthorized;
        var ex = await Assert.ThrowsAsync<HostProblem>(() => n.Gotify("a test", null));
        Assert.Equal("Gotify refused the app token.", ex.Message);

        s.GotifyUrl = "";
        Assert.False(n.GotifyReady());
        await Assert.ThrowsAsync<HostProblem>(() => n.Gotify("x", null));
    }

    [Fact]
    public void Choices_from_before_discord_keep_their_meaning_and_new_ones_mix()
    {
        Assert.Equal(NotifyTo.Gotify | NotifyTo.Discord | NotifyTo.VShell, Notifier.Parse("both"));
        Assert.Equal(NotifyTo.Gotify | NotifyTo.Discord, Notifier.Parse("gotify"));
        Assert.Equal(NotifyTo.VShell, Notifier.Parse("vshell"));
        Assert.Equal(NotifyTo.None, Notifier.Parse("off"));
        Assert.Null(Notifier.Parse("nonsense"));
        Assert.Null(Notifier.Parse("to:gotify,pigeon"));
        Assert.Equal("to:discord,vshell", Notifier.Write(NotifyTo.VShell | NotifyTo.Discord));
        Assert.Equal("off", Notifier.Write(NotifyTo.None));
        Assert.Equal(NotifyTo.Gotify, Notifier.Parse(Notifier.Write(NotifyTo.Gotify)));

        var s = new Settings();
        var n = new Notifier("vTest", Groups, () => s);
        Assert.Equal(NotifyTo.Gotify | NotifyTo.Discord | NotifyTo.VShell, n.To("down"));     // default "both"
        n.Set("down", NotifyTo.Discord);
        Assert.Equal("to:discord", s.Notify["down"]);
        Assert.Equal(NotifyTo.Discord, n.To("down"));
        var e = new ActivityEvent { Group = "down", ToVShell = true };
        n.Mark(e);
        Assert.False(e.ToVShell);
    }

    [Fact]
    public async Task Discord_gets_the_app_name_and_red_for_a_problem()
    {
        Assert.NotNull(Notifier.DiscordProblem("http://discord.com/api/webhooks/1/x"));
        Assert.NotNull(Notifier.DiscordProblem("https://example.com/api/webhooks/1/x"));
        Assert.Null(Notifier.DiscordProblem("https://discord.com/api/webhooks/123/abc"));

        var s = new Settings { DiscordWebhook = "https://discord.com/api/webhooks/123/abc" };
        var fake = new FakeDiscord();
        var n = new Notifier("vTest", Groups, () => s, new HttpClient(fake));
        Assert.True(n.DiscordReady());
        await n.Discord("Web is down", "Not answering", problem: true);
        Assert.Equal("https://discord.com/api/webhooks/123/abc", fake.Got[0].Url);
        Assert.Contains("\"username\":\"vTest\"", fake.Got[0].Body);
        Assert.Contains("\"title\":\"Web is down\"", fake.Got[0].Body);
        Assert.Contains("\"color\":" + 0xE74C3C, fake.Got[0].Body);

        fake.Answer = HttpStatusCode.NotFound;
        var ex = await Assert.ThrowsAsync<HostProblem>(() => n.Discord("x", null));
        Assert.Contains("deleted", ex.Message);
    }

    sealed class FakeDiscord : HttpMessageHandler
    {
        public List<(string Url, string Body)> Got { get; } = new();
        public HttpStatusCode Answer { get; set; } = HttpStatusCode.NoContent;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Got.Add((r.RequestUri.ToString(), await r.Content.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(Answer);
        }
    }
}
