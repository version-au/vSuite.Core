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
        }
        return ip;
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

    static bool Open(PathString p)
    {
        string s = p.Value ?? "/";
        if (s.StartsWith("/_framework") || s.StartsWith("/_content") || s == "/api/health" || s == "/favicon.svg") return true;
        if (s.StartsWith("/api/v1/") || s.StartsWith("/hook/")) return false;
        if (s.StartsWith("/Error") || s.StartsWith("/not-found")) return true;
        string last = s.Substring(s.LastIndexOf('/') + 1);
        return last.Contains('.') && (s.EndsWith(".css") || s.EndsWith(".js") || s.EndsWith(".svg") || s.EndsWith(".png") || s.EndsWith(".woff2") || s.EndsWith(".map"));
    }

    public async Task InvokeAsync(HttpContext http, IWebState state)
    {
        var s = state.Web;
        string path = http.Request.Path.Value ?? "/";
        switch (_rules.First?.Invoke(http, s) ?? GateSays.Next)
        {
            case GateSays.Pass: await _next(http); return;
            case GateSays.Stop: return;
        }
        if (Open(http.Request.Path)) { await _next(http); return; }

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
            if (path.StartsWith("/_blazor")) { http.Response.StatusCode = 401; return; }
            http.Response.Redirect("/setup");
            return;
        }
        if (path == "/setup") { http.Response.Redirect("/"); return; }

        bool signedIn = http.User?.Identity?.IsAuthenticated == true;
        if (!signedIn && Auth.Trusted(http, s) != null)
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
            if (path.StartsWith("/_blazor") || path.StartsWith("/api/")) { http.Response.StatusCode = 401; return; }
            http.Response.Redirect("/login?r=" + Uri.EscapeDataString(path + http.Request.QueryString));
            return;
        }
        await _next(http);
    }
}
