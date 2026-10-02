using System.Threading;
using System.Threading.Tasks;
using SysDiag.Core.Hardware;
using SysDiag.Core.Security;
using SysDiag.Models;

namespace SysDiag.Services;

/// <summary>Defender, Firewall, BitLocker, TPM, Secure Boot y UAC. Todo de solo lectura.</summary>
public class SecurityService : ISecurityService
{
    public string Clave => "seguridad";

    public Task EjecutarAsync(DiagnosticReport reporte, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        SystemModule.Run(reporte, token: token);
        token.ThrowIfCancellationRequested();
        SecurityModule.Run(reporte);
    }, token);
}
