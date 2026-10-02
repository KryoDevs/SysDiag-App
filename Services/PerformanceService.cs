using System.Threading;
using System.Threading.Tasks;
using SysDiag.Core.Hardware;
using SysDiag.Core.Performance;
using SysDiag.Models;

namespace SysDiag.Services;

/// <summary>CPU real por proceso, RAM y cola de disco.</summary>
public class PerformanceService : IDiagnosticService
{
    public string Clave => "rendimiento";

    public Task EjecutarAsync(DiagnosticReport reporte, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        SystemModule.Run(reporte, token: token);
        token.ThrowIfCancellationRequested();
        PerformanceModule.Run(reporte, token);
    }, token);
}
