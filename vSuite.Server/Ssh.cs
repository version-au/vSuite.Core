using System.Security.Cryptography;
using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace vSuite.Server;

/// <summary>Quoting for sh.</summary>
public static class Sh
{
    /// <summary>One argument for sh, in single quotes (a ' inside becomes '"'"').</summary>
    public static string Q(string s) { return "'" + (s ?? "").Replace("'", "'\"'\"'") + "'"; }
}
/// <summary>
/// The app's own SSH key (ECDSA P-256), made on first start: id_&lt;app&gt; (owner-only) and id_&lt;app&gt;.pub in the data folder.
/// Hosts get its public half in authorized_keys when they're added.
/// </summary>
public static class SshKey
{
    static string _pem, _pub;
    static readonly object Gate = new();

    public static string Public { get { Ensure(); return _pub; } }

    public static PrivateKeyFile File()
    {
        Ensure();
        return new PrivateKeyFile(new MemoryStream(Encoding.ASCII.GetBytes(_pem)));
    }

    static void Ensure()
    {
        lock (Gate)
        {
            if (_pem != null) return;
            string path = Paths.File("id_" + ServerApp.Lower);
            if (System.IO.File.Exists(path))
            {
                _pem = System.IO.File.ReadAllText(path);
                _pub = System.IO.File.Exists(path + ".pub") ? System.IO.File.ReadAllText(path + ".pub").Trim() : null;
            }
            if (_pem == null || _pub == null)
            {
                (_pem, _pub) = Make(ServerApp.Lower + "@" + Environment.MachineName.ToLowerInvariant());
                System.IO.File.WriteAllText(path, _pem);
                JsonFile.Private(path);
                System.IO.File.WriteAllText(path + ".pub", _pub + "\n");
                Log.Write("Made " + ServerApp.Name + "'s SSH key");
            }
        }
    }

    /// <summary>A new key: its PEM ("EC PRIVATE KEY") and its authorized_keys line.</summary>
    public static (string pem, string pub) Make(string comment)
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = ec.ExportParameters(false);
        byte[] point = new byte[1 + p.Q.X.Length + p.Q.Y.Length];
        point[0] = 4;
        p.Q.X.CopyTo(point, 1);
        p.Q.Y.CopyTo(point, 1 + p.Q.X.Length);
        var blob = new MemoryStream();
        SshString(blob, Encoding.ASCII.GetBytes("ecdsa-sha2-nistp256"));
        SshString(blob, Encoding.ASCII.GetBytes("nistp256"));
        SshString(blob, point);
        return (ec.ExportECPrivateKeyPem(), "ecdsa-sha2-nistp256 " + Convert.ToBase64String(blob.ToArray()) + " " + comment);
    }

    static void SshString(Stream s, byte[] data)
    {
        byte[] len = BitConverter.GetBytes(data.Length);
        if (BitConverter.IsLittleEndian) Array.Reverse(len);
        s.Write(len);
        s.Write(data);
    }
}

/// <summary>Runs commands on a host. SSH for real hosts; the demo answers from made-up data.</summary>
public interface IHostRunner : IDisposable
{
    string SeenKey { get; }
    bool Connected { get; }
    void Connect();
    Task<RunResult> RunAsync(string command, int timeoutSeconds = 60);
    /// <summary>A long-running command (a log that follows): each line as it comes, until it ends or ct is cancelled. Returns its exit code.</summary>
    Task<int> StreamAsync(string command, Action<string> line, CancellationToken ct);
    /// <summary>Put a file on the host (SFTP), with its permissions written as their octal digits: 755, 600 (SSH.NET's way).</summary>
    Task UploadAsync(Stream data, string path, short mode);
}

/// <summary>Something went wrong reaching a host, in words for the page.</summary>
public class HostProblem : Exception
{
    public HostProblem(string message, Exception inner = null) : base(message, inner) { }
}

/// <summary>One SSH connection to a host, kept open between checks; commands take turns.</summary>
public sealed class SshRunner : IHostRunner
{
    readonly SshClient _c;
    readonly ConnectionInfo _ci;
    readonly SemaphoreSlim _turn = new(1, 1);
    readonly string _expect, _address;

    public string SeenKey { get; private set; }
    public bool Connected { get { return _c.IsConnected; } }

    /// <param name="expectKey">The host key seen before ("SHA256:…"), or null to take the one it shows (adding a host).</param>
    public SshRunner(string address, int port, string user, string expectKey, params AuthenticationMethod[] auth)
    {
        _expect = expectKey;
        _address = address;
        var ci = new ConnectionInfo(address, port, user, auth) { Timeout = TimeSpan.FromSeconds(10) };
        _ci = ci;
        _c = new SshClient(ci) { KeepAliveInterval = TimeSpan.FromSeconds(30) };
        _c.HostKeyReceived += (s, e) =>
        {
            SeenKey = "SHA256:" + e.FingerPrintSHA256;
            e.CanTrust = _expect == null || _expect == SeenKey;
        };
    }

    public static AuthenticationMethod[] WithPassword(string user, string password)
    {
        var kbd = new KeyboardInteractiveAuthenticationMethod(user);
        kbd.AuthenticationPrompt += (s, e) => { foreach (var p in e.Prompts) p.Response = password; };
        return new AuthenticationMethod[] { new PasswordAuthenticationMethod(user, password), kbd };
    }

    public static AuthenticationMethod[] WithKey(string user, PrivateKeyFile key)
    {
        return new AuthenticationMethod[] { new PrivateKeyAuthenticationMethod(user, key) };
    }

    public void Connect()
    {
        try { _c.Connect(); }
        catch (SshConnectionException ex) when (_expect != null && SeenKey != null && SeenKey != _expect)
        {
            throw new HostProblem("The host's SSH key has changed (it was " + _expect + ", now " + SeenKey + "). " + ServerApp.Name + " won't connect until you check it and add the host again.", ex);
        }
        catch (SshAuthenticationException ex) { throw new HostProblem("The host didn't accept the sign-in. Check the user and the password or key.", ex); }
        catch (System.Net.Sockets.SocketException ex) { throw new HostProblem("Couldn't reach " + _address + ": " + ex.Message, ex); }
        catch (SshOperationTimeoutException ex) { throw new HostProblem("No answer from " + _address + " within 10 seconds.", ex); }
        catch (SshException ex) { throw new HostProblem("SSH failed: " + ex.Message, ex); }
    }

    public async Task<RunResult> RunAsync(string command, int timeoutSeconds = 60)
    {
        await _turn.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                using var cmd = _c.CreateCommand(command);
                cmd.CommandTimeout = TimeSpan.FromSeconds(timeoutSeconds);
                cmd.Execute();
                return new RunResult(cmd.ExitStatus ?? -1, cmd.Result, cmd.Error);
            }).ConfigureAwait(false);
        }
        catch (SshOperationTimeoutException ex) { throw new HostProblem("The host took longer than " + timeoutSeconds + " s to answer.", ex); }
        catch (SshConnectionException ex) { throw new HostProblem("The connection dropped: " + ex.Message, ex); }
        finally { _turn.Release(); }
    }

    /// <summary>Its own channel on the same connection (doesn't wait for other commands).</summary>
    public async Task<int> StreamAsync(string command, Action<string> line, CancellationToken ct)
    {
        using var cmd = _c.CreateCommand(command);
        var run = cmd.ExecuteAsync(ct);
        var reader = Task.Run(async () =>
        {
            using var r = new StreamReader(cmd.OutputStream);
            string s;
            while ((s = await r.ReadLineAsync(ct).ConfigureAwait(false)) != null) line(s);
        }, ct);
        try { await run.ConfigureAwait(false); }
        catch (OperationCanceledException) { return -1; }
        try { await reader.ConfigureAwait(false); } catch (OperationCanceledException) { }
        return cmd.ExitStatus ?? -1;
    }

    /// <summary>
    /// A terminal on the host (a pty, its own channel on the same connection): a login shell - write "exec cmd" into it to run one
    /// command that ends the session with it. Its output (colours, menus) is for a terminal to draw (xterm.js); ChangeWindowSize follows
    /// the page's size. vDeploy's terminal in the page (1.0.37).
    /// </summary>
    public ShellStream OpenShell(uint columns, uint rows, string terminal = "xterm-256color")
    {
        try { return _c.CreateShellStream(terminal, columns, rows, 0, 0, 64 * 1024); }
        catch (SshConnectionException ex) { throw new HostProblem("The connection dropped: " + ex.Message, ex); }
        catch (SshException ex) { throw new HostProblem("Couldn't open a terminal: " + ex.Message, ex); }
    }

    /// <summary>
    /// A file onto the host by SFTP - or, when the host has no SFTP server (Debian 13 without openssh-sftp-server: the subsystem fails
    /// and SSH.NET says "Channel was closed" - arrstack-vm, 2026-11-12), through a plain command's input: cat into path.part, chmod,
    /// then moved into place. mode is written as chmod takes it (755, 600).
    /// </summary>
    public async Task UploadAsync(Stream data, string path, short mode)
    {
        long start = data.CanSeek ? data.Position : -1;
        try
        {
            await Task.Run(() =>
            {
                using var sftp = new SftpClient(_ci);
                sftp.HostKeyReceived += (s, e) => e.CanTrust = _expect == null || _expect == "SHA256:" + e.FingerPrintSHA256;
                sftp.Connect();
                sftp.UploadFile(data, path, true);
                sftp.ChangePermissions(path, mode);
                sftp.Disconnect();
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (start >= 0 && _c.IsConnected && ex is not HostProblem)
        {
            Log.Write("SFTP didn't work on " + _address + " (" + ex.Message + ") - sending " + Path.GetFileName(path) + " over SSH instead");
            data.Position = start;
            await UploadOverShell(data, path, mode).ConfigureAwait(false);
        }
    }

    async Task UploadOverShell(Stream data, string path, short mode)
    {
        string q = Sh.Q(path), part = Sh.Q(path + ".part");
        await _turn.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(async () =>
            {
                using var cmd = _c.CreateCommand("cat > " + part + " && chmod " + mode + " " + part + " && mv -f " + part + " " + q);
                cmd.CommandTimeout = TimeSpan.FromMinutes(10);
                var run = cmd.BeginExecute();
                using (var input = cmd.CreateInputStream()) await data.CopyToAsync(input).ConfigureAwait(false);   // closing it ends cat's input
                cmd.EndExecute(run);
                if (cmd.ExitStatus != 0) throw new HostProblem("Couldn't send " + path + " to the host: " + (cmd.Error ?? "").Trim());
            }).ConfigureAwait(false);
        }
        catch (SshConnectionException ex) { throw new HostProblem("The connection dropped while sending " + path + ": " + ex.Message, ex); }
        finally { _turn.Release(); }
    }

    public void Dispose()
    {
        try { if (_c.IsConnected) _c.Disconnect(); } catch { }
        _c.Dispose();
    }
}
