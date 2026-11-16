using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace vSuite
{
    /// <summary>
    /// What vShell (or anything else) asks of vTerm. Cmd:
    ///   "toggle" / "show" / "hide" - the drop-down from a bar: where (X, Y, W, H - the terminal's card in screen pixels, its bar side
    ///                                against the bar's edge), the bar's look (Radius, BorderThickness, BorderColor, Shadow, Blur), and
    ///                                FromTop (it slides down out of a top bar; false = up out of a bottom bar), Attached (flush to the bar;
    ///                                false = a free card), HideOnLeave, Profile (a new tab's), Bar (the bar's window handle);
    ///   "open-host"                  - an SSH tab to Host / Name / User running Remote (Why = why there's no address) - in the drop-down
    ///                                when the placement is given (W &gt; 0), else in a vTerm window;
    ///   "args"                       - vTerm.exe started again with Args: the running vTerm does what they ask.
    /// </summary>
    public sealed class TermRequest
    {
        public string Cmd { get; set; } = "toggle";
        public int X { get; set; }
        public int Y { get; set; }
        public int W { get; set; }
        public int H { get; set; }
        public bool FromTop { get; set; } = true;
        public bool Attached { get; set; } = true;
        public double Radius { get; set; } = 8;
        public double BorderThickness { get; set; } = 1;
        /// <summary>"#AARRGGBB" (the bar's / widget style's outline at its opacity); "" = none.</summary>
        public string BorderColor { get; set; } = "";
        public bool Shadow { get; set; } = true;
        /// <summary>"Off", "Blur", "Acrylic" (Frosted) or "Clear" - the bar's glass.</summary>
        public string Blur { get; set; } = "Off";
        public bool HideOnLeave { get; set; } = true;
        public string Profile { get; set; } = "";
        public long Bar { get; set; }
        public string Host { get; set; }
        public string Name { get; set; }
        public string User { get; set; }
        public string Remote { get; set; }
        public string Why { get; set; }
        public string[] Args { get; set; }
    }

    /// <summary>
    /// The line between vShell and vTerm: a named pipe for the user's own processes only ("vTerm-" + the id of vTerm's settings folder, as
    /// Instance uses), one request per connection - a line of JSON (TermRequest) in, a line of text back ("shown", "hidden", "ok", or
    /// "error: …"). The sender lets vTerm take the foreground (its window comes to the front for the keyboard).
    /// </summary>
    public static class TermLink
    {
        public const string AppName = "vTerm";

        /// <summary>The pipe of the vTerm that uses this settings folder.</summary>
        public static string PipeName(string dataFolder) { return AppName + "-" + Instance.IdFor(dataFolder).ToString("x8"); }

        /// <summary>The pipe of the vTerm that uses the usual settings folder (%APPDATA%\vTerm).</summary>
        public static string DefaultPipe { get { return PipeName(SuiteApps.DataFolder(AppName)); } }

        static readonly JsonSerializerOptions Json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        public static string Encode(TermRequest r) { return JsonSerializer.Serialize(r ?? new TermRequest()); }

        public static TermRequest Decode(string line)
        {
            try { return JsonSerializer.Deserialize<TermRequest>(line ?? "", Json); }
            catch { return null; }
        }

        /// <summary>
        /// Is anything listening on that pipe? (Windows lists the pipes there are.) Connecting to one that isn't there doesn't fail at
        /// once - it keeps trying for the whole timeout, at full speed: every start of vTerm lost a second and a half to it.
        /// When the list can't be read: true (find out by connecting).
        /// </summary>
        public static bool Listening(string pipe)
        {
            try
            {
                foreach (string p in Directory.EnumerateFiles(@"\\.\pipe\"))
                    if (string.Equals(Path.GetFileName(p), pipe, StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
            catch { return true; }
        }

        /// <summary>Send a request; the answer, or null when no vTerm is listening on that pipe (not running) or it didn't answer in time.</summary>
        public static string Send(string pipe, TermRequest r, int timeoutMs = 3000)
        {
            // not there: look a few more times before saying so - a listener has no pipe for an instant between two requests
            for (int i = 0; !Listening(pipe); i++)
            {
                if (i >= 8) return null;
                Thread.Sleep(25);
            }
            try
            {
                using (var c = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.CurrentUserOnly))
                {
                    c.Connect(Math.Max(50, Math.Min(timeoutMs, 1500)));
                    // vTerm may come to the front (its drop-down / window takes the keyboard)
                    try
                    {
                        uint pid;
                        if (GetNamedPipeServerProcessId(c.SafePipeHandle.DangerousGetHandle(), out pid)) AllowSetForegroundWindow(pid);
                    }
                    catch { }
                    var enc = new UTF8Encoding(false);
                    byte[] line = enc.GetBytes(Encode(r) + "\n");
                    c.Write(line, 0, line.Length);
                    c.Flush();
                    var reader = new StreamReader(c, enc);
                    var task = reader.ReadLineAsync();
                    if (!task.Wait(timeoutMs)) return null;
                    return task.Result;
                }
            }
            catch (TimeoutException) { return null; }
            catch (IOException) { return null; }
            catch (Exception ex) { Log.Write("TermLink send: " + ex.Message); return null; }
        }

        /// <summary>
        /// Listen on a pipe (vTerm), on a background thread, until stop is cancelled: each request goes to handle (which runs on whatever
        /// thread it chooses - vTerm hands it to its UI thread) and its answer goes back.
        /// </summary>
        public static void Serve(string pipe, Func<TermRequest, string> handle, CancellationToken stop)
        {
            var t = new Thread(() =>
            {
                var enc = new UTF8Encoding(false);
                while (!stop.IsCancellationRequested)
                {
                    try
                    {
                        using (var s = new NamedPipeServerStream(pipe, PipeDirection.InOut, 4, PipeTransmissionMode.Byte,
                                                                 PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
                        {
                            s.WaitForConnectionAsync(stop).Wait();
                            var reader = new StreamReader(s, enc, false, 4096, true);
                            string line = reader.ReadLine();
                            string answer;
                            TermRequest r = Decode(line);
                            try { answer = r == null ? "error: not a request" : handle(r) ?? "ok"; }
                            catch (Exception ex) { answer = "error: " + ex.Message; Log.Write("TermLink: " + ex); }
                            byte[] back = enc.GetBytes(answer.Replace("\r", " ").Replace("\n", " ") + "\n");
                            s.Write(back, 0, back.Length);
                            s.Flush();
                            try { s.WaitForPipeDrain(); } catch { }
                        }
                    }
                    catch (AggregateException) when (stop.IsCancellationRequested) { break; }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        if (stop.IsCancellationRequested) break;
                        Log.Write("TermLink listen: " + ex.Message);
                        Thread.Sleep(250);
                    }
                }
            }) { IsBackground = true, Name = "TermLink" };
            t.Start();
        }

        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint pid);
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(uint pid);
    }
}
