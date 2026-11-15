using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace vSuite.Server;

/// <summary>Passwords as PBKDF2-SHA256 ("pbkdf2$iterations$salt$hash"), checked in constant time.</summary>
public static class Password
{
    const int Iterations = 210_000;

    public static string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return "pbkdf2$" + Iterations + "$" + Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(hash);
    }

    public static bool Verify(string password, string stored)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(stored)) return false;
        string[] p = stored.Split('$');
        if (p.Length != 4 || p[0] != "pbkdf2" || !int.TryParse(p[1], out int it)) return false;
        try
        {
            byte[] salt = Convert.FromBase64String(p[2]), want = Convert.FromBase64String(p[3]);
            byte[] got = Rfc2898DeriveBytes.Pbkdf2(password, salt, it, HashAlgorithmName.SHA256, want.Length);
            return CryptographicOperations.FixedTimeEquals(got, want);
        }
        catch { return false; }
    }

    /// <summary>Why a new password isn't good enough, or null.</summary>
    public static string Problem(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 10) return "Use at least 10 characters.";
        return null;
    }
}

/// <summary>Authenticator codes (RFC 6238: 30 s, 6 digits, SHA-1) and their Base32 keys.</summary>
public static class Totp
{
    const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string NewSecret() { return Base32(RandomNumberGenerator.GetBytes(20)); }

    public static string Code(byte[] key, DateTimeOffset at, int digits = 6)
    {
        long counter = at.ToUnixTimeSeconds() / 30;
        byte[] msg = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(msg);
        byte[] h = HMACSHA1.HashData(key, msg);
        int o = h[^1] & 0x0F;
        int bin = ((h[o] & 0x7F) << 24) | (h[o + 1] << 16) | (h[o + 2] << 8) | h[o + 3];
        int mod = (int)Math.Pow(10, digits);
        return (bin % mod).ToString(new string('0', digits));
    }

    /// <summary>The code now, or one step either side (clocks drift).</summary>
    public static bool Check(string base32Secret, string code, DateTimeOffset now)
    {
        code = (code ?? "").Replace(" ", "");
        if (code.Length != 6 || string.IsNullOrEmpty(base32Secret)) return false;
        byte[] key = FromBase32(base32Secret);
        for (int i = -1; i <= 1; i++)
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Code(key, now.AddSeconds(30 * i))), Encoding.ASCII.GetBytes(code))) return true;
        return false;
    }

    public static string Uri(string base32Secret, string user, string issuer = null)
    {
        issuer ??= ServerApp.Name;
        return "otpauth://totp/" + System.Uri.EscapeDataString(issuer + ":" + user) + "?secret=" + base32Secret + "&issuer=" + System.Uri.EscapeDataString(issuer);
    }

    public static string Base32(byte[] data)
    {
        var sb = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (byte b in data)
        {
            buffer = (buffer << 8) | b; bits += 8;
            while (bits >= 5) { sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]); bits -= 5; }
        }
        if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    public static byte[] FromBase32(string s)
    {
        s = s.Trim().TrimEnd('=').Replace(" ", "").ToUpperInvariant();
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (char c in s)
        {
            int v = Alphabet.IndexOf(c);
            if (v < 0) throw new FormatException("Not a Base32 key.");
            buffer = (buffer << 5) | v; bits += 5;
            if (bits >= 8) { bytes.Add((byte)((buffer >> (bits - 8)) & 0xFF)); bits -= 8; }
        }
        return bytes.ToArray();
    }
}

/// <summary>An address or a range ("10.0.10.0/24", "10.0.20.15", "fd00::/8"); IPv4 written as IPv6 (::ffff:10.0.10.5) counts as IPv4.</summary>
public static class IpRange
{
    public static bool TryParse(string text, out IPNetwork net)
    {
        net = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        if (text.Contains('/')) return IPNetwork.TryParse(text, out net);
        if (!IPAddress.TryParse(text, out var ip)) return false;
        net = new IPNetwork(ip, ip.AddressFamily == AddressFamily.InterNetwork ? 32 : 128);
        return true;
    }

    public static IPAddress Plain(IPAddress ip)
    {
        if (ip != null && ip.IsIPv4MappedToIPv6) return ip.MapToIPv4();
        return ip;
    }

    public static bool Contains(string range, IPAddress ip)
    {
        ip = Plain(ip);
        return ip != null && TryParse(range, out var net) && net.Contains(ip);
    }
}

/// <summary>
/// Secrets kept in the data folder (the authenticator key, later stack secrets): AES-GCM with a key in secret.key (owner-only), as "vdk1:…".
/// Moving the data folder moves the key with it - back up the folder as a whole.
/// </summary>
public static class Secrets
{
    static byte[] _key;
    static readonly object Gate = new();

    static byte[] Key
    {
        get
        {
            lock (Gate)
            {
                if (_key != null) return _key;
                string path = Paths.File("secret.key");
                if (File.Exists(path)) _key = File.ReadAllBytes(path);
                if (_key == null || _key.Length != 32)
                {
                    _key = RandomNumberGenerator.GetBytes(32);
                    File.WriteAllBytes(path, _key);
                    JsonFile.Private(path);
                }
                return _key;
            }
        }
    }

    public static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return plain;
        byte[] nonce = RandomNumberGenerator.GetBytes(12), data = Encoding.UTF8.GetBytes(plain), cipher = new byte[data.Length], tag = new byte[16];
        using (var aes = new AesGcm(Key, 16)) aes.Encrypt(nonce, data, cipher, tag);
        return "vdk1:" + Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }

    public static string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored) || !stored.StartsWith("vdk1:")) return stored;
        try
        {
            byte[] all = Convert.FromBase64String(stored.Substring(5));
            byte[] nonce = all[..12], tag = all[12..28], cipher = all[28..], plain = new byte[cipher.Length];
            using (var aes = new AesGcm(Key, 16)) aes.Decrypt(nonce, cipher, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) { Log.Write("Couldn't read a stored secret: " + ex.Message); return null; }
    }
}

/// <summary>Wrong sign-ins per address: 5 within 15 minutes locks that address out for 15 minutes.</summary>
public sealed class Lockout
{
    readonly Dictionary<string, List<DateTimeOffset>> _fails = new();
    public const int Tries = 5;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    public bool Locked(string who, DateTimeOffset now)
    {
        lock (_fails)
        {
            if (!_fails.TryGetValue(who ?? "", out var list)) return false;
            list.RemoveAll(t => now - t > Window);
            return list.Count >= Tries;
        }
    }

    public void Failed(string who, DateTimeOffset now)
    {
        lock (_fails)
        {
            if (!_fails.TryGetValue(who ?? "", out var list)) _fails[who ?? ""] = list = new();
            list.Add(now);
        }
    }

    public void Passed(string who) { lock (_fails) _fails.Remove(who ?? ""); }
}
