using System;
using System.Collections.Generic;
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

                filas.Add(new ProcessRow
                {
                    Proceso = p.ProcessName,
                    Pid = p.Id,
                    CpuPct = Math.Round(Math.Clamp(delta / (segundos * nucleos) * 100, 0, 100), 1),
                    RamMb = Math.Round(p.WorkingSet64 / 1024d / 1024d, 1)
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

        double colaDisco = -1, tiempoDisco = -1;
        var disco = Wmi.Query("SELECT * FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk WHERE Name='_Total'").FirstOrDefault();
        if (disco != null)
        {
            colaDisco = Wmi.Num(disco, "CurrentDiskQueueLength");
            tiempoDisco = Wmi.Num(disco, "PercentDiskTime");
        }

        var resumen = new List<KeyValueRow>
        {
            new("CPU total", cpuTotal.HasValue ? $"{cpuTotal:0} %" : "n/d"),
            new("RAM en uso", totalKb > 0 && Wmi.TryNum(os, "FreePhysicalMemory", out _)
                ? $"{usadaPct} % ({AppEnv.FormatBytes((totalKb - libreKb) * 1024)} de {AppEnv.FormatBytes(totalKb * 1024)})" : "n/d"),
            new("Cola de disco", colaDisco < 0 ? "n/d" : colaDisco.ToString("0")),
            new("Tiempo de disco", tiempoDisco < 0 ? "n/d" : $"{tiempoDisco:0} %"),
            new("Procesos medidos", filas.Count.ToString()),
            new("Muestreo", $"{segundos:0.0} s sobre {nucleos} núcleos lógicos")
        };
        r.RendimientoResumen = resumen;

        foreach (var row in resumen) AppLog.Write($"{row.Clave,-18}: {row.Valor}");
        foreach (var p in r.TopCpu.Take(5))
            AppLog.Write($"  {p.Proceso,-28} {p.CpuPct,6} %   {p.RamMb,8} MB");

        if (colaDisco > 5)
            r.Add(Severity.Warn, "Disco", $"Cola de disco en {colaDisco:0}.",
                "El disco es el cuello de botella en este momento. Revisa qué proceso está escribiendo: antivirus, indexador o actualizaciones.");

        // El aviso de RAM alta y el de proceso dominante de CPU ahora los
        // evalúa DiagnosticEngine (Diagnostics/MemoryRules.cs, CpuRules.cs)
        // sobre los datos que este módulo deja en el reporte. Es el primer
        // caso real de la separación recolección/regla: antes vivían aquí
        // mezclados con la medición.
    }
}
