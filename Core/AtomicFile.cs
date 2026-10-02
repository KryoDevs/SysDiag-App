using System.IO;
using System.Text;

namespace SysDiag.Core;

/// <summary>Escribe en un temporal del mismo directorio y reemplaza el destino solo al terminar.</summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string content, Encoding encoding = null, Action<string> beforeReplace = null)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None, 4096, FileOptions.WriteThrough))
            {
                using (var writer = new StreamWriter(stream, encoding ?? new UTF8Encoding(false),
                           4096, leaveOpen: true))
                    writer.Write(content);
                stream.Flush(flushToDisk: true);
            }
            beforeReplace?.Invoke(temporary);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
