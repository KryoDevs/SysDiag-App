using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace SysDiag.Core.Windows;

/// <summary>
/// Un JSON en Documentos es modificable por procesos sin elevar del mismo usuario.
/// Los respaldos que guían una restauración elevada deben ser propiedad de Administradores/SYSTEM
/// y no permitir escritura a otros. No se importan automáticamente respaldos antiguos de Documentos.
/// </summary>
public static class SecureBackupDirectory
{
    private static readonly SecurityIdentifier Admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier System = new(WellKnownSidType.LocalSystemSid, null);
    private const FileSystemRights DangerousRights = FileSystemRights.WriteData | FileSystemRights.AppendData
        | FileSystemRights.WriteAttributes | FileSystemRights.WriteExtendedAttributes | FileSystemRights.Delete
        | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    public static string BackupFile
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            string sid = identity.User?.Value ?? throw new InvalidOperationException("No se pudo identificar al usuario.");
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "SysDiag-Backups", sid, "estado-previo.json");
        }
    }

    public static void Ensure()
    {
        if (!AppEnv.IsAdmin) throw new UnauthorizedAccessException("El respaldo protegido requiere administrador.");
        string profile = Path.GetDirectoryName(BackupFile)!;
        ProtectDirectory(Path.GetDirectoryName(profile)!);
        ProtectDirectory(profile);
    }

    /// <summary>También utilizado en pruebas sobre un directorio temporal, nunca sobre carpetas de Windows.</summary>
    public static void ProtectDirectory(string path)
    {
        if (!AppEnv.IsAdmin) throw new UnauthorizedAccessException("El respaldo protegido requiere administrador.");
        path = Path.GetFullPath(path);
        RejectLinks(path, directory: true);
        var protectedAcl = new DirectorySecurity();
        protectedAcl.SetAccessRuleProtection(true, false);
        protectedAcl.SetOwner(Admins);
        foreach (var principal in new[] { Admins, System })
            protectedAcl.AddAccessRule(new FileSystemAccessRule(principal, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        byte[] descriptor = protectedAcl.GetSecurityDescriptorBinaryForm();
        IntPtr pointer = Marshal.AllocHGlobal(descriptor.Length);
        try
        {
            Marshal.Copy(descriptor, 0, pointer, descriptor.Length);
            var attributes = new SecurityAttributes { Size = Marshal.SizeOf<SecurityAttributes>(), Descriptor = pointer };
            if (!CreateDirectoryW(path, ref attributes) && Marshal.GetLastWin32Error() != 183 /* ya existe */)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "No se pudo crear la carpeta de respaldo protegido.");
        }
        finally { Marshal.FreeHGlobal(pointer); }
        using var handle = Open(path, directory: true);
        var security = new DirectoryInfo(path).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
        RequireTrustedOwner(security);
        RequireNoUnprivilegedWriters(security);
        Apply(handle, protectedAcl.GetSecurityDescriptorBinaryForm());
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("El directorio de respaldo no puede ser un enlace/junction.");
    }

    /// <summary>Se aplica al temporal ALEATORIO antes de renombrarlo a estado-previo.json.</summary>
    public static void ProtectTemporaryFile(string path)
    {
        using var handle = Open(path, directory: false);
        var security = new FileSecurity();
        security.SetAccessRuleProtection(true, false);
        security.SetOwner(Admins);
        foreach (var principal in new[] { Admins, System })
            security.AddAccessRule(new FileSystemAccessRule(principal, FileSystemRights.FullControl, AccessControlType.Allow));
        Apply(handle, security.GetSecurityDescriptorBinaryForm());
    }

    public static void VerifyFile(string path)
    {
        using var handle = Open(path, directory: false);
        var security = new FileInfo(path).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
        RequireTrustedOwner(security);
        RequireNoUnprivilegedWriters(security);
    }

    private static void RequireTrustedOwner(FileSystemSecurity security)
    {
        var owner = security.GetOwner(typeof(SecurityIdentifier));
        if (!Admins.Equals(owner) && !System.Equals(owner))
            throw new UnauthorizedAccessException("La carpeta/archivo de respaldo tiene un propietario no confiable. No se usará ni se tomará posesión automáticamente.");
    }

    private static void RequireNoUnprivilegedWriters(FileSystemSecurity security)
    {
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if (rule.AccessControlType == AccessControlType.Allow && rule.PropagationFlags != PropagationFlags.InheritOnly
                && (rule.FileSystemRights & DangerousRights) != 0
                && !Admins.Equals(rule.IdentityReference) && !System.Equals(rule.IdentityReference))
                throw new UnauthorizedAccessException("El respaldo permite escritura a usuarios sin elevar. No se usará automáticamente.");
    }

    private static SafeFileHandle Open(string path, bool directory)
    {
        path = Path.GetFullPath(path);
        RejectLinks(path, directory);
        const uint readControl = 0x20000, writeDac = 0x40000, writeOwner = 0x80000;
        if (!AppEnv.IsAdmin) throw new UnauthorizedAccessException("Acceso elevado requerido para el respaldo.");
        var handle = CreateFileW(path, readControl | writeDac | writeOwner, 1 /* compartir solo lectura */,
            IntPtr.Zero, 3, 0x00200000 /* abrir enlace, no seguirlo */ | (directory ? 0x02000000u : 0u), IntPtr.Zero);
        if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        { handle.Dispose(); throw new IOException("El respaldo no puede ser un enlace/junction."); }
        return handle;
    }

    private static void RejectLinks(string path, bool directory)
    {
        for (var parent = new DirectoryInfo(directory ? path : Path.GetDirectoryName(path)!);
             parent != null; parent = parent.Parent)
            if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("La ruta de respaldo contiene un enlace/junction.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes { public int Size; public IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] public bool Inherit; }
    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateDirectoryW(string path, ref SecurityAttributes security);

    private static void Apply(SafeFileHandle handle, byte[] descriptor)
    {
        if (!SetKernelObjectSecurity(handle, 0x80000005 /* OWNER | DACL | PROTECTED_DACL */, descriptor))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "No se pudo proteger el respaldo; no se aplicarán ajustes.");
    }

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint sharing, IntPtr security,
        uint creation, uint flags, IntPtr template);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetKernelObjectSecurity(SafeFileHandle handle, uint information, byte[] descriptor);
}
