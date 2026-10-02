using SysDiag.Core.Storage;
using SysDiag.Models;

namespace SysDiag.Services;

/// <summary>El diagnóstico completo solo analiza; jamás limpia sin confirmación del usuario.</summary>
public sealed class CleanupService : IDiagnosticService
{
    public string Clave => "limpieza";
    public Task EjecutarAsync(DiagnosticReport report, CancellationToken token) =>
        Task.Run(() => CleanupModule.Analyze(report, token), token);
}
