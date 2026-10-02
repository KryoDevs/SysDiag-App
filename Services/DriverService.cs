using System.Threading;
using System.Threading.Tasks;
using SysDiag.Core.Drivers;
using SysDiag.Core.Hardware;
using SysDiag.Models;

namespace SysDiag.Services;

/// <summary>
/// Inventario de drivers instalados con su antigüedad. La búsqueda de
/// actualizaciones en Windows Update (DriverUpdateModule) y la verificación
/// de firma de un archivo bajado (DriverVerifier) son acciones que el usuario
/// dispara explícitamente, no parte de un escaneo rutinario, así que no
/// entran en este servicio.
/// </summary>
public class DriverService : IDriverService
{
    public string Clave => "drivers";

    public Task EjecutarAsync(DiagnosticReport reporte, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        SystemModule.Run(reporte, token: token);
        token.ThrowIfCancellationRequested();
        DriverModule.Run(reporte);
    }, token);
}
