using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using SysDiag.Models;

namespace SysDiag.Core.Performance;

public static class PerformanceModule
{
    public static int SampleSeconds = 5;

    public static void Run(DiagnosticReport r, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        int sampleSeconds = Math.Clamp(SampleSeconds, 2, 30);
        AppLog.Write($"Rendimiento (muestreo de {sampleSeconds} s)", "STEP");

        int nucleos = Environment.ProcessorCount;

        // La CPU se mide como diferencia real de tiempo de procesador entre dos
        // instantes, normalizada por núcleo. Leer el acumulado del proceso, como
        // hacen los scripts habituales, solo premia a los procesos más antiguos.
        var primera = new Dictionary<int, (TimeSpan Cpu, DateTime Start)>();
        var inaccesibles = new HashSet<int>();

        foreach (var p in Process.GetProcesses())
        {
            try { primera[p.Id] = (p.TotalProcessorTime, p.StartTime); }
            catch { inaccesibles.Add(p.Id); }
            finally { p.Dispose(); }
        }

        var reloj = Stopwatch.StartNew();
        if (token.WaitHandle.WaitOne(TimeSpan.FromSeconds(sampleSeconds))) token.ThrowIfCancellationRequested();
        reloj.Stop();
        double segundos = reloj.Elapsed.TotalSeconds;

        var filas = new List<ProcessRow>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                // Los procesos protegidos ya fallaron en la primera muestra:
                // reintentarlos solo genera excepciones, que cuestan órdenes de
                // magnitud más que una comprobación.
                if (inaccesibles.Contains(p.Id)) continue;
                if (!primera.TryGetValue(p.Id, out var before) || p.StartTime != before.Start) continue;
                token.ThrowIfCancellationRequested();
                double delta = (p.TotalProcessorTime - before.Cpu).TotalSeconds;
                if (delta < 0) continue;

                double ramMb = Math.Round(p.WorkingSet64 / 1024d / 1024d, 1);
                filas.Add(new ProcessRow
                {
                    Proceso = p.ProcessName,
                    Pid = p.Id,
                    CpuPct = Math.Round(Math.Clamp(delta / (segundos * nucleos) * 100, 0, 100), 1),
                    RamMb = ramMb,
                    // El porcentaje se calcula contra la memoria total instalada
                    // y no contra la usada: es la referencia que permite saber
                    // si un proceso «de 900 MB» es mucho o poco en este equipo.
                    RamPct = MemoriaInstaladaMb() > 0
                        ? Math.Round(Math.Clamp(ramMb / MemoriaInstaladaMb() * 100, 0, 100), 1)
                        : 0
                });
            }
            catch (OperationCanceledException) { throw; }
            catch { /* el proceso murió durante el muestreo */ }
            finally { p.Dispose(); }
        }

        r.TopCpu = filas.OrderByDescending(x => x.CpuPct).Take(10).ToList();
        r.TopRam = filas.OrderByDescending(x => x.RamMb).Take(10).ToList();

        // ---- Totales -------------------------------------------------------
        var os = Wmi.First("Win32_OperatingSystem");
        double totalKb = Wmi.Num(os, "TotalVisibleMemorySize");
        double libreKb = Wmi.Num(os, "FreePhysicalMemory");
        double usadaPct = totalKb > 0 ? Math.Round((totalKb - libreKb) / totalKb * 100, 1) : 0;

        double? cpuTotal = null;
        var perf = Wmi.Query("SELECT * FROM Win32_PerfFormattedData_PerfOS_Processor WHERE Name='_Total'").FirstOrDefault();
        if (Wmi.TryNum(perf, "PercentProcessorTime", out double measuredCpu)) cpuTotal = measuredCpu;

        var resumen = new List<KeyValueRow>
        {
            // El orden de estas dos primeras filas no es cosmético: las reglas
            // de diagnóstico y las tarjetas del resumen las buscan por nombre
            // (MemoryRules, RecommendationEngine), y «CPU total» tiene que ser
            // la primera fila que menciona CPU.
            new("CPU total", cpuTotal.HasValue ? $"{cpuTotal:0} %" : "n/d"),
            new("RAM en uso", totalKb > 0 && Wmi.TryNum(os, "FreePhysicalMemory", out _)
                ? $"{usadaPct} % ({AppEnv.FormatBytes((totalKb - libreKb) * 1024)} de {AppEnv.FormatBytes(totalKb * 1024)})" : "n/d")
        };

        // --- CPU por núcleo: un solo número esconde un núcleo saturado ---
        // Ocho núcleos al 12 % dan 12 % de promedio y se lee «holgado», pero
        // si uno está al 100 % hay un hilo esperando. Es la medición que
        // explica los tirones con CPU aparentemente baja.
        var nucleosInfo = Wmi.Query("SELECT * FROM Win32_PerfFormattedData_Counters_ProcessorInformation");
        var porNucleo = nucleosInfo
            // En equipos con más de un grupo de procesadores hay una instancia
            // «0,_Total» por grupo además del «_Total» global: si no se excluye,
            // el total entra dos veces en la lista de núcleos.
            .Where(x => !Wmi.Str(x, "Name").Contains("_Total", StringComparison.OrdinalIgnoreCase))
            .Select(x => Contador(x, "PercentProcessorTime"))
            .Where(v => v >= 0)
            .ToList();

        if (porNucleo.Count > 0)
        {
            double maximo = porNucleo.Max();
            int saturados = porNucleo.Count(v => v >= 90);
            resumen.Add(new KeyValueRow("Núcleo más cargado",
                $"{maximo:0} %" + (saturados > 0 ? $" · {saturados} por encima de 90 %" : "")));
        }

        // --- Memoria: disponible, compromiso y paginación ---
        var memoria = Wmi.First("Win32_PerfFormattedData_PerfOS_Memory");
        if (Wmi.TryNum(memoria, "AvailableMBytes", out double disponibleMb))
            resumen.Add(new KeyValueRow("RAM disponible", AppEnv.FormatBytes(disponibleMb * 1024 * 1024)));

        if (Wmi.TryNum(memoria, "CommitLimit", out double limite) && Wmi.TryNum(memoria, "CommittedBytes", out double compromiso) && limite > 0)
            resumen.Add(new KeyValueRow("Compromiso de memoria",
                $"{Math.Round(compromiso / limite * 100, 1)} % ({AppEnv.FormatBytes(compromiso)} de {AppEnv.FormatBytes(limite)})"));

        double paginas = Contador(memoria, "PagesPersec", "PagesPerSec");
        if (paginas >= 0)
            resumen.Add(new KeyValueRow("Páginas por segundo", $"{paginas:0}"));

        // --- Disco: cola, tiempo y caudal real ---
        double colaDisco = -1, tiempoDisco = -1;
        var disco = Wmi.Query("SELECT * FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk WHERE Name='_Total'").FirstOrDefault();
        if (disco != null)
        {
            colaDisco = Wmi.Num(disco, "CurrentDiskQueueLength");
            tiempoDisco = Wmi.Num(disco, "PercentDiskTime");

            double lectura = Contador(disco, "DiskReadBytesPersec", "DiskReadBytesPerSec");
            double escritura = Contador(disco, "DiskWriteBytesPersec", "DiskWriteBytesPerSec");
            if (lectura >= 0 && escritura >= 0)
                resumen.Add(new KeyValueRow("Disco",
                    $"{AppEnv.FormatBytes(lectura)}/s leyendo · {AppEnv.FormatBytes(escritura)}/s escribiendo"));
        }

        resumen.Add(new KeyValueRow("Cola de disco", colaDisco < 0 ? "n/d" : colaDisco.ToString("0", CultureInfo.CurrentCulture)));
        resumen.Add(new KeyValueRow("Tiempo de disco", tiempoDisco < 0 ? "n/d" : $"{tiempoDisco:0} %"));

        // --- Red: cuánto entra y sale en este instante ---
        // «La red está lenta» casi siempre es ancho de banda saturado por algo
        // que no se ve (una copia de seguridad, una actualización). Sin el
        // caudal, la latencia alta no tiene explicación a la vista.
        var interfaces = Wmi.Query("SELECT * FROM Win32_PerfFormattedData_Tcpip_NetworkInterface");
        double recibido = 0, enviado = 0, ancho = 0;
        bool hayRed = false;
        foreach (var nic in interfaces)
        {
            double r = Contador(nic, "BytesReceivedPersec", "BytesReceivedPerSec");
            double s = Contador(nic, "BytesSentPersec", "BytesSentPerSec");
            if (r < 0 && s < 0) continue;
            hayRed = true;
            recibido += Math.Max(r, 0);
            enviado += Math.Max(s, 0);
            ancho += Math.Max(Contador(nic, "CurrentBandwidth"), 0);
        }

        if (hayRed)
        {
            string uso = ancho > 0 ? $" · {Math.Round((recibido + enviado) * 8 / ancho * 100, 1)} % del enlace" : "";
            resumen.Add(new KeyValueRow("Red ahora",
                $"{AppEnv.FormatBytes(recibido)}/s ↓ · {AppEnv.FormatBytes(enviado)}/s ↑{uso}"));
        }

        // --- Sistema: procesos, subprocesos y tiempo encendido ---
        var sistema = Wmi.First("Win32_PerfFormattedData_PerfOS_System");
        double hilos = Contador(sistema, "Threads");
        if (hilos >= 0) resumen.Add(new KeyValueRow("Subprocesos", hilos.ToString("0", CultureInfo.CurrentCulture)));

        string encendido = TiempoEncendido(os);
        if (!string.IsNullOrEmpty(encendido)) resumen.Add(new KeyValueRow("Tiempo encendido", encendido));

        resumen.Add(new KeyValueRow("Procesos medidos", filas.Count.ToString(CultureInfo.CurrentCulture)));
        resumen.Add(new KeyValueRow("Muestreo", $"{segundos:0.0} s sobre {nucleos} núcleos lógicos"));

        r.RendimientoResumen = resumen;

        foreach (var row in resumen) AppLog.Write($"{row.Clave,-22}: {row.Valor}");
        foreach (var p in r.TopCpu.Take(5))
            AppLog.Write($"  {p.Proceso,-28} {p.CpuPct,6} %   {p.RamMb,8} MB");

        if (colaDisco > 5)
            r.Add(Severity.Warn, "Disco", $"Cola de disco en {colaDisco:0}.",
                "El disco es el cuello de botella en este momento. Revisa qué proceso está escribiendo: antivirus, indexador o actualizaciones.");

        if (porNucleo.Count > 1 && porNucleo.Max() >= 95 && (cpuTotal ?? 0) < 40)
            r.Add(Severity.Warn, "CPU", $"Un núcleo al {porNucleo.Max():0} % con el total en {(cpuTotal ?? 0):0} %.",
                "La carga está concentrada en un solo hilo: por más núcleos libres que haya, una tarea que no se puede paralelizar sigue esperando. Es el patrón típico de una aplicación que no reparte su trabajo.");

        if (paginas > 1000)
            r.Add(Severity.Warn, "Memoria", $"Windows está paginando a disco ({paginas:0} páginas por segundo).",
                "Con la memoria física llena, el equipo mueve datos al disco: aparecen tirones que no se explican ni por CPU ni por red. Cerrar lo que no se usa alivia de inmediato.");

        // El aviso de RAM alta y el de proceso dominante de CPU ahora los
        // evalúa DiagnosticEngine (Diagnostics/MemoryRules.cs, CpuRules.cs)
        // sobre los datos que este módulo deja en el reporte. Es el primer
        // caso real de la separación recolección/regla: antes vivían aquí
        // mezclados con la medición.
    }

    /// <summary>
    /// Los contadores de WMI no se ponen de acuerdo en cómo escribir «por
    /// segundo»: unas clases usan <c>Persec</c> y otras <c>PerSec</c>, porque
    /// la barra no es válida en un nombre de propiedad y cada clase la resolvió
    /// distinto. Se prueban las dos y, si ninguna existe, devuelve -1 para que
    /// el llamador lo muestre como «n/d» y no como un cero medido.
    /// </summary>
    private static double Contador(WmiRow fila, params string[] nombres)
    {
        foreach (string nombre in nombres)
            if (Wmi.TryNum(fila, nombre, out double valor)) return valor;
        return -1;
    }

    private static double _memoriaInstaladaMb = -1;
    private static double MemoriaInstaladaMb()
    {
        // Se consulta una sola vez por corrida: la memoria instalada no cambia
        // durante un diagnóstico y cada llamada a WMI cuesta.
        if (_memoriaInstaladaMb < 0)
        {
            var os = Wmi.First("Win32_OperatingSystem");
            _memoriaInstaladaMb = Wmi.Num(os, "TotalVisibleMemorySize") / 1024d;
        }
        return _memoriaInstaladaMb;
    }

    /// <summary>
    /// Tiempo desde el último arranque. Un equipo con semanas sin reiniciar
    /// acumula memoria fragmentada y controladores en estados raros; es el
    /// primer dato que explica una lentitud que ninguna otra medición explica.
    /// </summary>
    private static string TiempoEncendido(WmiRow os)
    {
        string crudo = Wmi.Str(os, "LastBootUpTime");
        if (string.IsNullOrWhiteSpace(crudo)) return "";

        try
        {
            var arranque = System.Management.ManagementDateTimeConverter.ToDateTime(crudo);
            var transcurrido = DateTime.Now - arranque;
            if (transcurrido.TotalSeconds < 0) return "";

            return transcurrido.TotalDays >= 1
                ? $"{(int)transcurrido.TotalDays} d {transcurrido.Hours} h"
                : $"{transcurrido.Hours} h {transcurrido.Minutes} min";
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException)
        {
            return "";
        }
    }
}
