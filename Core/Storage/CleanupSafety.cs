using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SysDiag.Core.Storage;

/// <summary>Comprobaciones compartidas por el análisis y el borrado. Nunca sigue enlaces/junctions.</summary>
public static class CleanupSafety
{
    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static bool SamePath(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), PathComparison);

    public static bool IsContainedFile(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(path)
            || !Path.IsPathFullyQualified(root) || !Path.IsPathFullyQualified(path)) return false;
        string relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return relative != "." && relative != ".." && !Path.IsPathRooted(relative)
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, PathComparison)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, PathComparison);
    }

    public static bool HasSafeAncestors(string root, string path)
    {
        if (!IsContainedFile(root, path) || SamePath(root, Path.GetPathRoot(root)!)) return false;
        try
        {
            // Incluye la raíz y sus padres: un objetivo que ya es un junction tampoco es seguro.
            for (var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path))!);
                 directory != null; directory = directory.Parent)
            {
                directory.Refresh();
                if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            }
            var file = new FileInfo(path);
            file.Refresh();
            return file.Exists && (file.Attributes & (FileAttributes.ReparsePoint | FileAttributes.ReadOnly)) == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { return false; }
    }

    public static bool IsThumbnail(string path) =>
        Path.GetFileName(path).StartsWith("thumbcache_", StringComparison.OrdinalIgnoreCase)
        && Path.GetExtension(path).Equals(".db", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// En Windows elimina por HANDLE, no reabriendo el nombre después de validarlo.
    /// Comprobar la ruta final del handle evita que un junction sustituido entre análisis y borrado
    /// redirija una operación elevada a otro archivo. No cambia atributos ni borra directorios.
    /// </summary>
    public static bool TryDelete(string root, string path, out long bytes)
    {
        bytes = 0;
        if (!HasSafeAncestors(root, path)) return false;
        if (!OperatingSystem.IsWindows())
        {
            // Los diagnósticos de Windows no usan esta rama; permite probar contención en otros SO.
            var info = new FileInfo(path);
            bytes = info.Length;
            File.Delete(path);
            return true;
        }

        const uint delete = 0x00010000, readAttributes = 0x80;
        const uint shareReadWrite = 0x1 | 0x2, openExisting = 3, openReparsePoint = 0x00200000;
        using var handle = CreateFileW(path, delete | readAttributes, shareReadWrite, IntPtr.Zero,
            openExisting, openReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid) return false;

        if (!GetFileInformationByHandle(handle, out var info)
            || (info.Attributes & (uint)(FileAttributes.ReparsePoint | FileAttributes.ReadOnly | FileAttributes.Directory)) != 0)
            return false;

        var finalPath = new StringBuilder(32768);
        uint length = GetFinalPathNameByHandleW(handle, finalPath, (uint)finalPath.Capacity, 0);
        if (length == 0 || length >= finalPath.Capacity) return false;
        string resolved = finalPath.ToString();
        if (resolved.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            resolved = @"\\" + resolved[8..];
        else if (resolved.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
            resolved = resolved[4..];
        if (!SamePath(path, resolved) || !IsContainedFile(root, resolved)) return false;

        var disposition = new FileDispositionInfo { DeleteFile = true };
        if (!SetFileInformationByHandle(handle, 4 /* FileDispositionInfo */, ref disposition,
                (uint)Marshal.SizeOf<FileDispositionInfo>())) return false;
        bytes = ((long)info.SizeHigh << 32) | info.SizeLow;
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        public uint VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInfo
    {
        [MarshalAs(UnmanagedType.U1)] public bool DeleteFile;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation info);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint size, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int kind,
        ref FileDispositionInfo info, uint size);
}
