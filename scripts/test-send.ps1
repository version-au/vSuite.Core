<#
  Tests only: send a test command to a running suite app started with <PREFIX>_TEST_HOOKS=1 (vXplorer, vPad, vTerm) - by WM_COPYDATA to
  its window, so it needn't be in front and the real mouse and keyboard are never touched.
    test-send.ps1 -ProcessId 1234 -Command "open C:\x\a.yml"
    test-send.ps1 -ProcessId 1234 -Command "dump C:\temp\state.txt" -Wait 500
  -Title  only windows whose title ends with this (default: any visible window of the process with a title)
  -Wait   milliseconds to wait afterwards (the app runs the command a moment later, on its own thread)
#>
param([Parameter(Mandatory = $true)][int]$ProcessId, [Parameter(Mandatory = $true)][string]$Command, [string]$Title = '', [int]$Wait = 300)
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices; using System.Text;
public static class VxTestSend {
  [StructLayout(LayoutKind.Sequential)] public struct CDS { public IntPtr dwData; public int cbData; public IntPtr lpData; }
  public delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EP cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, ref CDS l);
  public static IntPtr Find(uint pid, string title) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, l) => {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p != pid || !IsWindowVisible(h)) return true;
      var sb = new StringBuilder(512); GetWindowText(h, sb, 512); string t = sb.ToString();
      if (t.Length == 0 || (title.Length > 0 && !t.EndsWith(title))) return true;
      found = h; return false;
    }, IntPtr.Zero);
    return found;
  }
  public static bool Send(IntPtr h, string text) {
    IntPtr mem = Marshal.StringToHGlobalUni(text + "\0");
    try { var c = new CDS { dwData = new IntPtr(0x76580002), cbData = (text.Length + 1) * 2, lpData = mem }; return SendMessage(h, 0x4A, IntPtr.Zero, ref c) != IntPtr.Zero; }
    finally { Marshal.FreeHGlobal(mem); }
  }
}
'@ -ErrorAction SilentlyContinue
$h = [VxTestSend]::Find([uint32]$ProcessId, $Title)
if ($h -eq [IntPtr]::Zero) { Write-Host "No window of process $ProcessId$(if ($Title) { " ending '$Title'" })."; exit 1 }
if (-not [VxTestSend]::Send($h, $Command)) { Write-Host "The window didn't take it - is <PREFIX>_TEST_HOOKS=1 set for that process?"; exit 1 }
if ($Wait -gt 0) { Start-Sleep -Milliseconds $Wait }
exit 0
