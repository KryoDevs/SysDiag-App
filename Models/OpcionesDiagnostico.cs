namespace SysDiag.Models;

/// <summary>
/// Valores de medición de una corrida, inmutables. Los módulos los reciben como argumento: no los
/// leen de un estático. Antes eran campos estáticos mutables (MEJORAS §1, F10): un módulo podía
/// leer un valor que otra ventana cambió a media corrida, y una prueba no podía fijar un umbral sin
/// pisar al resto de la suite.
/// </summary>
public sealed record OpcionesDiagnostico(
    int MuestreoSegundos,
    int Pings,
    int DiasEventos,
    int DiasWhea,
    int HistorialMaximo,
    int LogsMaximo)
{
    public const int MuestreoPorDefecto = 5;
    public const int PingsPorDefecto = 20;
    public const int DiasEventosPorDefecto = 30;
    public const int DiasWheaPorDefecto = 15;
    public const int HistorialPorDefecto = 60;
    public const int LogsPorDefecto = 30;

    public static OpcionesDiagnostico PorDefecto { get; } = new(
        MuestreoPorDefecto, PingsPorDefecto, DiasEventosPorDefecto, DiasWheaPorDefecto,
        HistorialPorDefecto, LogsPorDefecto);

    /// <summary>Normaliza y copia: el resultado ya está dentro de rango, venga de JSON o de la UI.</summary>
    public static OpcionesDiagnostico Desde(AppSettings? settings)
    {
        var n = (settings ?? AppSettings.PorDefecto()).Normalizar();
        return new OpcionesDiagnostico(n.SampleSeconds, n.PingCount, n.EventDays, n.WheaDays,
            n.HistorialMaximo, n.LogsMaximo);
    }
}
