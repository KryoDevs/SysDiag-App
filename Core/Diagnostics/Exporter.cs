using System.Collections;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SysDiag.Models;

namespace SysDiag.Core.Diagnostics;

public static class Exporter
{
    public static string HistorialPath { get; } = Path.Combine(AppEnv.OutputPath, "historial");
    public static int HistorialMaximo = 60;
    private static readonly object ArchiveGate = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, Converters = { new JsonStringEnumConverter() }
    };

    private static string ExportPath(string root, string prefix, string extension) => Path.Combine(root,
        $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss_fffffff}_{Guid.NewGuid():N}.{extension}");

    public static string ToCsv(string nombre, IList filas, string outputDirectory = null)
    {
        if (filas == null || filas.Count == 0) throw new InvalidOperationException("La tabla está vacía.");
        var props = TypeDescriptor.GetProperties(filas[0].GetType()).Cast<PropertyDescriptor>()
            .Where(p => p.IsBrowsable).ToList();
        var text = new StringBuilder();
        text.AppendLine(string.Join(";", props.Select(p => EscapeCsvField(p.DisplayName))));
        foreach (var row in filas)
            text.AppendLine(string.Join(";", props.Select(p => EscapeCsvField(p.GetValue(row)?.ToString() ?? ""))));
        string file = ExportPath(outputDirectory ?? AppEnv.OutputPath, Sanear(nombre), "csv");
        AtomicFile.WriteAllText(file, text.ToString(), new UTF8Encoding(true));
        AppLog.Write($"Exportado: {file}", "OK");
        return file;
    }

    public static string ToJson(DiagnosticReport report, string outputDirectory = null)
    {
        string file = ExportPath(outputDirectory ?? AppEnv.OutputPath, "diagnostico", "json");
        AtomicFile.WriteAllText(file, Serializar(report));
        AppLog.Write($"Exportado: {file}", "OK");
        return file;
    }

    public static bool Archivar(DiagnosticReport report, string historialDirectory = null)
    {
        try
        {
            lock (ArchiveGate)
            {
                string root = historialDirectory ?? HistorialPath;
                if (report.Id == Guid.Empty) report.Id = Guid.NewGuid();
                string file = Path.Combine(root, $"{report.Inicio:yyyyMMdd_HHmmss_fffffff}_{report.Id:N}.json");
                AtomicFile.WriteAllText(file, Serializar(report));
                foreach (var old in ReadHistory(root).Skip(Math.Clamp(HistorialMaximo, 5, 500)))
                {
                    try { File.Delete(old.Archivo); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Write($"No se pudo archivar el diagnóstico: {ex.Message}", "WARN");
            return false;
        }
    }

    public sealed class EntradaHistorial
    {
        public DateTime Fecha;
        public int Puntaje;
        public string Archivo = "";
        public string[] Modulos = Array.Empty<string>();
    }

    /// <summary>La fecha guardada en JSON es la medición; mtime cambia al copiar/editar un archivo.</summary>
    private static List<EntradaHistorial> ReadHistory(string root)
    {
        var entries = new List<EntradaHistorial>();
        if (!Directory.Exists(root)) return entries;
        try
        {
            foreach (var file in new DirectoryInfo(root).GetFiles("*.json"))
            {
                try
                {
                    if (file.Length > 64 * 1024 * 1024) continue;
                    using var document = JsonDocument.Parse(File.ReadAllText(file.FullName));
                    var data = document.RootElement;
                    if (!data.TryGetProperty("Inicio", out var start) || !start.TryGetDateTime(out var date)
                        || !data.TryGetProperty("Puntaje", out var score) || !score.TryGetInt32(out int value)) continue;
                    string[] modules = data.TryGetProperty("ModulosCompletados", out var measured)
                        && measured.ValueKind == JsonValueKind.Object
                        ? measured.EnumerateObject().Select(p => p.Name).ToArray() : Array.Empty<string>();
                    entries.Add(new() { Fecha = date, Puntaje = value, Archivo = file.FullName, Modulos = modules });
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
                { AppLog.Write($"Entrada de historial omitida: {file.Name} ({ex.Message})", "WARN"); }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { AppLog.Write($"No se pudo leer el historial: {ex.Message}", "WARN"); }
        return entries.OrderByDescending(e => e.Fecha.ToUniversalTime()).ToList();
    }

    public static int PuntajeAnterior(DateTime actual, string[] modulos = null, string historialDirectory = null)
    {
        var expected = modulos == null ? null : new HashSet<string>(modulos, StringComparer.OrdinalIgnoreCase);
        return ReadHistory(historialDirectory ?? HistorialPath)
            .FirstOrDefault(e => e.Fecha.ToUniversalTime() < actual.ToUniversalTime() && e.Puntaje >= 0
                && (expected == null || expected.SetEquals(e.Modulos)))?.Puntaje ?? -1;
    }

    public static List<EntradaHistorial> Listar(int maximo = 100, string historialDirectory = null) =>
        ReadHistory(historialDirectory ?? HistorialPath).Take(Math.Max(0, maximo)).ToList();

    public static List<(DateTime Fecha, int Puntaje)> Historial(int maximo = 30, string historialDirectory = null) =>
        Listar(maximo, historialDirectory).Where(e => e.Puntaje >= 0).Select(e => (e.Fecha, e.Puntaje)).ToList();

    public static DiagnosticReport Cargar(string archivo)
    {
        if (new FileInfo(archivo).Length > 64 * 1024 * 1024) throw new InvalidDataException("El diagnóstico excede 64 MB.");
        return JsonSerializer.Deserialize<DiagnosticReport>(File.ReadAllText(archivo), JsonOptions)
            ?? throw new InvalidDataException("El diagnóstico está vacío.");
    }

    private static string Serializar(DiagnosticReport report)
    {
        report.Puntaje = HealthScore.Calcular(report);
        report.ActualizarRecomendaciones();
        return JsonSerializer.Serialize(report, JsonOptions);
    }

    /// <summary>Neutraliza fórmulas de Excel/LibreOffice y escapa separador, comillas y ambos saltos de línea.</summary>
    public static string EscapeCsvField(string value)
    {
        value ??= "";
        string trimmed = value.TrimStart();
        if ((trimmed.Length > 0 && "=+-@".Contains(trimmed[0]))
            || (value.Length > 0 && value[0] is '\t' or '\r' or '\n')) value = "'" + value;
        value = value.Replace("\"", "\"\"");
        return value.IndexOfAny(new[] { ';', '"', '\n', '\r' }) >= 0 ? $"\"{value}\"" : value;
    }

    private static string Sanear(string name)
    {
        name = string.IsNullOrWhiteSpace(name) ? "tabla" : name;
        foreach (char invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
        return name.Replace(' ', '_');
    }
}
