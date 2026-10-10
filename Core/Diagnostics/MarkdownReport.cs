using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SysDiag.Core.Recommendations;
using SysDiag.Models;

namespace SysDiag.Core.Diagnostics;

/// <summary>
/// Informe en Markdown.
///
/// El HTML está muy bien para abrirlo y para archivarlo, pero no sirve para el
/// lugar donde casi siempre se pide ayuda: un foro, un ticket, un hilo de
/// soporte. Ahí se pega texto. Hoy la gente pega una captura de pantalla del
/// resumen, que es lo único que se puede copiar, y quien intenta ayudar se
/// queda sin el detalle que haría falta.
///
/// Es deliberadamente más corto que el HTML: no refleja las treinta tablas,
/// sino lo que alguien necesita para orientarse —puntaje, alcance, hallazgos,
/// recomendaciones y el equipo—. Si hace falta el detalle completo, se manda
/// el JSON.
/// </summary>
public static class MarkdownReport
{
    public static string Construir(DiagnosticReport r)
    {
        var sb = new StringBuilder();
        int puntaje = r.Puntaje >= 0 ? r.Puntaje : HealthScore.Calcular(r);

        sb.AppendLine($"# Diagnóstico SysDiag — {Limpiar(r.Equipo)}");
        sb.AppendLine();
        sb.AppendLine($"| | |");
        sb.AppendLine($"|---|---|");
        sb.AppendLine($"| **Puntaje** | {puntaje}/100 — {HealthScore.Etiqueta(puntaje)} |");
        sb.AppendLine($"| **Fecha** | {r.Inicio:yyyy-MM-dd HH:mm} |");
        string duracion = r.Fin.HasValue && r.Fin >= r.Inicio
            ? $"{(int)(r.Fin.Value - r.Inicio).TotalSeconds} s" : "no registrada";
        sb.AppendLine($"| **Duración** | {duracion} |");
        sb.AppendLine($"| **Estado** | {Limpiar(r.EstadoEjecucion)} |");
        sb.AppendLine($"| **SysDiag** | v{AppEnv.Version} |");
        if (!string.IsNullOrWhiteSpace(r.CosteMedicion))
            sb.AppendLine($"| **Coste de la medición** | {Limpiar(r.CosteMedicion)} |");
        sb.AppendLine();

        // El alcance va antes de los hallazgos. Un puntaje de 92 sobre un
        // diagnóstico al que le faltaron tres módulos no significa lo mismo que
        // un 92 sobre uno completo, y sin esta línea la tabla de arriba miente
        // por omisión.
        var pendientes = r.ModulosFaltantes();
        sb.AppendLine("## Alcance");
        sb.AppendLine();
        sb.AppendLine(Limpiar(r.ResumenEstado()));
        if (pendientes.Count > 0)
            sb.AppendLine();
        if (pendientes.Count > 0)
            sb.AppendLine("Módulos pendientes: " + string.Join(", ", pendientes) + ".");
        if (r.ModulosColgados.Count > 0)
            sb.AppendLine();
        if (r.ModulosColgados.Count > 0)
            sb.AppendLine("**Módulos omitidos por no responder:** " +
                string.Join(", ", r.ModulosColgados.Select(c =>
                    DiagnosticReport.NombresModulos.TryGetValue(c, out var n) ? n : c)) + ".");
        sb.AppendLine();

        if (r.Hallazgos.Count > 0)
        {
            sb.AppendLine("## Hallazgos");
            sb.AppendLine();
            foreach (var f in r.Hallazgos.OrderBy(x => Orden(x.Severity)))
            {
                string marca = f.Severity switch
                {
                    Severity.Bad => "🔴 Crítico",
                    Severity.Warn => "🟠 Aviso",
                    _ => "🟢 Correcto"
                };
                sb.AppendLine($"### {marca} — {Limpiar(f.Area)}");
                sb.AppendLine();
                sb.AppendLine(Limpiar(f.Message));
                if (!string.IsNullOrWhiteSpace(f.Action))
                {
                    sb.AppendLine();
                    sb.AppendLine("> " + Limpiar(f.Action));
                }
                sb.AppendLine();
            }
        }
        else
        {
            sb.AppendLine("## Hallazgos");
            sb.AppendLine();
            sb.AppendLine("Sin hallazgos en los datos disponibles.");
            sb.AppendLine();
        }

        var recomendaciones = r.Recomendaciones.Count > 0 ? r.Recomendaciones : RecommendationEngine.Generate(r);
        if (recomendaciones.Count > 0)
        {
            sb.AppendLine("## Recomendaciones");
            sb.AppendLine();
            foreach (var rec in recomendaciones)
                sb.AppendLine($"- **[{Limpiar(rec.Prioridad)}] {Limpiar(rec.Titulo)}** — {Limpiar(rec.Descripcion)}");
            sb.AppendLine();
        }

        if (r.Sistema.Count > 0)
        {
            sb.AppendLine("## Equipo");
            sb.AppendLine();
            sb.Append(Tabla(r.Sistema, "Dato", "Valor"));
            sb.AppendLine();
        }

        if (r.Discos.Count > 0) { sb.AppendLine("## Discos"); sb.AppendLine(); sb.Append(Tabla(r.Discos)); sb.AppendLine(); }
        if (r.Memoria.Count > 0) { sb.AppendLine("## Memoria"); sb.AppendLine(); sb.Append(Tabla(r.Memoria)); sb.AppendLine(); }
        if (r.RendimientoResumen.Count > 0) { sb.AppendLine("## Rendimiento"); sb.AppendLine(); sb.Append(Tabla(r.RendimientoResumen)); sb.AppendLine(); }
        if (r.Red.Count > 0) { sb.AppendLine("## Red y latencia"); sb.AppendLine(); sb.Append(Tabla(r.Red)); sb.AppendLine(); }
        if (r.Termicas.Count > 0) { sb.AppendLine("## Térmicas"); sb.AppendLine(); sb.Append(Tabla(r.Termicas)); sb.AppendLine(); }
        if (r.Almacenamiento.Count > 0) { sb.AppendLine("## Almacenamiento"); sb.AppendLine(); sb.Append(Tabla(r.Almacenamiento)); sb.AppendLine(); }
        if (r.Seguridad.Count > 0) { sb.AppendLine("## Seguridad"); sb.AppendLine(); sb.Append(Tabla(r.Seguridad)); sb.AppendLine(); }
        if (r.Pantallazos.Count > 0) { sb.AppendLine("## Pantallazos decodificados"); sb.AppendLine(); sb.Append(Tabla(r.Pantallazos)); sb.AppendLine(); }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"_Generado por SysDiag v{AppEnv.Version}. El puntaje describe la evidencia disponible; no certifica ausencia de fallos._");
        return sb.ToString();
    }

    public static string Build(DiagnosticReport r, string outputDirectory = null)
    {
        string folder = outputDirectory ?? AppEnv.OutputPath;
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder,
            $"informe_{DateTime.Now:yyyyMMdd_HHmmss_fffffff}_{Guid.NewGuid():N}.md");
        AtomicFile.WriteAllText(file, Construir(r), new UTF8Encoding(true));
        AppLog.Write($"Exportado: {file}", "OK");
        return file;
    }

    // ---- Tablas ------------------------------------------------------------

    private static string Tabla(IList filas, string primera = null, string segunda = null)
    {
        if (filas == null || filas.Count == 0) return "";

        var props = TypeDescriptor.GetProperties(filas[0].GetType()).Cast<PropertyDescriptor>()
            .Where(p => p.IsBrowsable).ToList();
        if (props.Count == 0) return "";

        var sb = new StringBuilder();
        var encabezados = props.Select(p => p.DisplayName).ToList();
        if (primera != null && encabezados.Count > 0) encabezados[0] = primera;
        if (segunda != null && encabezados.Count > 1) encabezados[1] = segunda;

        sb.AppendLine("| " + string.Join(" | ", encabezados) + " |");
        sb.AppendLine("|" + string.Join("|", encabezados.Select(_ => "---")) + "|");
        foreach (var fila in filas)
        {
            if (fila == null) continue;
            // Un `|` dentro de una celda rompe la tabla entera, no solo la
            // fila: en Markdown no hay escape cómodo y lo que se ve después es
            // una columna fantasma.
            sb.AppendLine("| " + string.Join(" | ", props.Select(p =>
                Celda(p.GetValue(fila)?.ToString() ?? ""))) + " |");
        }
        return sb.ToString();
    }

    private static string Celda(string valor) =>
        Limpiar(valor).Replace("|", "\\|", StringComparison.Ordinal);

    /// <summary>Quita saltos de línea y espacios repetidos: dentro de una celda rompen la tabla.</summary>
    private static string Limpiar(string texto)
    {
        if (string.IsNullOrEmpty(texto)) return "";
        return string.Join(" ", texto.Split('\r', '\n').Select(l => l.Trim()).Where(l => l.Length > 0));
    }

    private static int Orden(Severity s) => s switch { Severity.Bad => 0, Severity.Warn => 1, _ => 2 };
}
