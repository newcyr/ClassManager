using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ClassManager.Core;

/// <summary>使用 Windows DPAPI（当前用户）加密 / 解密凭据，不明文落盘。</summary>
public static class CredentialProtector
{
    private const string Prefix = "dpapi:";
    private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string? szDataDescr,
        ref DATA_BLOB pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr,
        ref DATA_BLOB pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    public static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        if (plain.StartsWith(Prefix, StringComparison.Ordinal)) return plain;
        try
        {
            var data = Encoding.UTF8.GetBytes(plain);
            var inBlob = new DATA_BLOB { cbData = data.Length, pbData = Marshal.AllocHGlobal(data.Length) };
            Marshal.Copy(data, 0, inBlob.pbData, data.Length);
            var outBlob = new DATA_BLOB();
            var entropy = new DATA_BLOB();
            try
            {
                if (!CryptProtectData(ref inBlob, "ClassManager", ref entropy, IntPtr.Zero, IntPtr.Zero,
                        CRYPTPROTECT_UI_FORBIDDEN, ref outBlob))
                    return "";
                var cipher = new byte[outBlob.cbData];
                Marshal.Copy(outBlob.pbData, cipher, 0, outBlob.cbData);
                return Prefix + Convert.ToBase64String(cipher);
            }
            finally
            {
                if (inBlob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(inBlob.pbData);
                if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
            }
        }
        catch
        {
            return "";
        }
    }

    public static string Unprotect(string? cipherText)
    {
        if (string.IsNullOrEmpty(cipherText)) return "";
        if (!cipherText.StartsWith(Prefix, StringComparison.Ordinal)) return cipherText; // 兼容导入的明文
        try
        {
            var cipher = Convert.FromBase64String(cipherText.Substring(Prefix.Length));
            var inBlob = new DATA_BLOB { cbData = cipher.Length, pbData = Marshal.AllocHGlobal(cipher.Length) };
            Marshal.Copy(cipher, 0, inBlob.pbData, cipher.Length);
            var outBlob = new DATA_BLOB();
            var entropy = new DATA_BLOB();
            try
            {
                if (!CryptUnprotectData(ref inBlob, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero,
                        CRYPTPROTECT_UI_FORBIDDEN, ref outBlob))
                    return "";
                var plain = new byte[outBlob.cbData];
                Marshal.Copy(outBlob.pbData, plain, 0, outBlob.cbData);
                return Encoding.UTF8.GetString(plain);
            }
            finally
            {
                if (inBlob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(inBlob.pbData);
                if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
            }
        }
        catch
        {
            return "";
        }
    }
}
