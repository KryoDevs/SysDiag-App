using System.Threading;
using System.Threading.Tasks;
using SysDiag.Models;

namespace SysDiag.Services;

/// <summary>
/// Contrato común de todos los servicios de diagnóstico: reciben el reporte
/// donde escribir y un token de cancelación, y devuelven la tarea que corre
/// el módulo. Esta es la capa que hace testeable el motor: en un test se
/// puede implementar un servicio falso sin tocar WMI ni el registro real.
/// </summary>
public interface IDiagnosticService
{
    /// <summary>Nombre corto usado como clave de módulo al fusionar reportes parciales.</summary>
    string Clave { get; }
    Task EjecutarAsync(DiagnosticReport reporte, CancellationToken token);
}

public interface IHardwareService : IDiagnosticService { }
public interface INetworkService : IDiagnosticService { }
public interface IStorageService : IDiagnosticService { }
public interface ISecurityService : IDiagnosticService { }
public interface IDriverService : IDiagnosticService { }

/// <summary>Orquesta la ejecución de uno o varios IDiagnosticService y fusiona sus resultados.</summary>
public interface IScanService
{
    Task<DiagnosticReport> EjecutarAsync(
        DiagnosticReport acumulado, IDiagnosticService[] pasos, CancellationToken token);
}
