using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SysDiag.Models;

namespace SysDiag.Core.Diagnostics;

/// <summary>Un hallazgo comparado entre dos diagnósticos.</summary>
public sealed class HallazgoComparado
{
    public Severity Severity { get; init; }
    public string Area { get; init; } = "";
    public string Message { get; init; } = "";
    public string Action { get; init; } = "";

    /// <summary>La severidad que tenía antes, o `null` si el hallazgo es nuevo.</summary>
    public Severity? SeveridadAnterior { get; init; }

    public bool EsNuevo => SeveridadAnterior == null;
    public string Etiqueta => Severity switch
    {
        Severity.Bad => "Crítico",
        Severity.Warn => "Atención",
        _ => "Correcto"
    };
}

/// <summary>Una medición comparada. `Peor` lo decide quien la construye, no el diff.</summary>
public sealed class MedicionComparada
{
    public string Concepto { get; init; } = "";
    public string Antes { get; init; } = "n/d";
    public string Despues { get; init; } = "n/d";
    public bool Peor { get; init; }
    public string Nota { get; init; } = "";
}

/// <summary>
/// Diferencia entre dos diagnósticos del mismo equipo.
///
/// Es la pantalla que un técnico abre primero, y la que faltaba: los datos ya
/// estaban archivados desde hacía versiones, pero solo se podían mirar uno por
/// uno. «¿Qué cambió desde la última vez?» exigía acordarse de lo que decía el
/// anterior.
///
/// Dos decisiones que no son obvias:
///
///  1. **Los hallazgos se emparejan por área y mensaje, no por severidad.** Si
///     se emparejaran por severidad también, un hallazgo que pasó de Aviso a
///     Crítico aparecería como «uno resuelto y uno nuevo», que es justo la
///     lectura contraria a lo que pasó.
///  2. **Solo se comparan mediciones estructuradas.** El desgaste del SSD viaja
///     dentro de un texto («12 %») y recuperarlo exige parsear una cadena ya
///     formateada; si el formato cambia, la comparación deja de encontrar nada
///     y no hay forma de darse cuenta. Hasta que exista el contrato de medición
///     de `docs/MEJORAS.md §8.2.1`, esto compara lo que es número y no texto.
/// </summary>
public sealed class ReportDiff
{
    public DateTime FechaAnterior { get; init; }
    public DateTime FechaActual { get; init; }
    public int PuntajeAnterior { get; init; }
    public int PuntajeActual { get; init; }
    public int DeltaPuntaje => PuntajeActual - PuntajeAnterior;

    /// <summary>
    /// Si los dos diagnósticos midieron lo mismo. Comparar un «Red» suelto con
    /// un diagnóstico completo produce un puntaje que bajó sin que nada
    /// empeorara: la diferencia es de cobertura, no del equipo. Se declara y el
    /// usuario decide.
    /// </summary>
    public bool MismaCobertura { get; init; }
    public string CoberturaAnterior { get; init; } = "";
    public string CoberturaActual { get; init; } = "";

    public List<HallazgoComparado> Nuevos { get; init; } = new();
    public List<HallazgoComparado> Resueltos { get; init; } = new();
    public List<HallazgoComparado> Empeoraron { get; init; } = new();
    public List<HallazgoComparado> Mejoraron { get; init; } = new();
    public List<HallazgoComparado> Iguales { get; init; } = new();
    public List<MedicionComparada> Mediciones { get; init; } = new();

    public bool Vacio => Nuevos.Count == 0 && Resueltos.Count == 0
        && Empeoraron.Count == 0 && Mejoraron.Count == 0 && Mediciones.Count == 0;

    /// <summary>
    /// Una frase con el balance. Se arma acá y no en la vista porque tiene
    /// plurales y un caso «no comparable»: en XAML sería un conversor con
    /// estados.
    /// </summary>
    public string Resumen()
    {
        var partes = new List<string>();
        if (Nuevos.Count > 0) partes.Add($"{Nuevos.Count} {Plural(Nuevos.Count, "hallazgo nuevo", "hallazgos nuevos")}");
        if (Empeoraron.Count > 0) partes.Add($"{Empeoraron.Count} {Plural(Empeoraron.Count, "empeoró", "empeoraron")}");
        if (Resueltos.Count > 0) partes.Add($"{Resueltos.Count} {Plural(Resueltos.Count, "resuelto", "resueltos")}");
        if (Mejoraron.Count > 0) partes.Add($"{Mejoraron.Count} {Plural(Mejoraron.Count, "mejoró", "mejoraron")}");
        if (partes.Count == 0) return "Sin cambios en los hallazgos entre las dos fechas.";
        return string.Join(" · ", partes) + ".";
    }

    /// <summary>
    /// Los plurales van acá y no en la plantilla: «1 hallazgo(s) nuevo(s)» se
    /// lee como texto sin terminar, y es la primera línea que mira el usuario.
    /// </summary>
    private static string Plural(int n, string singular, string plural) =>
        n == 1 ? singular : plural;

    public string PuntajeTexto()
    {
        if (PuntajeAnterior < 0 || PuntajeActual < 0) return "sin puntaje comparable";
        if (DeltaPuntaje == 0) return $"{PuntajeActual}/100 (sin cambios)";
        return $"{PuntajeActual}/100 ({DeltaPuntaje:+#;-#;0} respecto de {PuntajeAnterior})";
    }
}

public static class DiffEngine
{
    /// <summary>
    /// Compara dos diagnósticos. `antes` y `después` son por fecha, no por orden
    /// de paso: si se pasan al revés, la comparación sale al revés y no hay
    /// forma de detectarlo desde acá.
    /// </summary>
    public static ReportDiff Comparar(DiagnosticReport antes, DiagnosticReport despues)
    {
        if (antes == null) throw new ArgumentNullException(nameof(antes));
        if (despues == null) throw new ArgumentNullException(nameof(despues));

        var mapa = (antes.Hallazgos ?? new List<Finding>())
            .GroupBy(Clave)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nuevos = new List<HallazgoComparado>();
        var empeoraron = new List<HallazgoComparado>();
        var mejoraron = new List<HallazgoComparado>();
        var iguales = new List<HallazgoComparado>();

        foreach (var f in despues.Hallazgos ?? new List<Finding>())
        {
            string clave = Clave(f);
            vistos.Add(clave);

            if (!mapa.TryGetValue(clave, out var previo))
            {
                nuevos.Add(Convertir(f, null));
                continue;
            }

            var comparado = Convertir(f, previo.Severity);
            // Severity es Ok = 0, Warn = 1, Bad = 2: crece con la gravedad, así
            // que subir es empeorar. Conviene no comparar por nombre ni armar
            // una tabla de equivalencias: si mañana se agrega un nivel, el orden
            // del enum sigue diciendo la verdad y esto no hay que tocarlo.
            if (f.Severity > previo.Severity) empeoraron.Add(comparado);
            else if (f.Severity < previo.Severity) mejoraron.Add(comparado);
            else iguales.Add(comparado);
        }

        var resueltos = (antes.Hallazgos ?? new List<Finding>())
            .Where(f => !vistos.Contains(Clave(f)))
            .Select(f => new HallazgoComparado
            {
                Severity = Severity.Ok,
                Area = f.Area,
                Message = f.Message,
                Action = f.Action,
                SeveridadAnterior = f.Severity
            })
            .ToList();

        return new ReportDiff
        {
            FechaAnterior = antes.Inicio,
            FechaActual = despues.Inicio,
            PuntajeAnterior = antes.Puntaje,
            PuntajeActual = despues.Puntaje,
            MismaCobertura = MismaCobertura(antes, despues),
            CoberturaAnterior = TextoCobertura(antes),
            CoberturaActual = TextoCobertura(despues),
            Nuevos = Ordenar(nuevos),
            Resueltos = Ordenar(resueltos),
            Empeoraron = Ordenar(empeoraron),
            Mejoraron = Ordenar(mejoraron),
            Iguales = Ordenar(iguales),
            Mediciones = CompararMediciones(antes, despues)
        };
    }

    /// <summary>
    /// Área y mensaje, sin severidad ni módulo. El módulo se excluye a propósito:
    /// un mismo hallazgo puede generarlo otro módulo entre una corrida y la
    /// siguiente, y eso no es un cambio.
    /// </summary>
    internal static string Clave(Finding f) =>
        $"{(f.Area ?? "").Trim()}|{(f.Message ?? "").Trim()}";

    private static HallazgoComparado Convertir(Finding f, Severity? anterior) => new()
    {
        Severity = f.Severity,
        Area = f.Area ?? "",
        Message = f.Message ?? "",
        Action = f.Action ?? "",
        SeveridadAnterior = anterior
    };

    private static List<HallazgoComparado> Ordenar(List<HallazgoComparado> lista) =>
        // Descendente: Severity crece con la gravedad (Ok=0, Warn=1, Bad=2), así
        // que lo peor queda primero.
        lista.OrderByDescending(x => (int)x.Severity)
             .ThenBy(x => x.Area, StringComparer.CurrentCultureIgnoreCase)
             .ToList();

    private static bool MismaCobertura(DiagnosticReport a, DiagnosticReport b)
    {
        var ca = new HashSet<string>((a.ModulosCompletados ?? new()).Keys, StringComparer.OrdinalIgnoreCase);
        var cb = new HashSet<string>((b.ModulosCompletados ?? new()).Keys, StringComparer.OrdinalIgnoreCase);
        if (ca.Count == 0 && cb.Count == 0) return true;
        return ca.SetEquals(cb);
    }

    private static string TextoCobertura(DiagnosticReport r)
    {
        var modulos = (r.ModulosCompletados ?? new()).Keys
            .Select(k => DiagnosticReport.NombresModulos.TryGetValue(k, out var n) ? n : k)
            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return modulos.Count == 0 ? "sin registrar" : string.Join(", ", modulos);
    }

    /// <summary>
    /// Solo mediciones que son número en el modelo. Ver el comentario de la
    /// clase sobre por qué no se comparan las que viajan dentro de un texto.
    /// </summary>
    private static List<MedicionComparada> CompararMediciones(DiagnosticReport a, DiagnosticReport b)
    {
        var lista = new List<MedicionComparada>();

        // --- Espacio libre por unidad -------------------------------------
        foreach (var disco in b.Discos ?? new List<DiskRow>())
        {
            if (string.IsNullOrWhiteSpace(disco.Unidad)) continue;
            var previo = (a.Discos ?? new List<DiskRow>())
                .FirstOrDefault(d => string.Equals(d.Unidad, disco.Unidad, StringComparison.OrdinalIgnoreCase));
            if (previo == null) continue;

            double delta = disco.LibrePct - previo.LibrePct;
            if (Math.Abs(delta) < 0.5) continue;
            lista.Add(new MedicionComparada
            {
                Concepto = $"Espacio libre en {disco.Unidad}",
                Antes = $"{previo.LibrePct:0.0} %",
                Despues = $"{disco.LibrePct:0.0} %",
                Peor = delta < 0,
                Nota = delta < 0
                    ? $"{Math.Abs(delta):0.0} puntos menos libres."
                    : $"{delta:0.0} puntos más libres."
            });
        }

        // --- Latencia por destino ------------------------------------------
        foreach (var red in b.Red ?? new List<LatencyResult>())
        {
            if (string.IsNullOrWhiteSpace(red.Destino)) continue;
            var previo = (a.Red ?? new List<LatencyResult>())
                .FirstOrDefault(x => string.Equals(x.Destino, red.Destino, StringComparison.OrdinalIgnoreCase));
            if (previo == null || previo.Media <= 0 || red.Media <= 0) continue;

            double delta = red.Media - previo.Media;
            // Menos de 3 ms es ruido de medición, no un cambio: anunciarlo haría
            // que todo el diff pareciera moverse siempre.
            if (Math.Abs(delta) < 3) continue;
            lista.Add(new MedicionComparada
            {
                Concepto = $"Latencia media hacia {red.Destino}",
                Antes = $"{previo.Media:0} ms",
                Despues = $"{red.Media:0} ms",
                Peor = delta > 0,
                Nota = delta > 0 ? $"{delta:0} ms más lenta." : $"{Math.Abs(delta):0} ms más rápida."
            });
        }

        // --- Pérdida de paquetes -------------------------------------------
        var perdida = b.Red?.Where(x => x.PerdidaPct > 0).ToList() ?? new List<LatencyResult>();
        foreach (var red in perdida)
        {
            var previo = (a.Red ?? new List<LatencyResult>())
                .FirstOrDefault(x => string.Equals(x.Destino, red.Destino, StringComparison.OrdinalIgnoreCase));
            double antesPerdida = previo?.PerdidaPct ?? 0;
            if (Math.Abs(red.PerdidaPct - antesPerdida) < 0.5) continue;
            lista.Add(new MedicionComparada
            {
                Concepto = $"Pérdida de paquetes hacia {red.Destino}",
                Antes = $"{antesPerdida:0.#} %",
                Despues = $"{red.PerdidaPct:0.#} %",
                Peor = red.PerdidaPct > antesPerdida
            });
        }

        // --- Repeticiones de eventos críticos -------------------------------
        foreach (var evento in b.EventosResumen ?? new List<EventSummaryRow>())
        {
            var previo = (a.EventosResumen ?? new List<EventSummaryRow>())
                .FirstOrDefault(x => x.Id == evento.Id
                    && string.Equals(x.Origen, evento.Origen, StringComparison.OrdinalIgnoreCase));
            if (previo == null || previo.Ocurrencias == 0) continue;

            int delta = evento.Ocurrencias - previo.Ocurrencias;
            if (delta == 0) continue;
            lista.Add(new MedicionComparada
            {
                Concepto = $"Evento {evento.Id} ({evento.Origen})",
                Antes = previo.Ocurrencias.ToString("N0", CultureInfo.CurrentCulture),
                Despues = evento.Ocurrencias.ToString("N0", CultureInfo.CurrentCulture),
                Peor = delta > 0,
                Nota = delta > 0
                    ? $"{delta:N0} repeticiones nuevas desde la medición anterior."
                    : $"{Math.Abs(delta):N0} menos."
            });
        }

        // --- Volcados de memoria -------------------------------------------
        int volcadosAntes = a.Minidumps?.Count ?? 0;
        int volcadosAhora = b.Minidumps?.Count ?? 0;
        if (volcadosAhora != volcadosAntes)
            lista.Add(new MedicionComparada
            {
                Concepto = "Volcados de memoria",
                Antes = volcadosAntes.ToString(CultureInfo.CurrentCulture),
                Despues = volcadosAhora.ToString(CultureInfo.CurrentCulture),
                Peor = volcadosAhora > volcadosAntes,
                Nota = volcadosAhora > volcadosAntes
                    ? "Hay pantallazos nuevos desde la última medición."
                    : "Hay menos volcados: puede que se hayan borrado a mano."
            });

        return lista.OrderByDescending(x => x.Peor).ThenBy(x => x.Concepto, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
