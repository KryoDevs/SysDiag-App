namespace SysDiag.Models;

/// <summary>
/// Parámetros ajustables. Antes vivían fijos como campos estáticos en cada
/// módulo (siguen siéndolo — es justo lo que permite ajustarlos sin tocar la
/// firma de cada método); esta clase es solo la capa de carga/guardado en
/// disco y los valores por defecto documentados en un solo lugar.
/// </summary>
public class AppSettings
{
    /// <summary>Segundos de muestreo para medir CPU real por proceso.</summary>
    public int SampleSeconds { get; set; } = 5;

    /// <summary>Cuántos pings por destino al medir latencia.</summary>
    public int PingCount { get; set; } = 20;

    /// <summary>Ventana de días para el registro de eventos críticos general.</summary>
    public int EventDays { get; set; } = 30;

    /// <summary>Ventana de días para el escaneo dedicado de errores WHEA.</summary>
    public int WheaDays { get; set; } = 15;

    /// <summary>Cuántos diagnósticos históricos conservar antes de descartar los más viejos.</summary>
    public int HistorialMaximo { get; set; } = 60;

    /// <summary>Cuántos archivos de registro conservar en logs/.</summary>
    public int LogsMaximo { get; set; } = 30;

    public static AppSettings PorDefecto() => new();

    /// <summary>También valida JSON cargado o llamadas directas, no solo los campos de la UI.</summary>
    public AppSettings Normalizar() => new()
    {
        SampleSeconds = Math.Clamp(SampleSeconds, 2, 30),
        PingCount = Math.Clamp(PingCount, 5, 100),
        EventDays = Math.Clamp(EventDays, 1, 90),
        WheaDays = Math.Clamp(WheaDays, 1, 90),
        HistorialMaximo = Math.Clamp(HistorialMaximo, 5, 500),
        LogsMaximo = Math.Clamp(LogsMaximo, 5, 200)
    };

    /// <summary>
    /// Interpreta el texto de un campo de Ajustes: si no es un número válido
    /// usa el valor por defecto, y si está fuera de rango lo acota. Vive acá
    /// (no en el code-behind de la ventana) para poder probarlo sin abrir
    /// ninguna ventana.
    /// </summary>
    public static int LeerCampo(string texto, int minimo, int maximo, int porDefecto)
        => int.TryParse(texto, out int v) ? Math.Clamp(v, minimo, maximo) : porDefecto;
}
