using SysDiag.Diagnostics;
using SysDiag.Models;

namespace SysDiag.Services;

/// <summary>Orquestación compartida por WPF y el runner: recolectar → reglas → sustituir módulo → recomendaciones.</summary>
public class ScanService : IScanService
{
    private readonly DiagnosticEngine _motor = new();

    public async Task<DiagnosticReport> EjecutarAsync(DiagnosticReport acumulado, IDiagnosticService[] pasos,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(pasos);
        var result = acumulado ?? new DiagnosticReport();
        foreach (var step in pasos)
        {
            token.ThrowIfCancellationRequested();
            var scratch = new DiagnosticReport();
            await step.EjecutarAsync(scratch, token);
            token.ThrowIfCancellationRequested();
            if (step.Clave == "rendimiento") _motor.Evaluar(scratch);
            foreach (var finding in scratch.Hallazgos)
                if (string.IsNullOrWhiteSpace(finding.Modulo)) finding.Modulo = step.Clave;
            result.ReplaceModuleFrom(scratch, step.Clave);
        }
        result.Fin = DateTime.Now;
        result.ActualizarRecomendaciones();
        return result;
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
