using System.Threading;
using System.Threading.Tasks;
using SysDiag.Core.Diagnostics;
using SysDiag.Core.Hardware;
using SysDiag.Models;

namespace SysDiag.Services;

/// <summary>Reinicios inesperados, pantallazos, errores WHEA y minidumps.</summary>
public class StabilityService : IDiagnosticService
{
    public string Clave => "estabilidad";

    public Task EjecutarAsync(DiagnosticReport reporte, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        SystemModule.Run(reporte, token: token);
        token.ThrowIfCancellationRequested();
        token.ThrowIfCancellationRequested();
        StabilityModule.Run(reporte, token);
    }, token);
}
