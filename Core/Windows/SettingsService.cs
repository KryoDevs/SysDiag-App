using System.Text.Json;
using SysDiag.Models;

namespace SysDiag.Core.Windows;

public static class SettingsService
{
    private static string Archivo => Path.Combine(AppEnv.OutputPath, "settings.json");

    public static AppSettings Cargar()
    {
        try
        {
            if (!File.Exists(Archivo)) return AppSettings.PorDefecto();
            return (JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Archivo))
                    ?? AppSettings.PorDefecto()).Normalizar();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Write($"No se pudo leer la configuración, se usan los valores por defecto: {ex.Message}", "WARN");
            return AppSettings.PorDefecto();
        }
    }

    public static bool Guardar(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        try
        {
            AtomicFile.WriteAllText(Archivo, JsonSerializer.Serialize(settings.Normalizar(),
                new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"No se pudo guardar la configuración: {ex.Message}", "ERROR");
            return false;
        }
    }

    public static void Aplicar(AppSettings settings)
    {
        var valid = (settings ?? AppSettings.PorDefecto()).Normalizar();
        Performance.PerformanceModule.SampleSeconds = valid.SampleSeconds;
        Network.NetworkModule.PingCount = valid.PingCount;
        Diagnostics.StabilityModule.EventDays = valid.EventDays;
        Diagnostics.StabilityModule.WheaDays = valid.WheaDays;
        Diagnostics.Exporter.HistorialMaximo = valid.HistorialMaximo;
        AppEnv.LogsMaximo = valid.LogsMaximo;
        AppEnv.RotarLogs();
    }
}
