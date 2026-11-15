using System.Security.Cryptography;
using System.Text;

namespace vSuite.Server;

/// <summary>
/// The sign-in part of a server app's settings.json - what the sign-in gate, the sign-in page and Options > Sign-in read. The app's
/// own settings class has these properties (same names in every app's file).
/// </summary>
public interface ISignInSettings
{
    string UserName { get; set; }
    string PasswordHash { get; set; }
    string TotpSecret { get; set; }              // protected by Secrets
    bool TrustedOn { get; set; }
    List<TrustedAddress> Trusted { get; set; }
    List<string> TrustedProxies { get; set; }
    int StayDays { get; set; }
    int Stamp { get; set; }                      // raised by "Sign out every device": older cookies stop working
    List<ApiKey> ApiKeys { get; set; }
    bool HasLogin { get; }
}

/// <summary>The settings the shared pages read: the sign-in part, dangerous actions, and the look.</summary>
public interface IWebSettings : ISignInSettings
{
    /// <summary>Dangerous actions: "type" the name, "password", or "both".</summary>
    string Confirm { get; set; }
    /// <summary>Use the colours vShell's plugin sends (theme.json); off = vShell's default theme.</summary>
    bool VShellColours { get; set; }
    /// <summary>"normal" or "larger".</summary>
    string TextSize { get; set; }
}

/// <summary>A key an app signs in with (Authorization: Bearer vd_…). Only its SHA-256 is kept; the key is shown once.</summary>
public sealed class ApiKey
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Hash { get; set; }
    public DateTimeOffset Made { get; set; }
    public DateTimeOffset? Used { get; set; }

    /// <summary>A new key: ServerApp.KeyPrefix + 48 hex.</summary>
    public static (ApiKey Key, string Plain) Make(string name)
    {
        string plain = ServerApp.KeyPrefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        return (new ApiKey { Id = Guid.NewGuid().ToString("n").Substring(0, 8), Name = name, Hash = HashOf(plain), Made = DateTimeOffset.Now }, plain);
    }

    public static string HashOf(string plain)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plain ?? ""))).ToLowerInvariant();
    }

    /// <summary>The key that matches (fixed-time compare), or null.</summary>
    public static ApiKey Find(IEnumerable<ApiKey> keys, string plain)
    {
        if (string.IsNullOrEmpty(plain) || !plain.StartsWith(ServerApp.KeyPrefix)) return null;
        var got = Encoding.ASCII.GetBytes(HashOf(plain));
        return keys.FirstOrDefault(k => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(k.Hash), got));
    }
}

public sealed class TrustedAddress
{
    public string Range { get; set; }
    public string Name { get; set; }
}
