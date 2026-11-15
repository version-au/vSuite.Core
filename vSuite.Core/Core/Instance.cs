using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace vSuite
{
    /// <summary>
    /// One copy of the app at a time (per settings folder): starting it again (a file or folder opened with it, a shortcut) hands what to
    /// open to the running one - one process, one set of settings. Each window carries the "Name.Instance" property with this settings
    /// folder's id; the text goes over in WM_COPYDATA. A test with its own PREFIX_DATA_DIR has another id, so it never hands off to
    /// your own copy (or the other way round).
    /// </summary>
    public static class Instance
    {
        /// <summary>The window property that marks a window of this app (and settings folder): "vXplorer.Instance".</summary>
        public static string Prop { get { return App.Name + ".Instance"; } }
        public const int CopyDataKind = 0x76580001;

        /// <summary>A small number for this settings folder (never 0).</summary>
        public static int Id { get { return IdFor(Log.Folder); } }

        /// <summary>The id of the copy that uses this settings folder (any suite app's).</summary>
        public static int IdFor(string dataFolder)
        {
            uint h = 2166136261;
            foreach (char c in (dataFolder ?? "").ToLowerInvariant()) { h ^= c; h *= 16777619; }
            return (int)(h & 0x7FFFFFFF) | 1;
        }

        /// <summary>
        /// Send text to another suite app's running copy (appName's window with that settings folder), as a second start would; true when
        /// one took it, false when none is running. vShell's Settings uses it to change an app's update settings.
        /// </summary>
        public static bool SendTo(string appName, string dataFolder, string text)
        {
            IntPtr target = Find(appName + ".Instance", IdFor(dataFolder));
            return target != IntPtr.Zero && Send(target, text);
        }

        /// <summary>The window marked prop = id: a visible one, else one hidden in the notification area (InTray).</summary>
        static IntPtr Find(string prop, int id)
        {
            IntPtr target = IntPtr.Zero, tray = IntPtr.Zero;
            string trayProp = prop + ".InTray";
            EnumWindows((h, l) =>
            {
                if (GetProp(h, prop).ToInt64() != id) return true;
                if (IsWindowVisible(h)) { target = h; return false; }
                if (tray == IntPtr.Zero && GetProp(h, trayProp) != IntPtr.Zero) tray = h;
                return true;
            }, IntPtr.Zero);
            return target != IntPtr.Zero ? target : tray;
        }

        /// <summary>
        /// The app's window is hidden in the notification area (minimise to the tray): on, a second start still hands over to it (only
        /// visible windows are found otherwise); off when it's shown again.
        /// </summary>
        public static void InTray(IntPtr hwnd, bool on)
        {
            if (hwnd == IntPtr.Zero) return;
            if (on) SetProp(hwnd, Prop + ".InTray", new IntPtr(1)); else RemoveProp(hwnd, Prop + ".InTray");
        }

        static bool Send(IntPtr target, string text)
        {
            byte[] bytes = Encoding.Unicode.GetBytes((text ?? "") + "\0");
            IntPtr mem = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, mem, bytes.Length);
                var cds = new COPYDATASTRUCT { dwData = new IntPtr(CopyDataKind), cbData = bytes.Length, lpData = mem };
                IntPtr result;
                IntPtr ok = SendMessageTimeout(target, 0x4A, IntPtr.Zero, ref cds, 0x2 /* SMTO_ABORTIFHUNG */, 5000, out result);
                return ok != IntPtr.Zero && result != IntPtr.Zero;
            }
            finally { Marshal.FreeHGlobal(mem); }
        }

        static Mutex _mutex;

        /// <summary>True if another copy (same settings folder) is running and took the file or folder.</summary>
        public static bool HandOff(string folder)
        {
            bool first;
            _mutex = new Mutex(true, "Local\\" + App.Name + "-" + Id.ToString("x8"), out first);
            if (first) return false;
            IntPtr target = Find(Prop, Id);
            if (target == IntPtr.Zero) return false;   // still starting up, or closing: open as usual

            uint pid;
            GetWindowThreadProcessId(target, out pid);
            AllowSetForegroundWindow(pid);   // the new window may come to the front
            return Send(target, folder);
        }

        /// <summary>The folder in a WM_COPYDATA from HandOff, or null if it isn't one.</summary>
        public static string Received(IntPtr lParam)
        {
            var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
            if (cds.dwData.ToInt64() != CopyDataKind || cds.lpData == IntPtr.Zero) return null;
            return Marshal.PtrToStringUni(cds.lpData, Math.Max(0, cds.cbData / 2)).TrimEnd('\0');
        }

        [StructLayout(LayoutKind.Sequential)]
        struct COPYDATASTRUCT { public IntPtr dwData; public int cbData; public IntPtr lpData; }

        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetProp(IntPtr h, string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SetProp(IntPtr h, string name, IntPtr value);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr RemoveProp(IntPtr h, string name);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(uint pid);
        [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, ref COPYDATASTRUCT l, uint flags, uint timeout, out IntPtr result);
    }
}
