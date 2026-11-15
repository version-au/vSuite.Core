using System;
using System.IO;

namespace vSuite
{
    /// <summary>How an app is installed, which decides how an update replaces it.</summary>
    public enum InstallStyle
    {
        /// <summary>One self-contained exe (vXplorer, vShell): the update swaps the exe.</summary>
        SingleExe,
        /// <summary>A folder of files (vView, whose VLC DLLs must stay separate): the update swaps the whole folder.</summary>
        Folder
    }

    /// <summary>
    /// Which app the library is running in. Each app calls <see cref="Init"/> first thing in Main; nothing in the library names an app.
    /// The app's name gives its settings folder (%APPDATA%\Name), log (name.log), window properties and mutexes; the prefix gives its
    /// test variables (PREFIX_DATA_DIR, PREFIX_VSHELL_DIR, PREFIX_TEST_HOOKS, PREFIX_TEST_ASK, PREFIX_UPDATE_SITE).
    /// </summary>
    public static class App
    {
        /// <summary>"vXplorer", "vView", "vShell".</summary>
        public static string Name { get; private set; } = "vSuite";
        /// <summary>"VXPLORER", "VVIEW", "VSHELL".</summary>
        public static string EnvPrefix { get; private set; } = "VSUITE";
        /// <summary>
        /// The web site the suite's releases are on ("https://vsuite.vrsion.com", no slash at the end): each app's newest release is in
        /// its own folder there (see Updater). PREFIX_UPDATE_SITE (tests) points at another one.
        /// </summary>
        public static string UpdateSite
        {
            get { return (Env("UPDATE_SITE") ?? _site).TrimEnd('/'); }
        }
        static string _site = "";
        public static InstallStyle Install { get; private set; } = InstallStyle.SingleExe;
        /// <summary>Folder installs: a sub-folder only a complete install has (vView: "libvlc"), so a build folder isn't mistaken for one.</summary>
        public static string FolderMarker { get; private set; }

        public static void Init(string name, string envPrefix, string updateSite, InstallStyle install = InstallStyle.SingleExe, string folderMarker = null)
        {
            Name = name;
            EnvPrefix = envPrefix;
            _site = updateSite ?? "";
            Install = install;
            FolderMarker = folderMarker;
        }

        /// <summary>The test / override variable PREFIX_key, or null when it isn't set.</summary>
        public static string Env(string key)
        {
            string v = Environment.GetEnvironmentVariable(EnvPrefix + "_" + key);
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }

        /// <summary>Test hooks are on (PREFIX_TEST_HOOKS=1).</summary>
        public static bool TestHooks { get { return Env("TEST_HOOKS") == "1"; } }

        public static string ExeName { get { return Name + ".exe"; } }

        /// <summary>%APPDATA%\Name (settings and log), or the folder in PREFIX_DATA_DIR - for tests, so they never touch your own settings.</summary>
        public static string DataFolder
        {
            get
            {
                return Env("DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Name);
            }
        }
    }
}
