using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ComponentModel;
using System.Net;
using System.Text;
using SysDiag.Core.Recommendations;
using SysDiag.Models;

namespace SysDiag.Core.Diagnostics;

public static class ReportBuilder
{
    public static string Build(DiagnosticReport r, string outputDirectory = null)
    {
        string folder = outputDirectory ?? AppEnv.OutputPath;
        Directory.CreateDirectory(folder);
        r.Puntaje = HealthScore.Calcular(r);
        r.ActualizarRecomendaciones();
        string file = Path.Combine(folder, $"informe_{DateTime.Now:yyyyMMdd_HHmmss_fffffff}_{Guid.NewGuid():N}.html");
        var sb = new StringBuilder();

        sb.AppendLine("<!DOCTYPE html><html lang=\"es\"><head><meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.AppendLine($"<title>SysDiag — {E(r.Equipo)}</title>");
        sb.AppendLine($"<style>{Css}</style></head><body><div class=\"wrap\">");

        string dur = r.Fin.HasValue && r.Fin >= r.Inicio
            ? $"{(int)(r.Fin.Value - r.Inicio).TotalSeconds} s" : "no registrada";
        sb.AppendLine("<header><h1>Informe de diagnóstico</h1><div class=\"slate\">");
        sb.AppendLine($"<span>Equipo <b>{E(r.Equipo)}</b></span>");
        sb.AppendLine($"<span>Fecha <b>{r.Inicio:yyyy-MM-dd HH:mm}</b></span>");
        sb.AppendLine($"<span>Duración <b>{E(dur)}</b></span>");
        sb.AppendLine($"<span>SysDiag <b>v{AppEnv.Version}</b></span>");
        if (r.Puntaje >= 0)
            sb.AppendLine($"<span>Puntaje <b>{r.Puntaje}/100</b> — {E(HealthScore.Etiqueta(r.Puntaje))}</span>");
        sb.AppendLine($"<span>Estado <b>{E(r.EstadoEjecucion)}</b></span>");
        sb.AppendLine("</div></header>");
        var pending = r.ModulosFaltantes();
        Section(sb, "Alcance de la medición", $"<p>{E(r.ResumenEstado())}</p><p>{E(pending.Count == 0
            ? "Se intentaron todos los módulos; sensores o consultas ausentes no equivalen a un equipo sano."
            : "Diagnóstico parcial. Módulos pendientes: " + string.Join(", ", pending))}</p>",
            "El puntaje describe solo la evidencia disponible; no certifica ausencia de fallos.");

        // Los hallazgos van primero: es lo único que la mayoría va a leer.
        if (r.Hallazgos.Count > 0)
        {
            var body = new StringBuilder();
            foreach (var f in r.Hallazgos.OrderBy(x => Orden(x.Severity)))
            {
                string clase = f.Severity == Severity.Bad ? "bad" : f.Severity == Severity.Warn ? "warn" : "ok";
                body.Append($"<div class=\"finding {clase}\">");
                body.Append($"<div class=\"tag\">{E(f.Etiqueta)} · {E(f.Area)}</div>");
                body.Append($"<div class=\"txt\"><b>{E(f.Message)}</b>");
                if (!string.IsNullOrWhiteSpace(f.Action)) body.Append($"<span>{E(f.Action)}</span>");
                body.Append("</div></div>");
            }
            Section(sb, "Hallazgos", body.ToString(),
                "Ordenados por severidad. Cada línea indica qué se detectó y qué hacer al respecto.");
        }

        var recomendaciones = r.Recomendaciones.Count > 0 ? r.Recomendaciones : RecommendationEngine.Generate(r);
        if (recomendaciones.Count > 0)
        {
            var body = new StringBuilder();
            foreach (var rec in recomendaciones)
            {
                body.Append("<div class=\"finding warn\">");
                body.Append($"<div class=\"tag\">{E(rec.Prioridad)}</div>");
                body.Append($"<div class=\"txt\"><b>{E(rec.Titulo)}</b><span>{E(rec.Descripcion)}</span></div>");
                body.Append("</div>");
            }
            Section(sb, "Recomendaciones", body.ToString(),
                "Sugerencias en prioridad para resolver o atenuar los problemas detectados.");
        }

        if (r.Sistema.Count > 0)
        {
            var body = new StringBuilder(KeyValue(r.Sistema));
            body.Append(Table(r.Discos));
            body.Append(Table(r.Memoria));
            Section(sb, "Equipo", body.ToString());
        }

        if (r.WiFi.Count > 0)
            Section(sb, "Enlace inalámbrico", KeyValue(r.WiFi),
                "La calidad que reporta Windows es un porcentaje, no dBm. La conversión aproximada asume escala lineal entre -100 y -50 dBm.");

        if (r.Red.Count > 0)
            Section(sb, "Calidad de enlace", Table(r.Red),
                "El jitter es la variación entre paquetes consecutivos: por encima de 15 ms se percibe como tirón aunque el ping medio sea bajo. Comparar el salto al router con la salida a internet separa un problema de red local de uno del proveedor.");

        if (r.RendimientoResumen.Count > 0)
        {
            var body = new StringBuilder(KeyValue(r.RendimientoResumen));
            body.Append("<h3>Mayor consumo de CPU</h3>").Append(Table(r.TopCpu));
            body.Append("<h3>Mayor consumo de memoria</h3>").Append(Table(r.TopRam));
            Section(sb, "Rendimiento", body.ToString(),
                "El uso de CPU es la diferencia real medida entre dos instantes y normalizada por núcleo, no el tiempo acumulado desde que arrancó el proceso.");
        }

        if (r.Termicas.Count > 0)
            Section(sb, "Térmicas y frecuencia", KeyValue(r.Termicas));

        if (r.Bateria.Count > 0)
            Section(sb, "Batería", KeyValue(r.Bateria),
                "El desgaste compara la capacidad máxima de carga actual contra la capacidad de diseño de fábrica.");

        if (r.EventosResumen.Count > 0 || r.Minidumps.Count > 0 || r.Pantallazos.Count > 0)
        {
            var body = new StringBuilder();
            if (r.EventosResumen.Count > 0) body.Append(Table(r.EventosResumen));
            if (r.Pantallazos.Count > 0) body.Append("<h3>Pantallazos decodificados</h3>").Append(Table(r.Pantallazos));
            if (r.Minidumps.Count > 0) body.Append("<h3>Volcados de memoria</h3>").Append(Table(r.Minidumps));
            if (r.EventosDetalle.Count > 0) body.Append("<h3>Últimos eventos</h3>").Append(Table(r.EventosDetalle));
            Section(sb, "Estabilidad", body.ToString(),
                $"Eventos críticos del registro del sistema en los últimos {StabilityModule.EventDays} días.");
        }

        if (r.Whea.Count > 0)
            Section(sb, "Errores WHEA (hardware)", Table(r.Whea),
                $"Eventos del proveedor Microsoft-Windows-WHEA-Logger en los últimos {StabilityModule.WheaDays} días: errores de CPU, RAM o PCIe que el firmware corrigió automáticamente. Suelen preceder a una pantalla azul si se repiten.");

        if (r.Traceroute.Count > 0)
            Section(sb, "Traceroute — " + r.TracerouteDestino, Table(r.Traceroute),
                "Cada salto es un router intermedio entre el equipo y el destino. Un salto que tarda mucho más que el anterior señala dónde empieza el problema en la ruta.");

        if (r.Limpieza.Count > 0)
        {
            string nota = string.IsNullOrEmpty(r.EspacioLiberado)
                ? "Análisis sin borrado."
                : $"Espacio liberado: {r.EspacioLiberado}.";
            Section(sb, "Limpieza", Table(r.Limpieza), nota);
        }

        if (r.Almacenamiento.Count > 0)
            Section(sb, "Salud del almacenamiento", Table(r.Almacenamiento),
                "Contadores de fiabilidad del propio disco. Errores no corregidos o desgaste alto no se arreglan con software.");

        if (r.RedesCercanas.Count > 0)
            Section(sb, "Redes cercanas", Table(r.RedesCercanas),
                "Redes que comparten espectro. En 2.4 GHz los canales se solapan entre sí; en 5 GHz solo interfiere quien use el mismo canal.");

        if (r.Actualizaciones.Count > 0)
            Section(sb, "Actualizaciones disponibles", Table(r.Actualizaciones),
                "Programas con versión más reciente publicada en el repositorio oficial de winget.");

        if (r.Arranque.Count > 0)
            Section(sb, "Programas al inicio", Table(r.Arranque),
                "Cada entrada suma tiempo de arranque y memoria en reposo.");

        if (r.Servicios.Count > 0)
            Section(sb, "Servicios automáticos", Table(r.Servicios),
                "Solo informativo. Desactivar servicios sin saber qué hacen es una vía rápida a un sistema inestable.");

        if (r.Programas.Count > 0)
            Section(sb, "Programas instalados", Table(r.Programas));

        if (r.Drivers.Count > 0)
            Section(sb, "Drivers", Table(r.Drivers),
                "Solo lectura: la app no descarga ni instala nada. Para actualizar, usa Windows Update ▸ Actualizaciones opcionales o la página de soporte del fabricante.");

        if (r.Seguridad.Count > 0)
            Section(sb, "Seguridad", Table(r.Seguridad),
                "Defender, Firewall, BitLocker, TPM, Secure Boot y UAC. Todo de solo lectura: la app nunca cambia ninguno de estos componentes por su cuenta.");

        if (r.Gpus.Count > 0)
            Section(sb, "GPU", Table(r.Gpus),
                "El uso solo se mide cuando hay una única GPU activa: con gráficos híbridos no hay forma confiable de saber a cuál de las dos atribuírselo, así que se deja vacío en vez de adivinar.");

        if (!r.TieneDatosRelevantes())
            Section(sb, "Diagnóstico incompleto",
                $"<p>{E(r.ResumenEstado())}</p>",
                "Esto suele pasar cuando el equipo no expone WMI o el usuario no tiene permisos de administrador para consultar algunos módulos.");

        sb.AppendLine($"<footer>Generado por SysDiag v{AppEnv.Version} · registro completo en {E(AppLog.File)}</footer>");
        sb.AppendLine("</div></body></html>");

        AtomicFile.WriteAllText(file, sb.ToString(), new UTF8Encoding(true));
        AppLog.Write($"Informe generado: {file}", "OK");
        return file;
    }

    private static int Orden(Severity s) => s == Severity.Bad ? 0 : s == Severity.Warn ? 1 : 2;

    private static void Section(StringBuilder sb, string title, string body, string note = null)
    {
        sb.AppendLine($"<section><h2>{E(title)}</h2>");
        if (!string.IsNullOrEmpty(note)) sb.AppendLine($"<p class=\"note\">{E(note)}</p>");
        sb.AppendLine(body);
        sb.AppendLine("</section>");
    }

    private static string KeyValue(List<KeyValueRow> rows)
    {
        var sb = new StringBuilder("<table class=\"kv\">");
        foreach (var row in rows)
            sb.Append($"<tr><th scope=\"row\">{E(row.Clave)}</th><td>{E(row.Valor)}</td></tr>");
        return sb.Append("</table>").ToString();
    }

    /// <summary>Construye la tabla leyendo los DisplayName de las propiedades del modelo.</summary>
    private static string Table<T>(List<T> rows)
    {
        if (rows == null || rows.Count == 0) return "";

        var props = TypeDescriptor.GetProperties(typeof(T))
            .Cast<PropertyDescriptor>()
            .Where(p => p.IsBrowsable)
            .ToList();

        var sb = new StringBuilder("<table><thead><tr>");
        foreach (var p in props) sb.Append($"<th>{E(p.DisplayName)}</th>");
        sb.Append("</tr></thead><tbody>");

        foreach (var row in rows)
        {
            sb.Append("<tr>");
            foreach (var p in props) sb.Append($"<td>{E(p.GetValue(row)?.ToString() ?? "")}</td>");
            sb.Append("</tr>");
        }

        return sb.Append("</tbody></table>").ToString();
    }

    private static string E(string s) => WebUtility.HtmlEncode(s ?? "");

    private const string Css = @"
:root{
  --paper:#EDEEF0; --panel:#FFFFFF; --ink:#14181D; --muted:#5C646E;
  --rule:#C9CDD3; --line:#17607A; --ok:#2C6E49; --warn:#B8720C; --bad:#A5222B;
}
*{box-sizing:border-box}
body{margin:0;padding:32px 20px 64px;background:var(--paper);color:var(--ink);
  font-family:'Segoe UI Variable Text','Segoe UI',system-ui,sans-serif;font-size:15px;line-height:1.55}
.wrap{max-width:1040px;margin:0 auto}
h1{font-family:Bahnschrift,'DIN Alternate','Segoe UI',sans-serif;font-weight:600;
  font-size:34px;letter-spacing:.06em;text-transform:uppercase;margin:0}
h2{font-family:Bahnschrift,'DIN Alternate','Segoe UI',sans-serif;font-weight:600;
  font-size:15px;letter-spacing:.14em;text-transform:uppercase;color:var(--line);
  margin:0 0 14px;padding-bottom:8px;border-bottom:2px solid var(--line)}
h3{font-size:12px;letter-spacing:.1em;text-transform:uppercase;color:var(--muted);
  margin:26px 0 8px;font-weight:600}
header{border-bottom:3px solid var(--ink);padding-bottom:16px}
.slate{display:flex;flex-wrap:wrap;gap:0 36px;margin-top:12px;
  font-family:Consolas,'Cascadia Mono',monospace;font-size:12.5px;color:var(--muted)}
.slate b{color:var(--ink);font-weight:600}
section{background:var(--panel);border:1px solid var(--rule);padding:22px 24px;margin-top:24px}
table{width:100%;border-collapse:collapse;font-size:14px;margin-bottom:4px}
th,td{text-align:left;padding:7px 10px;border-bottom:1px solid var(--rule);vertical-align:top}
thead th{font-size:11px;letter-spacing:.09em;text-transform:uppercase;color:var(--muted);
  border-bottom:1.5px solid var(--ink);white-space:nowrap}
tbody td:not(:first-child){font-family:Consolas,'Cascadia Mono',monospace;font-variant-numeric:tabular-nums}
table.kv th{width:240px;font-weight:600;color:var(--muted)}
table.kv td{font-family:Consolas,'Cascadia Mono',monospace}
.note{color:var(--muted);font-size:13.5px;margin:-4px 0 16px;max-width:74ch}
.finding{display:grid;grid-template-columns:150px 1fr;gap:0 18px;
  border-left:6px solid var(--rule);padding:12px 0 12px 16px;
  border-bottom:1px solid var(--rule)}
.finding:last-child{border-bottom:0}
.finding.ok{border-left-color:var(--ok)}
.finding.warn{border-left-color:var(--warn)}
.finding.bad{border-left-color:var(--bad)}
.finding .tag{font-family:Consolas,monospace;font-size:11px;letter-spacing:.07em;
  text-transform:uppercase;color:var(--muted);padding-top:3px}
.finding .txt b{display:block;margin-bottom:3px}
.finding .txt span{color:var(--muted);font-size:13.5px}
footer{margin-top:32px;padding-top:14px;border-top:1px solid var(--rule);
  color:var(--muted);font-size:12.5px;font-family:Consolas,monospace;word-break:break-all}
@media(max-width:680px){.finding{grid-template-columns:1fr}table.kv th{width:auto}}
";
}
