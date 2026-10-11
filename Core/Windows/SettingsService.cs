using System.IO;
using System.Text.Json;
using System.Threading;
using SysDiag.Models;

namespace SysDiag.Core.Windows;

public static class SettingsService
{
    private static string Archivo => Path.Combine(AppEnv.OutputPath, "settings.json");

    /// <summary>
    /// Foto de los valores vigentes, solo para los textos de la interfaz y de los informes («en los
    /// últimos 30 días»). Los módulos NO la leen: reciben <see cref="OpcionesDiagnostico"/> como
    /// argumento (MEJORAS §1, F10). Se reemplaza entera y nunca se modifica en sitio.
    /// </summary>
    private static OpcionesDiagnostico _vigentes = OpcionesDiagnostico.PorDefecto;
    public static OpcionesDiagnostico Vigentes => Volatile.Read(ref _vigentes);

    /// <param name="archivo">Ruta alternativa; sin valor, el settings.json de SysDiag. Existe para las pruebas.</param>
    public static AppSettings Cargar(string? archivo = null)
    {
        string ruta = archivo ?? Archivo;
        try
        {
            if (!File.Exists(ruta)) return AppSettings.PorDefecto();
            return (JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(ruta))
                    ?? AppSettings.PorDefecto()).Normalizar();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Write($"No se pudo leer la configuración, se usan los valores por defecto: {ex.Message}", "WARN");
            return AppSettings.PorDefecto();
        }
    }

    /// <param name="archivo">Ruta alternativa; sin valor, el settings.json de SysDiag. Existe para las pruebas.</param>
    public static bool Guardar(AppSettings settings, string? archivo = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        try
        {
            AtomicFile.WriteAllText(archivo ?? Archivo, JsonSerializer.Serialize(settings.Normalizar(),
                new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"No se pudo guardar la configuración: {ex.Message}", "ERROR");
            return false;
        }
    }

    /// <summary>
    /// Publica los valores para la interfaz y aplica la rotación de registros, que es el único efecto
    /// de disco de los ajustes. Ya no toca estáticos de los módulos: esos reciben las opciones al correr.
    /// </summary>
    public static void Aplicar(AppSettings settings)
    {
        var valid = (settings ?? AppSettings.PorDefecto()).Normalizar();
        Volatile.Write(ref _vigentes, OpcionesDiagnostico.Desde(valid));
        AppEnv.RotarLogs(valid.LogsMaximo);
    }
}
