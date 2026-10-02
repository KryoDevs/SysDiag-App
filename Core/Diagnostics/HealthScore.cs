using System;
using System.Collections.Generic;
using System.Linq;
using SysDiag.Models;

namespace SysDiag.Core.Diagnostics;

/// <summary>
/// Puntaje compuesto de 0 a 100. No pretende ser una medida absoluta de nada:
/// es un titular que ordena la atención. Por eso siempre se muestra junto a su
/// desglose — un número solo, sin el porqué, es humo.
/// </summary>
public static class HealthScore
{
    public static int Calcular(DiagnosticReport r)
    {
        if (!r.TieneDatosRelevantes()) return -1;

        int puntaje = 100;

        foreach (var h in r.Hallazgos)
        {
            // Los pesos se reparten por severidad y no por área: un crítico de
            // almacenamiento y uno de red duelen lo mismo para el usuario.
            puntaje -= h.Severity switch
            {
                Severity.Bad => 15,
                Severity.Warn => 5,
                _ => 0
            };
        }

        return Math.Clamp(puntaje, 0, 100);
    }

    public static string Etiqueta(int puntaje) => puntaje switch
    {
        < 0 => "sin datos",
        < 50 => "Requiere atención",
        < 75 => "Con reparos",
        < 90 => "Aceptable",
        _ => "En buen estado"
    };

    public static Severity Nivel(int puntaje) => puntaje switch
    {
        < 0 => Severity.Ok,
        < 50 => Severity.Bad,
        < 90 => Severity.Warn,
        _ => Severity.Ok
    };

    /// <summary>Áreas que más restan, para explicar el número.</summary>
    public static string Desglose(DiagnosticReport r)
    {
        var areas = r.Hallazgos
            .Where(h => h.Severity != Severity.Ok)
            .GroupBy(h => h.Area)
            .OrderByDescending(g => g.Count(x => x.Severity == Severity.Bad) * 3 + g.Count())
            .Take(3)
            .Select(g => g.Key)
            .ToList();

        return areas.Count == 0 ? "sin hallazgos que resten" : "afecta: " + string.Join(", ", areas);
    }
}
