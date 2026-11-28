using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using vSuite.Server;

namespace vSuite.Web;

/// <summary>Options › Backups › Download: /backup/{type}/{name} - behind the sign-in like the pages (the app's Gate covers it).</summary>
public static class BackupWeb
{
    public static void Map(IEndpointRouteBuilder app, Backups backups)
    {
        app.MapGet("/backup/{type}/{name}", (string type, string name) =>
        {
            var e = backups.Find(type, name);
            return e == null ? Results.NotFound() : Results.File(e.Path, "application/zip", e.Name);
        });
    }
}
