using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics.Eventing.Reader;
using SysDiag.Models;

namespace SysDiag.Core.Diagnostics;

public static class StabilityModule
{
    public static int EventDays = 30;

    private const int EventosMaximo = 50000;

    // Cada fila WHEA se archiva en el JSON del historial (~200 bytes indentada): con hardware fallando, 50.000
    // filas serían ~10 MB por diagnóstico. Los conteos quedan como límite inferior, y el aviso lo declara.
    private const int WheaMaximo = 2000;

    public static void Run(DiagnosticReport r, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        AppLog.Write($"Estabilidad (últimos {EventDays} días)", "STEP");

        if (!AppEnv.IsAdmin)
            AppLog.Write("Sin privilegios de administrador: el registro puede venir incompleto.", "WARN");

        var eventos = LeerEventos(token, out bool complete);
        if (!complete)
            r.Add(Severity.Warn, "Estabilidad", "La consulta del registro de eventos quedó incompleta.",
                "Revisa permisos y Registro. No se puede concluir que no hubo fallos.");

        r.EventosResumen = eventos
            .GroupBy(e => (e.Id, e.Origen))
            .Select(g => new EventSummaryRow
            {
                Id = g.Key.Id,
                Origen = g.Key.Origen,
                Descripcion = SystemEventCatalog.Description(g.Key.Id, g.Key.Origen),
                Ocurrencias = g.Count(),
                Ultimo = g.Max(e => e.Cuando).ToString("yyyy-MM-dd HH:mm")
            })
            .OrderByDescending(x => x.Ocurrencias)
            .ToList();

        r.EventosDetalle = eventos
            .OrderByDescending(e => e.Cuando)
            .Take(DetalleMaximo)
            .Select(e => new EventRow
            {
                Fecha = e.Cuando.ToString("yyyy-MM-dd HH:mm:ss"),
                Id = e.Id,
                Origen = e.Origen,
                Detalle = e.Detalle
            })
            .ToList();

        foreach (var s in r.EventosResumen)
            AppLog.Write($"ID {s.Id,-5} x{s.Ocurrencias,-4} última {s.Ultimo}  {s.Descripcion}", "WARN");

        // ---- Kernel-Power 41 ----------------------------------------------
        var kp41 = eventos.Where(e => e.Id == 41).ToList();
        if (kp41.Count > 0)
        {
            var hora = kp41.GroupBy(e => e.Cuando.Hour)
                           .OrderByDescending(g => g.Count())
                           .First();

            r.Add(Severity.Bad, "Estabilidad",
                $"{kp41.Count} reinicios inesperados (Kernel-Power 41) en {EventDays} días.",
                $"Se concentran alrededor de las {hora.Key:00}:00. Si no hay pantalla azul asociada, el sistema se cortó sin alcanzar a registrar nada: apunta a alimentación, batería, temperatura o memoria. Comprueba si coincide con el equipo en reposo o con tareas programadas.");
        }

        // ---- Minidumps -----------------------------------------------------
        var dumps = new List<DumpRow>();
        string dumpDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Minidump");

        if (Directory.Exists(dumpDir))
        {
            try
            {
                foreach (var f in new DirectoryInfo(dumpDir)
                             .GetFiles("*.dmp")
                             .OrderByDescending(f => f.LastWriteTime)
                             .Take(15))
                {
                    token.ThrowIfCancellationRequested();
                    dumps.Add(new DumpRow
                    {
                        Archivo = f.Name,
                        Fecha = f.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                        Tamano = AppEnv.FormatBytes(f.Length)
                    });
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                AppLog.Write($"No se pudo leer {dumpDir}: {ex.Message}", "WARN");
                r.Add(Severity.Warn, "Estabilidad", "No se pudieron consultar los minidumps.", "Revisa permisos antes de concluir que no hay volcados de memoria.");
            }
        }
        r.Minidumps = dumps;

        // Los volcados se decodifican acá, sin depurador. Antes la recomendación
        // era «abrilos con WinDbg», que es correcta y casi nadie sigue: instalar
        // las herramientas de depuración para leer un número convierte un dato
        // disponible en un dato perdido.
        r.Pantallazos = BugCheckDecoder.Decodificar();

        if (dumps.Count > 0)
        {
            // Se nombra el código del volcado más reciente que tenga uno. Si
            // ninguno lo tiene, no se dice nada: «0x00000000» no es un dato.
            var conCodigo = r.Pantallazos.LastOrDefault(p => !string.IsNullOrEmpty(p.Codigo));
            string detalle = conCodigo == null
                ? ""
                : $" Código de detención del más reciente: {conCodigo.Codigo} ({conCodigo.Nombre}).";
            r.Add(Severity.Warn, "Estabilidad", $"{dumps.Count} volcados de memoria disponibles." + detalle,
                "La tabla «Pantallazos» de la vista Datos dice qué significa el código y qué controladores de terceros estaban cargados. " +
                "Para llegar al módulo exacto hace falta análisis de pila (WinDbg); acá se lista lo accionable sin instalar nada.");
        }
        else if (kp41.Count > 0)
        {
            r.Add(Severity.Warn, "Estabilidad", "Hay reinicios inesperados pero ningún minidump.",
                "Eso refuerza la hipótesis de corte de energía por hardware: no hubo pantalla azul que volcar.");
        }

        if (complete && eventos.Count == 0 && dumps.Count == 0)
            r.Add(Severity.Ok, "Estabilidad", $"Sin eventos del catálogo de estabilidad en {EventDays} días.");

        // Un pantallazo azul real es la señal más directa para sugerir
        // sfc/DISM: a diferencia de un corte de energía (Kernel-Power 41
        // sin volcado), acá sí hubo oportunidad de que Windows detectara
        // corrupción de archivos al reiniciar.
        var bugchecks = eventos.Where(e => e.Id == 1001).ToList();
        if (bugchecks.Count > 0)
            r.Add(Severity.Bad, "Estabilidad",
                $"{bugchecks.Count} pantalla(s) azul(es) registrada(s) en {EventDays} días.",
                "Vale la pena comprobar la integridad de los archivos de sistema. Si sigue pasando después de eso, el minidump correspondiente suele nombrar al driver responsable.",
                "sfc");

        RunWheaScan(r, token);
    }

    /// <summary>
    /// Escaneo dedicado del proveedor Microsoft-Windows-WHEA-Logger. A diferencia
    /// del catálogo genérico de arriba (que solo mira IDs 18/19 dentro del log
    /// System), esto filtra por el nombre exacto del proveedor: cualquier evento
    /// que emita, no solo esos dos IDs, y con una ventana más corta porque el
    /// hardware que está fallando ahora es más relevante que hace un mes.
    /// </summary>
    public static int WheaDays = 15;

    private static void RunWheaScan(DiagnosticReport r, CancellationToken token)
    {
        var eventos = new List<EventRow>();
        long ms = (long)Math.Clamp(WheaDays, 1, 90) * 24 * 60 * 60 * 1000;
        string xpath = $"*[System[Provider[@Name='Microsoft-Windows-WHEA-Logger'] and TimeCreated[timediff(@SystemTime) <= {ms}]]]";

        try
        {
            var query = new EventLogQuery("System", PathType.LogName, xpath) { ReverseDirection = true };
            using var reader = new EventLogReader(query);

            int formateados = 0;

            for (EventRecord rec = reader.ReadEvent(); rec != null; rec = reader.ReadEvent())
            {
                using (rec)
                {
                    token.ThrowIfCancellationRequested();
                    if (eventos.Count >= WheaMaximo)
                    {
                        r.Add(Severity.Warn, "Hardware", $"Consulta WHEA truncada a {WheaMaximo:N0} eventos guardados.", "Los conteos son un límite inferior; revisa el registro completo de Windows.");
                        break;
                    }
                    string detalle = "";

                    if (formateados < DetalleMaximo)
                    {
                        try { detalle = rec.FormatDescription() ?? ""; }
                        catch { detalle = ""; }

                        int corte = detalle.IndexOf('\n');
                        if (corte > 0) detalle = detalle.Substring(0, corte);
                        formateados++;
                    }

                    eventos.Add(new EventRow
                    {
                        Fecha = (rec.TimeCreated ?? DateTime.MinValue).ToString("yyyy-MM-dd HH:mm:ss"),
                        Id = rec.Id,
                        Origen = rec.ProviderName ?? "",
                        Detalle = detalle.Trim()
                    });
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            AppLog.Write($"No se pudo consultar el proveedor WHEA-Logger: {ex.Message}", "WARN");
            r.Add(Severity.Warn, "Hardware", "No se pudo comprobar el registro WHEA.", "La falta de acceso no demuestra ausencia de errores de hardware.");
            return;
        }

        r.Whea = eventos;
        if (eventos.Count == 0) return;

        AppLog.Write($"WHEA-Logger: {eventos.Count} eventos en los últimos {WheaDays} días.", "WARN");

        if (eventos.Count >= 20)
            r.Add(Severity.Bad, "Hardware", $"{eventos.Count} errores WHEA en {WheaDays} días.",
                "El firmware está corrigiendo errores de hardware con frecuencia: CPU, RAM o el bus PCIe. Esto suele preceder a una pantalla azul. Revisa temperaturas, el asiento de la RAM y si hay una actualización de BIOS pendiente.");
        else
            r.Add(Severity.Warn, "Hardware", $"{eventos.Count} errores WHEA en {WheaDays} días.",
                "Errores de hardware corregidos automáticamente. Vale la pena vigilar si aumentan.");
    }

    /// <summary>Cuántos eventos se muestran en la tabla de detalle.</summary>
    private const int DetalleMaximo = 30;

    private record Evento(int Id, DateTime Cuando, string Origen, string Detalle);

    private static List<Evento> LeerEventos(CancellationToken token, out bool complete)
    {
        complete = false;
        var lista = new List<Evento>();
        string xpath = SystemEventCatalog.QueryXml(EventDays);

        try
        {
            var query = new EventLogQuery("System", PathType.LogName, xpath) { ReverseDirection = true };
            using var reader = new EventLogReader(query);

            // La consulta viene del más reciente al más antiguo, así que los
            // primeros son justo los que se muestran. Formatear la descripción
            // de cada evento es caro y solo hace falta en esos: con miles de
            // registros, hacerlo en todos multiplicaba el tiempo del módulo.
            int formateados = 0;
            complete = true;
            for (EventRecord rec = reader.ReadEvent(); rec != null; rec = reader.ReadEvent())
            {
                using (rec)
                {
                    token.ThrowIfCancellationRequested();
                    if (!SystemEventCatalog.IsKnownEvent(rec.Id, rec.ProviderName)) continue;
                    if (lista.Count >= EventosMaximo)
                    {
                        complete = false;
                        AppLog.Write("Consulta de estabilidad truncada a 50.000 eventos.", "WARN");
                        break;
                    }
                    string detalle = "";

                    if (formateados < DetalleMaximo)
                    {
                        try { detalle = rec.FormatDescription() ?? ""; }
                        catch { detalle = ""; }

                        int corte = detalle.IndexOf('\n');
                        if (corte > 0) detalle = detalle.Substring(0, corte);
                        formateados++;
                    }

                    lista.Add(new Evento(
                        rec.Id,
                        rec.TimeCreated ?? DateTime.MinValue,
                        rec.ProviderName ?? "",
                        detalle.Trim()));
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException)
        {
            complete = false;
            AppLog.Write("Acceso denegado al registro de eventos. Ejecuta como administrador.", "ERROR");
        }
        catch (Exception ex)
        {
            complete = false;
            AppLog.Write($"No se pudo consultar el registro de eventos: {ex.Message}", "WARN");
        }

        return lista;
    }
}
