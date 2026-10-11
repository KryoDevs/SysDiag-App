using System.Collections.Generic;
using System.Linq;
using SysDiag.Models;

namespace SysDiag.Diagnostics;

/// <summary>
/// Corre un conjunto de reglas sobre un reporte ya recolectado. Las reglas
/// registradas aquí se suman a los hallazgos que cada módulo de Core/ ya
/// genera durante la recolección — no los reemplazan.
/// </summary>
public class DiagnosticEngine
{
    private readonly List<IDiagnosticRule> _reglas = new();

    public DiagnosticEngine()
    {
        // Reglas de ejemplo que muestran el patrón para lo que se agregue
        // de aquí en más. El resto de los ~40 hallazgos de la app siguen
        // viviendo dentro de sus módulos, que es donde ya están probados.
        _reglas.Add(new CpuRules());
        _reglas.Add(new MemoryRules());
    }

    public void Evaluar(DiagnosticReport reporte)
    {
        foreach (var regla in _reglas)
            regla.Evaluar(reporte);
    }

    public IReadOnlyList<IDiagnosticRule> Reglas => _reglas;
}
