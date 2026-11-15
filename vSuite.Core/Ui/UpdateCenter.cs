using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using vSuite;

namespace vSuite.Ui
{
    /// <summary>The app's update settings (its AppSettings implements this; Save writes them).</summary>
    public interface IUpdateSettings
    {
        /// <summary>Look for a new version a minute after starting, then about once a day.</summary>
        bool UpdateAuto { get; set; }
        /// <summary>Offer pre-releases too.</summary>
        bool UpdatePre { get; set; }
        DateTime UpdateLastCheck { get; set; }
        /// <summary>A version you chose to skip (or that didn't start and was rolled back): not offered again; a newer one is.</summary>
        string UpdateSkip { get; set; }
        void Save();
    }

    /// <summary>
    /// Update checks and installs, as in vShell: checks the update site a minute after the app starts and then about once a day (Options >
    /// Updates), shows a notice in the windows when a new version is out (UpdateNotice draws it), and runs the install, which closes
    /// the app (each window saves as it closes) and the watchdog starts the new version.
    /// </summary>
    public static class UpdateCenter
    {
        static IUpdateSettings _s;

        /// <summary>The update settings (Options' Updates page reads them to follow a change from vShell).</summary>
        public static IUpdateSettings Settings { get { return _s; } }
        static DispatcherTimer _timer;

        public static UpdateInfo Available { get; private set; }
        public static string LastError { get; private set; } = "";
        public static DateTime LastChecked { get; private set; } = DateTime.MinValue;
        public static bool Busy { get; private set; }
        public static bool Installing { get; private set; }
        /// <summary>Download progress 0..1 while installing.</summary>
        public static double Progress { get; private set; }
        /// <summary>"" while downloading; "install" once downloaded; "restart" just before the app closes.</summary>
        public static string Stage { get; private set; } = "";
        /// <summary>Why the last install failed ("" = it didn't).</summary>
        public static string InstallError { get; private set; } = "";
        /// <summary>The notice is showing (a new version found, not skipped or put off - or an install going on).</summary>
        public static bool NoticeVisible { get; private set; }
        /// <summary>Something above changed: the windows redraw the notice, Options its Updates card.</summary>
        public static event Action Changed;

        public static void Start(IUpdateSettings s)
        {
            if (_s != null) return;
            _s = s;
            string failed = Updater.TakeFailedVersion();
            if (failed != null)
            {
                // the update wouldn't start and the watchdog put this version back: don't offer it again
                s.UpdateSkip = failed;
                s.Save();
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                {
                    Window w = Application.Current.Windows.OfType<Window>().FirstOrDefault(x => x is IThemedWindow && x.IsVisible);
                    if (w != null) Ask.YesNo(w, App.Name + " update", App.Name + " " + failed + " didn't start properly, so " + App.Name + " went back to " + Updater.CurrentText +
                                             ". That version won't be offered again; a newer one will.", "OK", null, false);
                }), DispatcherPriority.ApplicationIdle);
            }
            Updater.ReportStarted();
            if (_installWhenFound) Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => CheckNow(true)), DispatcherPriority.ApplicationIdle);   // started with --update

            // a minute after start-up, then about once a day (looked at every hour)
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            bool first = true;
            _timer.Tick += (o, e) =>
            {
                _timer.Interval = TimeSpan.FromHours(1);
                bool due = first || (DateTime.UtcNow - _s.UpdateLastCheck).TotalHours >= 20;
                first = false;
                if (!_s.UpdateAuto || Busy || Installing || !due) return;
                CheckNow(false);
            };
            _timer.Start();
        }

        static void RaiseChanged()
        {
            if (Changed == null) return;
            try { Changed(); } catch (Exception ex) { Log.Write("Update UI: " + ex.Message); }
        }

        /// <summary>The start-up / hand-off command that asks an app to update now (vShell's Settings > System > vSuite apps sends it).</summary>
        public const string UpdateCommand = "--update";
        /// <summary>"--update-pre on|off": vShell's Settings changing this app's Include pre-releases.</summary>
        public const string PreCommand = "--update-pre";

        /// <summary>One of the commands HandleCommand takes (a hand-off that isn't a file or folder).</summary>
        public static bool IsCommand(string text)
        {
            string t = (text ?? "").Trim();
            return t.Equals(UpdateCommand, StringComparison.OrdinalIgnoreCase) || t.StartsWith(PreCommand + " ", StringComparison.OrdinalIgnoreCase);
        }

        static bool _installWhenFound;

        /// <summary>
        /// "--update" (from the command line, or handed over to the running copy): look for a new version now and install it when there is
        /// one (the notice shows the progress; up to date = the notice doesn't show). Returns false for anything else.
        /// </summary>
        public static bool HandleCommand(string text)
        {
            string t = (text ?? "").Trim();
            if (t.StartsWith(PreCommand + " ", StringComparison.OrdinalIgnoreCase))
            {
                bool on = t.EndsWith(" on", StringComparison.OrdinalIgnoreCase);
                if (_s != null)
                {
                    _s.UpdatePre = on;
                    _s.Save();
                    RaiseChanged();   // Options' switch follows
                }
                Log.Write("Include pre-releases " + (on ? "on" : "off") + " (from vShell)");
                return true;
            }
            if (!string.Equals(t, UpdateCommand, StringComparison.OrdinalIgnoreCase)) return false;
            Log.Write("Update asked for (" + UpdateCommand + ")");
            _installWhenFound = true;
            if (_s != null && !Busy) CheckNow(true);   // not started yet: Start checks straight away; a check going on: it takes this request
            return true;
        }

        /// <summary>Check the update site now. byUser = asked for (Options or the menu): a skipped version is shown too.</summary>
        public static async void CheckNow(bool byUser)
        {
            if (_s == null || Busy || Installing) return;
            Busy = true;
            LastError = "";
            RaiseChanged();
            UpdateInfo found = null;
            bool checkedOk = false;
            try
            {
                UpdateInfo u = await Updater.CheckAsync(_s.UpdatePre);
                Available = u;
                LastChecked = DateTime.UtcNow;
                _s.UpdateLastCheck = LastChecked;
                _s.Save();
                NoticeVisible = u != null && (byUser || !IsSkipped(u));
                found = u;
                checkedOk = true;
            }
            catch (Exception ex)
            {
                LastError = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                Log.Write("Update check: " + LastError);
            }
            finally
            {
                Busy = false;
                // "--update" (vShell's Update button) counts for this one check only - whether it worked or not - so a later automatic
                // check never installs on its own. A version that was skipped (or rolled back) isn't installed this way either.
                bool install = _installWhenFound && checkedOk && found != null && !IsSkipped(found) && Updater.CantUpdateReason == null;
                _installWhenFound = false;
                if (install) _ = Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => Install(found)));
                RaiseChanged();
            }
        }

        public static string VersionText(UpdateInfo u) { return Updater.Text(u.Version); }

        public static bool IsSkipped(UpdateInfo u) { return u != null && _s != null && _s.UpdateSkip == VersionText(u); }

        /// <summary>Don't offer this version again (a newer one will be).</summary>
        public static void Skip(UpdateInfo u)
        {
            if (u == null || _s == null) return;
            _s.UpdateSkip = VersionText(u);
            _s.Save();
            NoticeVisible = false;
            RaiseChanged();
        }

        /// <summary>Close the notice for now (it comes back at the next check).</summary>
        public static void Later()
        {
            if (Installing) return;
            NoticeVisible = false;
            InstallError = "";
            RaiseChanged();
        }

        public static void OpenPage(UpdateInfo u)
        {
            string url = u != null && u.PageUrl.Length > 0 ? u.PageUrl : Updater.ReleasesPage;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Write("Open releases: " + ex.Message); }
        }

        /// <summary>Download and install, then close the app (the watchdog starts the new version). Progress and errors show in the notice.</summary>
        public static async void Install(UpdateInfo u)
        {
            if (u == null || _s == null || Installing) return;
            Installing = true;
            NoticeVisible = true;
            Progress = 0;
            Stage = "";
            InstallError = "";
            RaiseChanged();
            Dispatcher disp = Dispatcher.CurrentDispatcher;
            string err = await Updater.DownloadAndStageAsync(u, p => disp.BeginInvoke(new Action(() =>
            {
                Progress = p;
                if (p >= 1) Stage = "install";
                RaiseChanged();
            })));
            if (err != null)
            {
                Installing = false;
                InstallError = err;
                RaiseChanged();
                return;
            }
            Progress = 1;
            Stage = "restart";
            RaiseChanged();
            await System.Threading.Tasks.Task.Delay(900);
            // close every window the normal way (each saves the settings); the process ends with the last one
            foreach (Window w in Application.Current.Windows.Cast<Window>().ToList())
            {
                try { w.Close(); } catch { }
            }
        }
    }
}
