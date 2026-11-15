using Microsoft.AspNetCore.Components;

namespace vSuite.Web;

/// <summary>The app's settings, shared by every page (the app registers its own state as this): Save writes settings.json and tells open pages.</summary>
public interface IWebState
{
    IWebSettings Web { get; }
    void Save();
    event Action Changed;
}

/// <summary>App-wide hooks for the shared parts - set them at start-up.</summary>
public static class WebApp
{
    /// <summary>An error in words for a page (vDeploy: Fleet.Message - its host problems in plain words).</summary>
    public static Func<Exception, string> Message { get; set; } = ex => ex.Message;

    /// <summary>The app's icon on the sign-in and setup cards (40 px).</summary>
    public static RenderFragment CardLogo { get; set; }

    /// <summary>Where the first-time setup goes once the login is made (vDeploy: Add a host).</summary>
    public static string AfterSetup { get; set; } = "/";

    /// <summary>The library's own pages (Sign in, Setup) - give it to the router (Routes' AdditionalAssemblies) and to
    /// MapRazorComponents' AddAdditionalAssemblies.</summary>
    public static System.Reflection.Assembly Assembly { get { return typeof(WebApp).Assembly; } }
}
