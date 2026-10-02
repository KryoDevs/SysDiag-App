using System.Linq;
using SysDiag.Models;

namespace SysDiag.Diagnostics;

/// <summary>
/// Ejemplo de regla extraída a su propia clase: un proceso que domina la CPU
/// durante la medición. La lógica es la misma que ya corría inline dentro de
/// PerformanceModule; vive aquí también para no duplicarla — PerformanceModule
/// ya no la evalúa, delega en esta regla al terminar de recolectar datos.
/// </summary>
public class CpuRules : IDiagnosticRule
{
    public string Id => "cpu.proceso-dominante";
    public string Categoria => "CPU";

    public void Evaluar(DiagnosticReport reporte)
    {
        var top = reporte.TopCpu.FirstOrDefault();
        if (top == null || top.CpuPct <= 40) return;

        reporte.Add(Severity.Warn, "CPU",
            $"{top.Proceso} consumió {top.CpuPct}% de CPU durante la medición.",
            "Comprueba si es esperable. Un proceso sostenido por encima del 40% deja poco margen para el resto.", modulo: "rendimiento");
    }
}
