using System;
using System.Diagnostics;
using System.Globalization;

namespace SysDiag.Diagnostics;

/// <summary>
/// Presupuesto de medición propio: cuánta CPU y cuánta memoria costó la
/// corrida que acaba de terminar.
///
/// Existe porque una herramienta que mide perturba aquello que mide, y porque
/// el módulo de rendimiento muestrea durante varios segundos. Si SysDiag se
/// come un 30 % de un núcleo mientras mide el rendimiento, sus propios números
/// valen menos —y el usuario tiene derecho a ver la cifra en vez de tener que
/// creerle a la herramienta.
///
/// Mide tiempo de CPU del proceso (no porcentaje instantáneo: el porcentaje
/// depende de cuántos núcleos haya y cambia según quién más esté corriendo), y
/// el pico de memoria privada alcanzado durante la corrida.
/// </summary>
public sealed class MedidorCoste
{
    private readonly Stopwatch _reloj = new();
    private TimeSpan _cpuInicio;
    private long _picoMb;
    private bool _activo;

    /// <summary>Núcleos del equipo. Si no se puede saber, se asume 1 y el porcentaje se lee como «de un núcleo».</summary>
    private static int Nucleos()
    {
        try { int n = Environment.ProcessorCount; return n > 0 ? n : 1; }
        catch { return 1; }
    }

    public void Iniciar()
    {
        _reloj.Restart();
        _cpuInicio = CpuActual();
        _picoMb = MemoriaActualMb();
        _activo = true;
    }

    /// <summary>
    /// Detiene la medición y devuelve la frase lista para mostrar. Se devuelve
    /// acá y no en la vista porque la frase tiene plurales, una unidad y un
    /// caso «no se pudo medir»: armarla en XAML sería un conversor con estados.
    /// </summary>
    public string Detener()
    {
        if (!_activo) return "";
        _activo = false;
        _reloj.Stop();

        // El pico se muestrea una última vez al cerrar: el máximo real pudo
        // darse entre dosTicks del cronómetro de proceso, y el valor final
        // sigue siendo la mejor cota que se puede dar sin un muestreador
        // dedicado —que ya sería más costoso que lo que mide.
        _picoMb = Math.Max(_picoMb, MemoriaActualMb());

        TimeSpan cpu = CpuActual() - _cpuInicio;
        double segundos = _reloj.Elapsed.TotalSeconds;

        // Un cronómetro de proceso puede no estar disponible (contenedores,
        /// permisos reducidos). Ahí la respuesta honesta es no decir nada.
        if (cpu < TimeSpan.Zero || segundos <= 0) return "";

        double pctUnNucleo = cpu.TotalSeconds / segundos * 100;
        double pctEquipo = pctUnNucleo / Nucleos();

        string detalle = cpu.TotalSeconds < 1
            ? $"{cpu.TotalMilliseconds:0} ms de CPU"
            : $"{cpu.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture)} s de CPU";

        // El porcentaje del equipo completo es el que importa para juzgar el
        // impacto; el de un núcleo es el que permite comparar entre equipos.
        return $"{detalle} · {pctEquipo.ToString("0", CultureInfo.CurrentCulture)} % del equipo" +
               $" ({pctUnNucleo.ToString("0", CultureInfo.CurrentCulture)} % de un núcleo) · {_picoMb} MB";
    }

    /// <summary>Actualiza el pico de memoria. Se llama una vez por segundo desde el cronómetro de la interfaz.</summary>
    public void Muestrear()
    {
        if (!_activo) return;
        long actual = MemoriaActualMb();
        if (actual > _picoMb) _picoMb = actual;
    }

    private static TimeSpan CpuActual()
    {
        try { return Process.GetCurrentProcess().TotalProcessorTime; }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        { return TimeSpan.Zero; }
    }

    private static long MemoriaActualMb()
    {
        try { return Process.GetCurrentProcess().PrivateMemorySize64 / (1024 * 1024); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        { return 0; }
    }
}
