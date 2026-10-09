using System.IO;
using System.Runtime.InteropServices;
using SysDiag.Models;

namespace SysDiag.Core.Storage;

public static class CleanupModule
{
    public class Options
    {
        public bool TempUsuario = true;
        public bool TempWindows = true;
        public bool CacheInternet = true;
        // Conservar evidencia de fallos y cachés costosas salvo selección explícita.
        public bool VolcadosApp = false;
        public bool LogsJuegos = true;
        public bool Miniaturas = true;
        public bool ShaderCache = false;
        public bool ErroresWindows = false;
        public bool CacheWindowsUpdate = false;
        public bool DeliveryOptimization = false;
        public bool Prefetch = false;
        public bool Papelera = false;
    }

    public static Options Opts = new();
    private sealed record Target(string Nombre, string Ruta, bool Miniaturas = false, bool SoloAntiguos = false);

    private static List<Target> Objetivos()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var targets = new List<Target>();
        void Add(bool enabled, string name, string root, bool thumbnails = false, bool oldOnly = false)
        {
            if (enabled && Path.IsPathFullyQualified(root)) targets.Add(new(name, root, thumbnails, oldOnly));
        }
        // No confiar en TEMP: una variable alterada podía convertir Documentos o C:\ en un objetivo.
        if (!string.IsNullOrWhiteSpace(local))
        {
            Add(Opts.TempUsuario, "Temporales del usuario", Path.Combine(local, "Temp"), oldOnly: true);
            Add(Opts.CacheInternet, "Caché de Internet", Path.Combine(local, "Microsoft", "Windows", "INetCache"));
            Add(Opts.VolcadosApp, "Volcados de aplicaciones", Path.Combine(local, "CrashDumps"));
            Add(Opts.Miniaturas, "Caché de miniaturas", Path.Combine(local, "Microsoft", "Windows", "Explorer"), thumbnails: true);
            Add(Opts.ShaderCache, "Caché de sombreadores DirectX", Path.Combine(local, "D3DSCache"));
            Add(Opts.ErroresWindows, "Informes de errores de Windows", Path.Combine(local, "Microsoft", "Windows", "WER"));
        }
        if (!string.IsNullOrWhiteSpace(windows))
        {
            Add(Opts.TempWindows, "Temporales de Windows", Path.Combine(windows, "Temp"), oldOnly: true);
            Add(Opts.CacheWindowsUpdate, "Caché de Windows Update", Path.Combine(windows, "SoftwareDistribution", "Download"));
            Add(Opts.DeliveryOptimization, "Archivos de Delivery Optimization", Path.Combine(windows, "ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization", "Cache"));
            Add(Opts.Prefetch, "Prefetch", Path.Combine(windows, "Prefetch"));
        }
        Add(Opts.LogsJuegos, "Registros de League of Legends", @"C:\Riot Games\League of Legends\Logs", oldOnly: true);
        return targets;
    }

    private static bool Eligible(Target target, FileInfo file)
    {
        if (!CleanupSafety.HasSafeAncestors(target.Ruta, file.FullName)) return false;
        if (target.Miniaturas && (!CleanupSafety.IsThumbnail(file.FullName)
            || !CleanupSafety.SamePath(file.DirectoryName!, target.Ruta))) return false;
        file.Refresh();
        return !target.SoloAntiguos || file.LastWriteTimeUtc <= DateTime.UtcNow.AddDays(-1);
    }

    public static void Analyze(DiagnosticReport report, CancellationToken token)
    {
        AppLog.Write("Análisis de temporales (no incluye temporales de las últimas 24 horas)", "STEP");
        var rows = new List<CleanupRow>();
        foreach (var target in Objetivos())
        {
            token.ThrowIfCancellationRequested();
            if (!Directory.Exists(target.Ruta)) continue;
            var files = new List<FileInfo>();
            long bytes = 0;
            try
            {
                var root = new DirectoryInfo(target.Ruta);
                if ((root.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = !target.Miniaturas,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                };
                foreach (var file in root.EnumerateFiles(target.Miniaturas ? "thumbcache_*.db" : "*", options))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        if (!Eligible(target, file)) continue;
                        bytes += file.Length;
                        files.Add(file);
                    }
                    catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException ex) { AppLog.Write($"No se pudo recorrer {target.Ruta}: {ex.Message}", "WARN"); }
            catch (UnauthorizedAccessException ex) { AppLog.Write($"No se pudo recorrer {target.Ruta}: {ex.Message}", "WARN"); }

            rows.Add(new CleanupRow
            {
                Ubicacion = target.Nombre, Ruta = target.Ruta, Archivos = files.Count,
                Bytes = bytes, Ocupa = AppEnv.FormatBytes(bytes), Items = files
            });
            AppLog.Write($"{target.Nombre,-34} {files.Count,7} archivos   {AppEnv.FormatBytes(bytes)}");
        }
        report.Limpieza = rows;
        report.EspacioLiberado = "";
        long total = rows.Sum(row => row.Bytes);
        AppLog.Write($"Total recuperable estimado: {AppEnv.FormatBytes(total)}", "OK");
        if (total > 2L * 1024 * 1024 * 1024)
            report.Add(Severity.Warn, "Limpieza", $"Hay {AppEnv.FormatBytes(total)} en temporales seleccionados.",
                "Revisa las categorías antes de borrar. Algunos archivos pueden estar en uso; el borrado no es reversible.", "limpiar-temp");
    }

    public static void Clean(DiagnosticReport report, List<CleanupRow> rows, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(rows);
        report.Limpieza = rows;
        var targets = Objetivos();
        long freed = 0;
        int skipped = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AppLog.Write("Limpieza de los archivos analizados y confirmados", "STEP");
        try
        {
            foreach (var row in rows)
            {
                token.ThrowIfCancellationRequested();
                // No aceptar rutas arbitrarias de reportes importados ni de filas manipuladas.
                var target = targets.FirstOrDefault(t => CleanupSafety.SamePath(t.Ruta, row.Ruta)
                    && t.Nombre == row.Ubicacion);
                if (target == null)
                {
                    AppLog.Write($"Objetivo de limpieza no autorizado: {row.Ruta}", "WARN");
                    continue;
                }
                List<FileInfo> items = row.Items ?? new List<FileInfo>();
                var remaining = new List<FileInfo>();
                int next = 0;
                try
                {
                    for (; next < items.Count; next++)
                    {
                        token.ThrowIfCancellationRequested();
                        FileInfo file = items[next];
                        try
                        {
                            if (seen.Add(file.FullName) && Eligible(target, file)
                                && CleanupSafety.TryDelete(target.Ruta, file.FullName, out long size))
                                freed += size;
                            else { remaining.Add(file); skipped++; }
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        { remaining.Add(file); skipped++; }
                    }
                }
                finally
                {
                    // Si la limpieza se cancela a mitad de la fila, lo ya borrado sale de ella y lo pendiente se
                    // conserva: la tabla refleja el disco, no el análisis anterior.
                    for (; next < items.Count; next++) remaining.Add(items[next]);
                    row.Items = remaining;
                    row.Archivos = remaining.Count;
                    row.Bytes = remaining.Sum(file => { try { file.Refresh(); return file.Exists ? file.Length : 0; } catch (IOException) { return 0; } });
                    row.Ocupa = AppEnv.FormatBytes(row.Bytes);
                }
            }
            token.ThrowIfCancellationRequested();
            if (Opts.Papelera) VaciarPapelera();
        }
        finally
        {
            report.EspacioLiberado = AppEnv.FormatBytes(freed);
            AppLog.Write($"Espacio liberado de los archivos seleccionados: {report.EspacioLiberado}", "OK");
            if (skipped > 0) AppLog.Write($"{skipped} archivo(s) omitidos por protección, cambios o uso activo.", "WARN");
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string root, uint flags);

    private static void VaciarPapelera()
    {
        int hr = SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4);
        if (hr != 0)
        {
            AppLog.Write($"No se confirmó el vaciado de la papelera (0x{hr:X8}).", "WARN");
            return;
        }
        AppLog.Write("Papelera vaciada. Su tamaño no está incluido en el espacio liberado estimado.", "OK");
    }
}
