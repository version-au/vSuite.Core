namespace vSuite.Server;

// Plain .NET only: vDeploy's trimmed helper links this file on its own.

/// <summary>What a command on a host gave back.</summary>
public sealed record RunResult(int Code, string Out, string Err)
{
    public bool Ok { get { return Code == 0; } }
    /// <summary>The error text, or the output if there was none - for messages.</summary>
    public string Why { get { string e = (Err ?? "").Trim(); return e.Length > 0 ? e : (Out ?? "").Trim(); } }
}
