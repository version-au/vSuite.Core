using System;
using System.Collections.Generic;
using System.IO;

namespace vSuite
{
    /// <summary>
    /// The app's log (name.log in its settings folder), as vShell's: lines are queued and written together by a background timer every
    /// half second, so callers - often the window's thread - never wait for the disk; the same line again within a few seconds is counted
    /// instead of written ("… repeated 12 times"). Past 1 MB the log moves to name.old.log and starts again. Call <see cref="Flush"/>
    /// before the process may end abruptly (an unhandled error); a normal exit flushes by itself.
    /// </summary>
    public static class Log
    {
        static readonly object Gate = new object(), FileGate = new object();
        const long MaxBytes = 1024 * 1024;
        static readonly List<string> Queue = new List<string>();
        static System.Threading.Timer _timer;
        static string _lastMsg;
        static DateTime _lastAt;
        static int _repeats;
        static bool _hooked;

        /// <summary>The app's settings folder (see <see cref="App.DataFolder"/>).</summary>
        public static string Folder { get { return App.DataFolder; } }

        public static string FilePath { get { return Path.Combine(Folder, App.Name.ToLowerInvariant() + ".log"); } }

        /// <summary>Thread-safe and quick: the line is written within half a second.</summary>
        public static void Write(string msg)
        {
            try
            {
                lock (Gate)
                {
                    DateTime now = DateTime.Now;
                    // the same line again straight after: count it (a loop can't flood the log - or the disk)
                    if (msg == _lastMsg && (now - _lastAt).TotalSeconds < 5) { _repeats++; _lastAt = now; return; }
                    FlushRepeats();
                    _lastMsg = msg;
                    _lastAt = now;
                    Queue.Add(now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + msg);
                    if (_timer == null)
                    {
                        _timer = new System.Threading.Timer(delegate { Flush(); }, null, 500, 500);
                        if (!_hooked) { _hooked = true; try { AppDomain.CurrentDomain.ProcessExit += delegate { Flush(); }; } catch { } }
                    }
                }
            }
            catch { }
        }

        static void FlushRepeats()
        {
            if (_repeats == 0) return;
            Queue.Add(_lastAt.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  (the line above repeated " + _repeats + " more time" + (_repeats == 1 ? "" : "s") + ")");
            _repeats = 0;
        }

        /// <summary>Write what's queued now.</summary>
        public static void Flush()
        {
            string[] lines;
            lock (Gate)
            {
                if (_repeats > 0 && (DateTime.Now - _lastAt).TotalSeconds >= 5) { FlushRepeats(); _lastMsg = null; }
                if (Queue.Count == 0) return;
                lines = Queue.ToArray();
                Queue.Clear();
            }
            try
            {
                lock (FileGate)
                {
                    Directory.CreateDirectory(Folder);
                    string path = FilePath;
                    var fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > MaxBytes)
                        File.Move(path, Path.Combine(Folder, App.Name.ToLowerInvariant() + ".old.log"), true);
                    File.AppendAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
