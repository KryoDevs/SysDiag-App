using System.Linq;
using SysDiag.Models;
using SysDiag.Core;

namespace SysDiag.Diagnostics;

/// <summary>Ejemplo de regla: RAM por encima del umbral donde Windows empieza a paginar.</summary>
public class MemoryRules : IDiagnosticRule
{
    public string Id => "memoria.uso-alto";
    public string Categoria => "Memoria";

    public void Evaluar(DiagnosticReport reporte)
    {
        var fila = reporte.RendimientoResumen.FirstOrDefault(x => x.Clave == "RAM en uso");
        if (fila == null) return;

        if (!NumericText.TryRead(fila.Valor, out double pct)) return;
        if (pct <= 85) return;

        reporte.Add(Severity.Warn, "Memoria", $"RAM al {pct}%.",
            "Con la memoria tan ocupada Windows empieza a paginar a disco y aparecen microtirones.", modulo: "rendimiento");
    }
}
