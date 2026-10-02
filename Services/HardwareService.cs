using System.Threading;
using System.Threading.Tasks;
using SysDiag.Core.Hardware;
using SysDiag.Models;

namespace SysDiag.Services;

/// <summary>Inventario del equipo, térmicas y GPU: los tres módulos que WMI agrupa como "hardware".</summary>
public class HardwareService : IHardwareService
{
    public string Clave => "termicas";

    public Task EjecutarAsync(DiagnosticReport reporte, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        SystemModule.Run(reporte, token: token);
        token.ThrowIfCancellationRequested();
        ThermalModule.Run(reporte);
        GpuModule.Run(reporte);
    }, token);
}
