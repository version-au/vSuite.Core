using System;
using System.Runtime.InteropServices;
using System.Text;

namespace vSuite
{
    /// <summary>
    /// Keeps a saved password / key passphrase / token out of settings.json in plain text: Windows DPAPI, tied to the current Windows user.
    /// Stored as "dpapi:" + base64 - the same in every app (vShell, vTerm, vPad), so a value saved by one is read by the others.
    /// A value without the prefix is treated as plain text.
    /// </summary>
    public static class Secret
    {
        const string Prefix = "dpapi:";

        [StructLayout(LayoutKind.Sequential)]
        struct DATA_BLOB { public int cbData; public IntPtr pbData; }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CryptProtectData(ref DATA_BLOB dataIn, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DATA_BLOB dataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CryptUnprotectData(ref DATA_BLOB dataIn, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DATA_BLOB dataOut);

        [DllImport("kernel32.dll")]
        static extern IntPtr LocalFree(IntPtr mem);

        public static bool IsProtected(string stored) { return stored != null && stored.StartsWith(Prefix, StringComparison.Ordinal); }

        /// <summary>Plain text -> value to store. Empty stays empty; if protection fails the plain text is kept (so nothing is lost).</summary>
        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            if (IsProtected(plain)) return plain;
            IntPtr buf = IntPtr.Zero;
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(plain);
                buf = Marshal.AllocHGlobal(data.Length);
                Marshal.Copy(data, 0, buf, data.Length);
                var inp = new DATA_BLOB { cbData = data.Length, pbData = buf };
                DATA_BLOB outp;
                if (!CryptProtectData(ref inp, App.Name, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out outp)) return plain;
                try
                {
                    byte[] res = new byte[outp.cbData];
                    Marshal.Copy(outp.pbData, res, 0, outp.cbData);
                    return Prefix + Convert.ToBase64String(res);
                }
                finally { LocalFree(outp.pbData); }
            }
            catch (Exception ex) { Log.Write("Secret.Protect: " + ex.Message); return plain; }
            finally { if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf); }
        }

        /// <summary>Stored value -> plain text ("" if it cannot be decrypted, e.g. it was saved by another Windows user).</summary>
        public static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return "";
            if (!IsProtected(stored)) return stored;
            IntPtr buf = IntPtr.Zero;
            try
            {
                byte[] data = Convert.FromBase64String(stored.Substring(Prefix.Length));
                buf = Marshal.AllocHGlobal(data.Length);
                Marshal.Copy(data, 0, buf, data.Length);
                var inp = new DATA_BLOB { cbData = data.Length, pbData = buf };
                DATA_BLOB outp;
                if (!CryptUnprotectData(ref inp, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out outp)) return "";
                try
                {
                    byte[] res = new byte[outp.cbData];
                    Marshal.Copy(outp.pbData, res, 0, outp.cbData);
                    return Encoding.UTF8.GetString(res);
                }
                finally { LocalFree(outp.pbData); }
            }
            catch (Exception ex) { Log.Write("Secret.Unprotect: " + ex.Message); return ""; }
            finally { if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf); }
        }
    }
}
