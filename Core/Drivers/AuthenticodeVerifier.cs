using System.IO;
using System.Runtime.InteropServices;

namespace SysDiag.Core.Drivers;

/// <summary>
/// WinVerifyTrust valida el hash firmado del archivo, la cadena, la marca de tiempo y la
/// revocación. Extraer un certificado o hacer X509Chain.Build NO valida la firma del contenido.
/// </summary>
public static class AuthenticodeVerifier
{
    private static readonly Guid VerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    public static int VerifyFile(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Authenticode requiere Windows.");
        path = Path.GetFullPath(path);
        // Verificar el mismo archivo abierto: impedir cambios/reemplazos durante WinVerifyTrust.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var fileInfo = new WinTrustFileInfo
        {
            Size = (uint)Marshal.SizeOf<WinTrustFileInfo>(), FilePath = path,
            FileHandle = stream.SafeFileHandle.DangerousGetHandle()
        };
        IntPtr filePointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        Marshal.StructureToPtr(fileInfo, filePointer, false);
        var data = new WinTrustData
        {
            Size = (uint)Marshal.SizeOf<WinTrustData>(),
            UiChoice = 2, // WTD_UI_NONE
            RevocationChecks = 1, // WTD_REVOKE_WHOLECHAIN
            UnionChoice = 1, // WTD_CHOICE_FILE
            FileInfo = filePointer,
            StateAction = 1, // WTD_STATEACTION_VERIFY
            ProviderFlags = 0x80 | 0x2000 // cadena sin raíz + deshabilitar MD2/MD4; NO ignorar caducidad
        };
        var action = VerifyV2;
        try { return WinVerifyTrust(IntPtr.Zero, ref action, ref data); }
        finally
        {
            // Windows reserva estado aun cuando la verificación falla.
            data.StateAction = 2; // WTD_STATEACTION_CLOSE
            WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            Marshal.DestroyStructure<WinTrustFileInfo>(filePointer);
            Marshal.FreeHGlobal(filePointer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint Size;
        [MarshalAs(UnmanagedType.LPWStr)] public string FilePath;
        public IntPtr FileHandle, KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint Size;
        public IntPtr PolicyCallbackData, SipClientData;
        public uint UiChoice, RevocationChecks, UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        [MarshalAs(UnmanagedType.LPWStr)] public string UrlReference;
        public uint ProviderFlags, UiContext;
        public IntPtr SignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref WinTrustData data);
}
