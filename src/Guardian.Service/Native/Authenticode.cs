using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace Guardian.Service.Native;

/// <summary>
/// The publisher of a program with a valid digital signature (Authenticode, checked by Windows itself).
/// A signature cannot be copied onto another program, so "this publisher, in this folder" keeps allowing a
/// program after its updates while nothing else gets through.
/// </summary>
internal static class Authenticode
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO { public int cbStruct; public string pcwszFilePath; public IntPtr hFile; public IntPtr pgKnownSubject; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public int cbStruct; public IntPtr pPolicyCallbackData; public IntPtr pSIPClientData; public int dwUIChoice; public int fdwRevocationChecks;
        public int dwUnionChoice; public IntPtr pFile; public int dwStateAction; public IntPtr hWVTStateData; public IntPtr pwszURLReference;
        public int dwProvFlags; public int dwUIContext; public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, ref WINTRUST_DATA data);

    /// <summary>The signing certificate's subject (e.g. "CN=Discord Inc., O=Discord Inc., ..."), or null if not validly signed.</summary>
    public static string? SignerOf(string path)
    {
        var file = new WINTRUST_FILE_INFO { cbStruct = Marshal.SizeOf<WINTRUST_FILE_INFO>(), pcwszFilePath = path };
        var pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(file, pFile, false);
            var data = new WINTRUST_DATA
            {
                cbStruct = Marshal.SizeOf<WINTRUST_DATA>(), dwUIChoice = 2 /* none */, fdwRevocationChecks = 0, dwUnionChoice = 1 /* file */,
                pFile = pFile, dwStateAction = 1 /* verify */, dwProvFlags = 0x80 /* no network for revocation */,
            };
            var rc = WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref data);
            data.dwStateAction = 2; // close
            WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref data);
            if (rc != 0) return null;
#pragma warning disable SYSLIB0057
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            return string.IsNullOrWhiteSpace(cert.Subject) ? null : cert.Subject;
        }
        catch (Exception) { return null; }
        finally { Marshal.DestroyStructure<WINTRUST_FILE_INFO>(pFile); Marshal.FreeHGlobal(pFile); }
    }
}
