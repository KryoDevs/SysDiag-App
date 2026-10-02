using SysDiag.Core.Hardware;
using SysDiag.Core.Windows;
using SysDiag.Models;

namespace SysDiag.Services;

public sealed class UpdateService : IDiagnosticService
{
    public string Clave => "actualizaciones";
    public Task EjecutarAsync(DiagnosticReport report, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        SystemModule.Run(report, token: token);
        token.ThrowIfCancellationRequested();
        UpdateModule.Run(report, token);
    }, token);
}
