using System;
using System.Collections.Generic;
using System.Linq;
using SysDiag.Models;

namespace SysDiag.Core.Storage;

/// <summary>
/// Salud del almacenamiento leída de los contadores de fiabilidad del propio
/// disco (equivalente a SMART). Es el módulo que responde a la pregunta que
/// deja el registro de eventos cuando aparecen resets de controladora: ¿el
/// disco se está muriendo, o es el driver?
/// </summary>
public static class StorageModule
{
    private const string StorageNs = @"root\Microsoft\Windows\Storage";

    private static double? Number(WmiRow row, string property) => Wmi.TryNum(row, property, out double value) ? value : null;

    public static Severity AssessHealth(double? health, double? wear, double? temperature, double? errors)
    {
        var baseline = health switch { 0 => Severity.Ok, 1 => Severity.Warn, 2 => Severity.Bad, _ => Severity.Warn };
        var counters = wear >= 80 || errors > 0 ? Severity.Bad
            : wear >= 50 || temperature >= 70 ? Severity.Warn : Severity.Ok;
        return (Severity)Math.Max((int)baseline, (int)counters);
    }

    public static void Run(DiagnosticReport r)
    {
        AppLog.Write("Salud del almacenamiento", "STEP");

        var filas = new List<StorageRow>();

        // Los contadores de fiabilidad se asocian por DeviceId con el disco.
        var contadores = new Dictionary<string, (double? Horas, double? Desgaste, double? Temp, double? Errores)>();
        foreach (var c in Wmi.Query("SELECT * FROM MSFT_StorageReliabilityCounter", StorageNs))
        {
            string id = Wmi.Str(c, "DeviceId");
            if (string.IsNullOrEmpty(id)) continue;

            double? readErrors = Number(c, "ReadErrorsUncorrected"), writeErrors = Number(c, "WriteErrorsUncorrected");
            double knownErrors = (readErrors ?? 0) + (writeErrors ?? 0);
            contadores[id] = (Number(c, "PowerOnHours"), Number(c, "Wear"), Number(c, "Temperature"),
                knownErrors > 0 ? knownErrors : readErrors.HasValue && writeErrors.HasValue ? knownErrors : null);
        }

        if (contadores.Count == 0 && !AppEnv.IsAdmin)
            AppLog.Write("Los contadores de fiabilidad requieren administrador.", "WARN");

        foreach (var d in Wmi.Query("SELECT * FROM MSFT_PhysicalDisk", StorageNs))
        {
            string id = Wmi.Str(d, "DeviceId");
            string nombre = Wmi.Str(d, "FriendlyName");
            if (string.IsNullOrWhiteSpace(nombre)) continue;

            double? salud = Number(d, "HealthStatus");   // 0 sano, 1 con avisos, 2 en fallo
            double media = Wmi.Num(d, "MediaType");      // 3 HDD, 4 SSD, 5 SCM

            var fila = new StorageRow
            {
                Nombre = nombre,
                Tipo = media switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "desconocido" },
                Salud = salud switch { 0 => "Correcta", 1 => "Con avisos", 2 => "En fallo", _ => "desconocida" },
                Firmware = Wmi.Str(d, "FirmwareVersion"),
                Horas = "n/d",
                Desgaste = "n/d",
                Temperatura = "n/d",
                Errores = "n/d",
                Estado = AssessHealth(salud, null, null, null)
            };

            if (contadores.TryGetValue(id, out var c))
            {
                if (c.Horas.HasValue) fila.Horas = $"{c.Horas:N0} h ({c.Horas / 8760:0.0} años)";
                if (c.Desgaste.HasValue) fila.Desgaste = $"{c.Desgaste:0} %";
                if (c.Temp > 0) fila.Temperatura = $"{c.Temp:0} °C";
                if (c.Errores.HasValue) fila.Errores = $"{c.Errores:0}";
                fila.Estado = AssessHealth(salud, c.Desgaste, c.Temp, c.Errores);

                if (c.Errores > 0)
                    r.Add(Severity.Bad, "Almacenamiento",
                        $"{nombre}: {c.Errores:0} errores de lectura/escritura no corregidos.",
                        "El disco no logró recuperar datos por su cuenta. Respalda ahora y considera reemplazarlo: esto no se arregla con drivers.");

                if (c.Desgaste >= 80)
                    r.Add(Severity.Bad, "Almacenamiento", $"{nombre}: {c.Desgaste:0}% de desgaste de celdas.",
                        "El SSD está cerca del final de su vida útil de escritura. Planifica el reemplazo.");
                else if (c.Desgaste >= 50)
                    r.Add(Severity.Warn, "Almacenamiento", $"{nombre}: {c.Desgaste:0}% de desgaste.",
                        "Dentro de lo normal, pero vale la pena vigilarlo.");

                if (c.Temp >= 70)
                    r.Add(Severity.Warn, "Almacenamiento", $"{nombre} a {c.Temp:0} °C.",
                        "Por encima de 70 °C el SSD reduce velocidad para protegerse. Revisa la ventilación.");
            }

            if (salud == 1 || salud == 2)
                r.Add(salud == 2 ? Severity.Bad : Severity.Warn, "Almacenamiento",
                    $"{nombre} reporta salud «{fila.Salud}».",
                    "Windows detectó un problema en el subsistema de almacenamiento. Respalda antes de seguir investigando.");

            if (!salud.HasValue || (salud != 0 && salud != 1 && salud != 2))
                r.Add(Severity.Warn, "Almacenamiento", $"{nombre}: no se pudo confirmar el estado de salud.",
                    "Un contador ausente o desconocido no equivale a un disco sano.");
            filas.Add(fila);
            AppLog.Write($"{nombre,-38} {fila.Tipo,-5} salud {fila.Salud,-12} desgaste {fila.Desgaste,-6} {fila.Horas}");
        }

        // Aviso de fallo inminente del propio firmware del disco.
        foreach (var p in Wmi.Query("SELECT * FROM MSStorageDriver_FailurePredictStatus", @"root\WMI"))
        {
            bool predice = false;
            try { predice = Convert.ToBoolean(p["PredictFailure"]); } catch { }

            if (predice)
                r.Add(Severity.Bad, "Almacenamiento", "El disco anuncia fallo inminente (SMART).",
                    "Respalda todo de inmediato y reemplaza la unidad. Este aviso lo emite el propio firmware del disco.");
        }

        r.Almacenamiento = filas;

        if (filas.Count == 0)
            AppLog.Write("No se pudo leer información de almacenamiento.", "WARN");
        else if (filas.All(f => f.Estado == Severity.Ok) && !r.Hallazgos.Any(h => h.Area == "Almacenamiento"))
            r.Add(Severity.Ok, "Almacenamiento", "Los discos reportan salud correcta.");
    }
}
