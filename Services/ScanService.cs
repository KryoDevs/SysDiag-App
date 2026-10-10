using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SysDiag.Core;
using SysDiag.Diagnostics;
using SysDiag.Models;

namespace SysDiag.Services;

/// <summary>Orquestación compartida por WPF y el runner: recolectar → reglas → sustituir módulo → recomendaciones.</summary>
public class ScanService : IScanService
{
    private readonly DiagnosticEngine _motor = new();

    /// <summary>
    /// Tiempo máximo por módulo. Un recolector que se cuelga —WMI que no
    /// responde, un `traceroute` que no termina, un servidor DNS que no
    /// contesta— dejaba la barra a medias para siempre y sin explicación: el
    /// pie decía «Midiendo» y el usuario no sabía si esperar o cerrar. Con
    /// este límite el módulo se marca y la corrida sigue con los demás.
    ///
    /// La cancelación que pide el usuario es distinta y se distingue abajo:
    /// aquí solo se interrumpe lo que se colgó solo.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, TimeSpan> LimitesPorModulo =
        new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase)
        {
            // La red hace traceroute y lotes de ping por destino: es el módulo
            // legítimamente más lento, y cortarlo a los 90 segundos daría un
            // diagnóstico de red sistemáticamente incompleto.
            ["red"] = TimeSpan.FromMinutes(4),
            ["drivers"] = TimeSpan.FromMinutes(3),
            ["actualizaciones"] = TimeSpan.FromMinutes(3),
            ["almacenamiento"] = TimeSpan.FromMinutes(2),
            ["estabilidad"] = TimeSpan.FromMinutes(2),
            ["seguridad"] = TimeSpan.FromMinutes(2),
            ["arranque"] = TimeSpan.FromMinutes(2),
            // El rendimiento muestrea a propósito: espera entre dos tomas de
            // contadores. Su tiempo está en el propio módulo, no acá.
            ["rendimiento"] = TimeSpan.FromMinutes(2),
            ["termicas"] = TimeSpan.FromMinutes(1),
            ["limpieza"] = TimeSpan.FromMinutes(2)
        };

    public static readonly TimeSpan LimiteDefecto = TimeSpan.FromMinutes(2);

    public static TimeSpan LimitePara(string clave) =>
        LimitesPorModulo.TryGetValue(clave ?? "", out var limite) ? limite : LimiteDefecto;

    public async Task<DiagnosticReport> EjecutarAsync(DiagnosticReport acumulado, IDiagnosticService[] pasos,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(pasos);
        var result = acumulado ?? new DiagnosticReport();
        bool diagnostic = pasos.Any(p => DiagnosticReport.NombresModulos.ContainsKey(p.Clave));
        if (diagnostic) result.EstadoEjecucion = "En curso";
        // La caché de WMI se vacía al empezar y vive lo que dura la corrida.
        // Va acá y no en la interfaz porque este mismo servicio es el que usa
        // el runner sin cabeza: si se reiniciara solo desde WPF, el modo
        // --diagnostico compartiría caché entre corridas de equipos distintos
        // en la misma sesión.
        Wmi.NuevaCorrida();
        result.LimpiarAvisosColgados();
        result.ModulosColgados = new List<string>();
        result.DuracionesModulo = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var step in pasos)
            {
                token.ThrowIfCancellationRequested();
                var scratch = new DiagnosticReport();
                var reloj = Stopwatch.StartNew();

                // Token propio por paso: se cancela solo, o cuando el usuario
                // cancela la corrida entera. El `when` de abajo separa los dos
                // casos, que no merecen el mismo mensaje ni el mismo destino.
                using var paso = CancellationTokenSource.CreateLinkedTokenSource(token);
                paso.CancelAfter(LimitePara(step.Clave));

                try
                {
                    await step.EjecutarAsync(scratch, paso.Token);
                    token.ThrowIfCancellationRequested();
                    if (step.Clave == "rendimiento") _motor.Evaluar(scratch);
                    foreach (var finding in scratch.Hallazgos)
                        if (string.IsNullOrWhiteSpace(finding.Modulo)) finding.Modulo = step.Clave;
                    result.ReplaceModuleFrom(scratch, step.Clave);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    // Se agotó el tiempo de este módulo. No se fusiona lo que
                    // trajo a medias: `scratch` se descarta y la corrida
                    // anterior de ese módulo queda en pantalla, que es más
                    // útil que un «Red y latencia» vacío. Y se dice, porque un
                    // módulo que falta sin avisar se lee como «no hay
                    // problemas de red».
                    string nombre = DiagnosticReport.NombresModulos.TryGetValue(step.Clave, out var etiqueta)
                        ? etiqueta : step.Clave;
                    result.MarcarColgado(step.Clave);
                    AppLog.Write(
                        $"El módulo «{nombre}» no respondió en {LimitePara(step.Clave).TotalSeconds:0} s y se omitió. " +
                        "Se continúa con el resto; los datos que ves de ese módulo son de una corrida anterior.", "WARN");
                }
                finally
                {
                    reloj.Stop();
                    result.RegistrarDuracion(step.Clave, reloj.Elapsed);
                }
            }
            if (diagnostic)
            {
                result.Fin = DateTime.Now;
                result.EstadoEjecucion = "Completado";
                // Los avisos van al final y no durante el bucle: `Add` se
                // ejecuta sobre el reporte fusionado, y un `ReplaceModuleFrom`
                // posterior los barrería con el resto de hallazgos del módulo.
                foreach (var colgado in result.ModulosColgados) result.AddColgado(colgado, LimitePara(colgado));
            }
            result.ActualizarRecomendaciones();
            return result;
        }
        catch (OperationCanceledException)
        {
            if (diagnostic) { result.Fin = DateTime.Now; result.EstadoEjecucion = "Cancelado"; }
            throw;
        }
        catch
        {
            if (diagnostic) { result.Fin = DateTime.Now; result.EstadoEjecucion = "Falló o incompleto"; }
            throw;
        }
    }
}

/// <summary>Adapta acciones especiales de la UI sin duplicar la lógica de fusión y evaluación.</summary>
public sealed class DelegateDiagnosticService : IDiagnosticService
{
    public string Clave { get; }
    private readonly Func<DiagnosticReport, CancellationToken, Task> _work;
    public DelegateDiagnosticService(string clave, Func<DiagnosticReport, CancellationToken, Task> work)
    { Clave = clave; _work = work; }
    public Task EjecutarAsync(DiagnosticReport report, CancellationToken token) => _work(report, token);
}
