namespace vSuite.Server;

/// <summary>
/// Which server app is running (as vSuite's App.Init for the Windows apps). Each app sets it once, before anything else - vDeploy from
/// a module initializer in vDeploy.Core, so its tests have it too. Nothing in the library names an app.
/// </summary>
public static class ServerApp
{
    /// <summary>"vDeploy" - shown in messages and the authenticator app.</summary>
    public static string Name { get; private set; } = "vSuite";
    /// <summary>"VDEPLOY" - environment variables are PREFIX_DATA_DIR, PREFIX_UPDATE_SITE, PREFIX_LAUNCHER.</summary>
    public static string Prefix { get; private set; } = "VSUITE";
    /// <summary>"vdeploy" - the program's file, the log, the folder on the update site, the SSH key's file (id_vdeploy).</summary>
    public static string Lower { get { return Name.ToLowerInvariant(); } }
    /// <summary>App keys start with this ("vd_").</summary>
    public static string KeyPrefix { get; private set; } = "vs_";
    /// <summary>Other programs in the app's build that must be executable after an update unpacks it (vDeploy's "vdeploy-helper").</summary>
    public static IReadOnlyList<string> Executables { get; private set; } = Array.Empty<string>();

    public static void Init(string name, string prefix, string keyPrefix, params string[] executables)
    {
        Name = name;
        Prefix = prefix;
        KeyPrefix = keyPrefix;
        Executables = executables ?? Array.Empty<string>();
    }

    /// <summary>The app's environment variable PREFIX_name, or null when it isn't set.</summary>
    public static string Env(string name)
    {
        string v = Environment.GetEnvironmentVariable(Prefix + "_" + name);
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }
}
