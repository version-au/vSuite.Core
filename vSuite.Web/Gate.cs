using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;

namespace vSuite.Web;

public static class Auth
{
    /// <summary>"vdeploy:stamp" - the sign-in's stamp ("Sign out every device" raises it: older cookies stop working).</summary>
    public static string StampClaim { get { return ServerApp.Lower + ":stamp"; } }
    public static string TrustedClaim { get { return ServerApp.Lower + ":trusted"; } }
    /// <summary>HttpContext.Items key: the name of the app key a request came in with.</summary>
    public static string KeyItem { get { return ServerApp.Lower + ":key"; } }

    public static ClaimsPrincipal Principal(ISignInSettings s, bool trusted)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, s.UserName ?? "you"), new(StampClaim, s.Stamp.ToString()) };
        if (trusted) claims.Add(new Claim(TrustedClaim, "1"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, trusted ? "trusted" : CookieAuthenticationDefaults.AuthenticationScheme));
    }

    /// <summary>The visitor's address. Behind a proxy you listed, the address it forwards (the last one it added); otherwise the connection's.</summary>
    public static IPAddress Client(HttpContext http, ISignInSettings s)
    {
        var ip = IpRange.Plain(http.Connection.RemoteIpAddress);
        if (ip != null && s.TrustedProxies.Any(p => IpRange.Contains(p, ip)))
        {
            string fwd = http.Request.Headers["X-Forwarded-For"].ToString();
            string last = fwd.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
            if (last != null && IPAddress.TryParse(last, out var f)) return IpRange.Plain(f);
            if (last != null && IPEndPoint.TryParse(last, out var fp)) return IpRange.Plain(fp.Address);     // "203.0.113.7:51234"
            return null;    // a proxy that doesn't say who it's passing on: nobody we know - never the proxy's own address
        }
        return ip;
    }

    /// <summary>
    /// Is the name this request was sent to one of the app's own? A page of another site can point its own name at the app's
    /// address (DNS rebinding) and would then look like the app's own page from a trusted address - so an address, a plain name
    /// from the home network (deploy, deploy.lan, .local, .internal, .home.arpa, localhost) or a name somebody has signed in on
    /// with the password counts; any other name has to sign in once.
    /// </summary>
    public static bool OwnName(HttpContext http)
    {
        string host = http.Request.Host.Host ?? "";
        if (host.Length == 0) return true;      // no Host at all: not a browser
        if (IPAddress.TryParse(host.Trim('[', ']'), out _)) return true;
        host = host.TrimEnd('.').ToLowerInvariant();
        if (!host.Contains('.') || host == "localhost") return true;
        foreach (string end in new[] { ".lan", ".local", ".internal", ".home.arpa", ".localhost", ".home", ".localdomain" })
            if (host.EndsWith(end)) return true;
        return KnownNames.Has(host);
    }

    public static TrustedAddress TrustedMatch(ISignInSettings s, IPAddress ip)
    {
        if (!s.TrustedOn || ip == null) return null;
        return s.Trusted.FirstOrDefault(t => IpRange.Contains(t.Range, ip));
    }

    /// <summary>
    /// May this request skip signing in? Only from a trusted address - and never when it came through a proxy the app wasn't told about
    /// (NPM on the home network would otherwise let the whole internet in).
    /// </summary>
    public static TrustedAddress Trusted(HttpContext http, ISignInSettings s)
    {
        var direct = IpRange.Plain(http.Connection.RemoteIpAddress);
        bool forwarded = http.Request.Headers.ContainsKey("X-Forwarded-For") || http.Request.Headers.ContainsKey("Forwarded");
        bool knownProxy = direct != null && s.TrustedProxies.Any(p => IpRange.Contains(p, direct));
        if (forwarded && !knownProxy) return null;
        if (OtherSite(http, knownProxy)) return null;
        if (!OwnName(http)) return null;
        return TrustedMatch(s, Client(http, s));
    }

    /// <summary>
    /// Sent by another site's page? A browser says where a request came from (Origin) on everything a page of another site sends
    /// that could change something. With no cookie to hold back, a trusted address would let any web page open on that PC press
    /// the app's buttons - so a request whose Origin isn't the address it was sent to isn't trusted. (Apps with a key, and a
    /// browser on the app's own pages, never get here with a different Origin.)
    /// </summary>
    public static bool OtherSite(HttpContext http, bool knownProxy)
    {
        string origin = http.Request.Headers["Origin"].ToString();
        if (origin.Length == 0) return false;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var o) || o.Host.Length == 0) return true;    // "null": a sandboxed frame, a file
        if (SameHost(o, http.Request.Host)) return false;
        if (knownProxy)     // a proxy that doesn't pass Host on says what it was in X-Forwarded-Host
            foreach (string h in http.Request.Headers["X-Forwarded-Host"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (SameHost(o, new HostString(h))) return false;
        return true;
    }

    static bool SameHost(Uri origin, HostString host)
    {
        if (!host.HasValue || !string.Equals(origin.Host, host.Host, StringComparison.OrdinalIgnoreCase)) return false;
        return host.Port == null ? origin.IsDefaultPort : host.Port == origin.Port;
    }
}

/// <summary>The names somebody has signed in on with the password (names.json in the data folder) - see <see cref="Auth.OwnName"/>.</summary>
public static class KnownNames
{
    sealed class FileShape { public List<string> Names { get; set; } = new(); }
    static readonly object _gate = new();
    static HashSet<string> _names;
    static string _dir;

    static HashSet<string> Names()
    {
        // read again when the data folder is another one (tests)
        if (_names == null || _dir != Paths.Data)
        {
            _dir = Paths.Data;
            _names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_dir != null) foreach (string n in JsonFile.Load<FileShape>(Paths.File("names.json")).Names ?? new()) _names.Add(n);
        }
        return _names;
    }

    public static bool Has(string host) { lock (_gate) return Names().Contains(host); }

    public static void Add(string host)
    {
        host = (host ?? "").TrimEnd('.').ToLowerInvariant();
        if (host.Length == 0 || host.Length > 253) return;
        lock (_gate)
        {
            var names = Names();
            if (names.Count >= 50 || !names.Add(host)) return;
            try { if (_dir != null) JsonFile.Save(Paths.File("names.json"), new FileShape { Names = names.OrderBy(n => n).ToList() }); }
            catch (Exception ex) { Log.Write("Couldn't save names.json: " + ex.Message); }
        }
    }
}

/// <summary>What the app's own rule says about a request, before the gate's.</summary>
public enum GateSays { Next, Pass, Stop }

/// <summary>The app's own rules for the gate (registered as a singleton).</summary>
public sealed class GateRules
{
    /// <summary>Asked first: Pass = let it through, Stop = answered already (set the status), Next = the usual rules
    /// (vDeploy: its helpers' port, its webhooks).</summary>
    public Func<HttpContext, IWebSettings, GateSays> First { get; set; }
}

/// <summary>
/// Every request goes through here: no login yet = the setup page; signed in (cookie) or from a trusted address = through;
/// an app key = /api/v1/ only; otherwise the sign-in page (pages) or 401 (the live connection, the API).
/// </summary>
public sealed class Gate
{
    readonly RequestDelegate _next;
    readonly GateRules _rules;
    public Gate(RequestDelegate next, GateRules rules = null) { _next = next; _rules = rules ?? new GateRules(); }

    static bool Open(HttpContext http)
    {
        string s = http.Request.Path.Value ?? "/";
        if (s.StartsWith("/_framework") || s.StartsWith("/_content") || s == "/api/health" || s == "/favicon.svg") return true;
        if (s.StartsWith("/api/v1/") || s.StartsWith("/hook/")) return false;
        if (s == "/Error") return true;     // drawn bare: nothing of the app on it
        // the pages' own files (styles, scripts, pictures) - but never a page: "/options/a.css" is still Options
        if (http.GetEndpoint()?.Metadata.GetMetadata<Microsoft.AspNetCore.Components.Endpoints.ComponentTypeMetadata>() != null) return false;
        string last = s.Substring(s.LastIndexOf('/') + 1);
        return last.Contains('.') && (s.EndsWith(".css") || s.EndsWith(".js") || s.EndsWith(".svg") || s.EndsWith(".png") || s.EndsWith(".woff2") || s.EndsWith(".map"));
    }

    /// <summary>The "not found" page is drawn in the app's window (its side panel): only for somebody signed in. Anybody else gets
    /// the answer as it stands (a 401 or 404 with nothing in it).</summary>
    static bool NotFoundPage(string path) { return path.StartsWith("/not-found", StringComparison.OrdinalIgnoreCase); }

    public async Task InvokeAsync(HttpContext http, IWebState state)
    {
        var s = state.Web;
        string path = http.Request.Path.Value ?? "/";
        switch (_rules.First?.Invoke(http, s) ?? GateSays.Next)
        {
            case GateSays.Pass: await _next(http); return;
            case GateSays.Stop: return;
        }
        if (Open(http)) { await _next(http); return; }

        // an app with a key (a vShell plugin, vTrace): the API only
        string auth = http.Request.Headers.Authorization.ToString();
        if (path.StartsWith("/api/v1/") && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var key = ApiKey.Find(s.ApiKeys, auth.Substring(7).Trim());
            if (key == null) { http.Response.StatusCode = 401; return; }
            http.Items[Auth.KeyItem] = key.Name;
            http.User = Auth.Principal(s, trusted: true);
            if (key.Used == null || DateTimeOffset.Now - key.Used.Value > TimeSpan.FromMinutes(10)) { key.Used = DateTimeOffset.Now; state.Save(); }
            await _next(http);
            return;
        }

        if (!s.HasLogin)
        {
            if (path == "/setup") { await _next(http); return; }
            if (NotFoundPage(path)) return;
            if (path.StartsWith("/_blazor")) { http.Response.StatusCode = 401; return; }
            http.Response.Redirect("/setup");
            return;
        }
        if (path == "/setup") { http.Response.Redirect("/"); return; }

        bool signedIn = http.User?.Identity?.IsAuthenticated == true;
        if (signedIn) { if (!Auth.OwnName(http)) KnownNames.Add(http.Request.Host.Host); }     // signed in with the password on this name: it's one of ours
        else if (Auth.Trusted(http, s) != null)
        {
            http.User = Auth.Principal(s, trusted: true);
            signedIn = true;
        }
        if (path == "/login")
        {
            if (signedIn && http.Request.Query["out"].Count == 0) { http.Response.Redirect("/"); return; }
            await _next(http);
            return;
        }
        if (!signedIn)
        {
            if (NotFoundPage(path)) return;
            if (path.StartsWith("/_blazor") || path.StartsWith("/api/")) { http.Response.StatusCode = 401; return; }
            http.Response.Redirect("/login?r=" + Uri.EscapeDataString(path + http.Request.QueryString));
            return;
        }
        await _next(http);
    }
}
