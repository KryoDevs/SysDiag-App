using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SysDiag.Models;

namespace SysDiag.Core.Hardware;

public static class ThermalModule
{
    public static void Run(DiagnosticReport r)
    {
        AppLog.Write("Térmicas y frecuencia", "STEP");

        var datos = new List<KeyValueRow>();

        // Windows no expone temperatura por núcleo sin un driver dedicado.
        // MSAcpi_ThermalZoneTemperature solo existe si el fabricante lo implementó.
        // Solo cuentan lecturas plausibles: una zona sin dato llega como 0 y antes se mostraba como «0 °C».
        double? maxTemp = null;
        foreach (var z in Wmi.Query("SELECT * FROM MSAcpi_ThermalZoneTemperature", @"root\WMI"))
        {
            double? c = ConvertirTemperaturaAcpi(Wmi.Num(z, "CurrentTemperature"));
            if (c.HasValue && (!maxTemp.HasValue || c.Value > maxTemp.Value)) maxTemp = c.Value;
        }

        if (maxTemp.HasValue)
        {
            double temperatura = maxTemp.Value;
            datos.Add(new KeyValueRow("Temperatura (ACPI)", $"{temperatura} °C"));

            if (temperatura >= 90)
                r.Add(Severity.Bad, "Térmicas", $"Zona térmica en {temperatura} °C.",
                    "A esta temperatura el equipo reduce frecuencia para protegerse. Toca limpieza de ventilación y cambio de pasta térmica.");
            else if (temperatura >= 80)
                r.Add(Severity.Warn, "Térmicas", $"Zona térmica en {temperatura} °C.",
                    "Margen justo bajo carga sostenida.");
        }
        else
        {
            datos.Add(new KeyValueRow("Temperatura (ACPI)", "no publicada por el firmware"));
            r.Add(Severity.Warn, "Térmicas", "El equipo no publica temperatura por WMI.",
                "Para lecturas por núcleo hace falta una herramienta con driver propio, como HWiNFO64 o LibreHardwareMonitor.");
        }

        var cpu = Wmi.First("Win32_Processor");
        double actual = Wmi.Num(cpu, "CurrentClockSpeed");
        double nominal = Wmi.Num(cpu, "MaxClockSpeed");
        datos.Add(new KeyValueRow("Frecuencia actual", $"{actual:0} MHz"));
        datos.Add(new KeyValueRow("Frecuencia nominal", $"{nominal:0} MHz"));

        // Por debajo del 100% de forma sostenida hay limitación: térmica,
        // de energía o de política de ahorro.
        var perf = Wmi.Query(
            "SELECT * FROM Win32_PerfFormattedData_Counters_ProcessorInformation WHERE Name='_Total'")
            .FirstOrDefault();

        if (perf != null)
        {
            double rendimiento = Wmi.Num(perf, "PercentProcessorPerformance");
            datos.Add(new KeyValueRow("Rendimiento del procesador", $"{rendimiento:0} %"));

            if (rendimiento > 0 && rendimiento < 70)
                r.Add(Severity.Warn, "Térmicas",
                    $"El procesador corre al {rendimiento:0}% de su frecuencia nominal.",
                    "Puede ser ahorro de energía, normal en reposo, o limitación térmica. Vuelve a medir bajo carga para distinguirlo.");
        }

        var bateria = Wmi.First("Win32_Battery");
        if (bateria != null)
        {
            double estado = Wmi.Num(bateria, "BatteryStatus");
            double carga = Wmi.Num(bateria, "EstimatedChargeRemaining");
            bool enBateria = Math.Abs(estado - 1) < 0.5;

            datos.Add(new KeyValueRow("Alimentación",
                enBateria ? $"Batería ({carga:0}%)" : "Conectado a la red eléctrica"));

            if (enBateria)
                r.Add(Severity.Warn, "Energía", "El equipo está funcionando con batería.",
                    "En batería el portátil limita CPU y GPU deliberadamente. Cualquier medición de rendimiento hecha así no es comparable.");
        }

        string plan = AppEnv.RunConsole("powercfg", "/getactivescheme");
        var m = Regex.Match(plan, @"\(([^)]+)\)");
        if (m.Success) datos.Add(new KeyValueRow("Plan de energía", m.Groups[1].Value));

        r.Termicas = datos;
        foreach (var d in datos) AppLog.Write($"{d.Clave,-28}: {d.Valor}");

        AuditBattery(r);
    }

    /// <summary>
    /// MSAcpi_ThermalZoneTemperature publica décimas de kelvin. Un valor no positivo, no finito o fuera de un
    /// rango físicamente posible significa que el firmware no entregó lectura: devuelve null en vez de un número.
    /// </summary>
    public static double? ConvertirTemperaturaAcpi(double decimasKelvin)
    {
        if (!double.IsFinite(decimasKelvin) || decimasKelvin <= 0) return null;
        double celsius = Math.Round(decimasKelvin / 10.0 - 273.15, 1);
        return celsius is >= -40 and <= 125 ? (double?)celsius : null;
    }

    /// <summary>
    /// Compara la capacidad de diseño de fábrica contra la capacidad máxima de
    /// carga actual, leídas de root\WMI (BatteryStaticData / BatteryFullChargedCapacity).
    /// Esa diferencia es el desgaste real de las celdas, algo que Win32_Battery
    /// no expone directamente.
    /// </summary>
    private static void AuditBattery(DiagnosticReport r)
    {
        var diseno = Wmi.Query("SELECT * FROM BatteryStaticData", @"root\WMI").FirstOrDefault();
        if (diseno == null) return; // equipo de escritorio o sin batería reportable

        var actual = Wmi.Query("SELECT * FROM BatteryFullChargedCapacity", @"root\WMI").FirstOrDefault();
        var estado = Wmi.Query("SELECT * FROM BatteryStatus", @"root\WMI").FirstOrDefault();

        double capDiseno = Wmi.Num(diseno, "DesignedCapacity");
        double capActual = actual != null ? Wmi.Num(actual, "FullChargedCapacity") : 0;
        if (capDiseno <= 0 || capActual <= 0) return;

        double desgaste = Math.Round(100 - capActual / capDiseno * 100, 1);
        if (desgaste < 0) desgaste = 0;

        var datos = new List<KeyValueRow>
        {
            new("Capacidad de diseño", $"{capDiseno / 1000.0:0.0} Wh"),
            new("Capacidad máxima actual", $"{capActual / 1000.0:0.0} Wh"),
            new("Desgaste de la batería", $"{desgaste} %")
        };

        if (estado != null)
        {
            double restante = Wmi.Num(estado, "RemainingCapacity");
            double cargaPct = capActual > 0 ? Math.Round(restante / capActual * 100, 1) : 0;
            datos.Add(new KeyValueRow("Carga actual", $"{cargaPct} %"));
        }

        r.Bateria = datos;
        foreach (var d in datos) AppLog.Write($"{d.Clave,-28}: {d.Valor}");

        if (desgaste >= 30)
            r.Add(Severity.Bad, "Batería", $"La batería tiene {desgaste}% de desgaste.",
                "Con ese nivel la autonomía real cae mucho respecto a la de fábrica. Si el equipo sigue en garantía, vale la pena reclamarla.");
        else if (desgaste >= 15)
            r.Add(Severity.Warn, "Batería", $"La batería tiene {desgaste}% de desgaste.",
                "Es un desgaste esperable con el tiempo. Evita dejarla al 100% enchufada de forma permanente si quieres frenarlo.");
    }
}
