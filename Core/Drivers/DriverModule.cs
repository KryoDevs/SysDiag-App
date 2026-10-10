using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SysDiag.Models;

namespace SysDiag.Core.Drivers;

/// <summary>
/// Auditoría de drivers de solo lectura: qué hay instalado y qué tan viejo es.
/// A propósito NO descarga ni instala nada — esa es exactamente la superficie
/// que explotan las herramientas tipo "driver updater": bajan de espejos no
/// verificados y a veces instalan la versión equivocada para el hardware real.
/// El único camino de actualización que ofrece la app son enlaces a canales
/// oficiales: Windows Update y la página de soporte del fabricante.
/// </summary>
public static class DriverModule
{
    // DeviceClass tal como lo reporta Win32_PnPSignedDriver. Estas son las
    // categorías que de verdad importan para estabilidad y rendimiento; el
    // resto (impresoras, HID genéricos, etc.) no aporta al diagnóstico.
    private static readonly Dictionary<string, string> Categorias = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HDC"] = "Almacenamiento",
        ["SCSIAdapter"] = "Almacenamiento",
        ["DiskDrive"] = "Almacenamiento",
        ["System"] = "Chipset / sistema",
        ["Net"] = "Red",
        ["Display"] = "Gráficos",
        ["MEDIA"] = "Audio",
        ["Bluetooth"] = "Bluetooth"
    };

    public static void Run(DiagnosticReport r)
    {
        AppLog.Write("Drivers instalados", "STEP");

        var filas = new List<DriverRow>();
        var hoy = DateTime.Now;

        foreach (var d in Wmi.Query("SELECT * FROM Win32_PnPSignedDriver WHERE DeviceClass IS NOT NULL"))
        {
            string clase = Wmi.Str(d, "DeviceClass");
            if (!Categorias.TryGetValue(clase, out string categoria)) continue;

            string nombre = Wmi.Str(d, "DeviceName");
            if (string.IsNullOrWhiteSpace(nombre)) continue;

            string fechaRaw = Wmi.Str(d, "DriverDate"); // formato WMI: AAAAMMDDHHMMSS.ffffff+zzz
            DateTime? fecha = ParseWmiDate(fechaRaw);
            int antiguedadAnios = fecha.HasValue ? (int)((hoy - fecha.Value).TotalDays / 365) : -1;

            var estado = Severity.Ok;
            // Chipset/almacenamiento/red envejecen peor que el resto: ahí es
            // donde vive tu sospecha del driver de NVMe.
            bool critica = categoria is "Almacenamiento" or "Chipset / sistema" or "Red";
            if (antiguedadAnios >= 3) estado = Severity.Bad;
            else if (antiguedadAnios >= (critica ? 1 : 2)) estado = Severity.Warn;

            filas.Add(new DriverRow
            {
                Dispositivo = nombre,
                Categoria = categoria,
                Fabricante = Wmi.Str(d, "DriverProviderName"),
                Version = Wmi.Str(d, "DriverVersion"),
                Fecha = fecha?.ToString("yyyy-MM-dd") ?? "desconocida",
                Antiguedad = antiguedadAnios >= 0 ? $"{antiguedadAnios} año(s)" : "desconocida",
                DeviceId = Wmi.Str(d, "DeviceID"),
                Estado = estado
            });
        }

        r.Drivers = filas.OrderByDescending(f => f.Estado).ThenBy(f => f.Categoria).ToList();

        foreach (var f in r.Drivers)
            AppLog.Write($"{f.Categoria,-18} {f.Dispositivo,-42} v{f.Version}  ({f.Antiguedad})");

        var criticos = r.Drivers.Where(f => f.Estado == Severity.Bad).ToList();
        var avisos = r.Drivers.Where(f => f.Estado == Severity.Warn).ToList();

        if (criticos.Count > 0)
        {
            var nombres = string.Join(", ", criticos.Select(f => f.Dispositivo).Distinct().Take(3));
            r.Add(Severity.Bad, "Drivers",
                $"{criticos.Count} driver(s) con 3 años o más sin actualizar: {nombres}.",
                "Los de almacenamiento y chipset son los que más pesan en la estabilidad. Busca si Microsoft publica una versión más nueva, firmada y validada para este hardware.",
                "buscar-drivers");
        }
        else if (avisos.Count > 0)
        {
            r.Add(Severity.Warn, "Drivers", $"{avisos.Count} driver(s) empezando a quedar atrás.",
                "Sin urgencia, pero vale la pena revisarlos en la próxima ventana de mantenimiento.");
        }
        else if (r.Drivers.Count > 0)
        {
            r.Add(Severity.Ok, "Drivers", "Los drivers auditados están razonablemente al día.");
        }
    }

    /// <summary>WMI entrega la fecha como AAAAMMDDHHMMSS.ffffff+zzz (formato WBEM datetime).</summary>
    private static DateTime? ParseWmiDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length < 8) return null;
        try
        {
            // Invariante a propósito: la fecha viene de WMI como dígitos ASCII,
            // no como texto localizable. Sin esto, el parse depende de la cultura
            // del hilo (el arranque fija es-CL hoy, y mañana no tiene por qué),
            // y un fallo aquí se traga en el catch de abajo como «fecha
            // ilegible» en vez de delatar el bug.
            int anio = int.Parse(raw.Substring(0, 4), CultureInfo.InvariantCulture);
            int mes = int.Parse(raw.Substring(4, 2), CultureInfo.InvariantCulture);
            int dia = int.Parse(raw.Substring(6, 2), CultureInfo.InvariantCulture);
            return new DateTime(anio, mes, dia);
        }
        catch
        {
            return null;
        }
    }
}
