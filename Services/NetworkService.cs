using SysDiag.Core.Hardware;
using SysDiag.Core.Network;
using SysDiag.Models;

namespace SysDiag.Services;

public class NetworkService : INetworkService
{
    public string Clave => "red";
    // netsh y el inventario son síncronos: tampoco pueden ejecutarse en el dispatcher de WPF.
    public Task EjecutarAsync(DiagnosticReport report, CancellationToken token) => Task.Run(async () =>
    {
        token.ThrowIfCancellationRequested();
        SystemModule.Run(report, token: token);
        token.ThrowIfCancellationRequested();
        await NetworkModule.RunAsync(report, token);
    }, token);
}
