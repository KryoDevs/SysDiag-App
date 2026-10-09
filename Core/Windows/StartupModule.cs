using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using SysDiag.Models;

namespace SysDiag.Core.Windows;

/// <summary>
/// Lo que compite por los recursos del equipo desde el arranque: programas de
/// inicio, servicios automáticos y software instalado. Todo es de solo lectura
/// y de solo diagnóstico: la app señala candidatos, la decisión de desinstalar
/// o desactivar es del usuario. Desactivar servicios a ciegas es una de las
/// formas más rápidas de dejar un Windows inestable.
/// </summary>
public static class StartupModule
{
    public static void Run(DiagnosticReport r)
    {
        AppLog.Write("Arranque, servicios y software", "STEP");

        Arranque(r);
        Servicios(r);
        Programas(r);
    }

    private static void Arranque(DiagnosticReport r)
    {
        var filas = new List<StartupRow>();

        foreach (var s in Wmi.Query("SELECT * FROM Win32_StartupCommand"))
        {
            filas.Add(new StartupRow
            {
                Nombre = Wmi.Str(s, "Name"),
                Origen = Wmi.Str(s, "Location"),
                Comando = Wmi.Str(s, "Command")
            });
        }

        r.Arranque = filas.OrderBy(x => x.Nombre).ToList();
        AppLog.Write($"Programas que arrancan con Windows: {filas.Count}");

        if (filas.Count >= 20)
            r.Add(Severity.Warn, "Arranque", $"{filas.Count} programas se inician con Windows.",
                "Cada uno suma tiempo de arranque y memoria en reposo. Revisa la lista en Datos ▸ Arranque y desactiva los que no reconozcas o no necesites.",
                "abrir-inicio");
        else if (filas.Count >= 12)
            r.Add(Severity.Warn, "Arranque", $"{filas.Count} programas al inicio.",
                "Cantidad manejable, pero hay margen para aligerar el arranque.");
    }

    private static void Servicios(DiagnosticReport r)
    {
        var filas = new List<ServiceRow>();

        foreach (var s in Wmi.Query(
            "SELECT Name, DisplayName, StartMode, State FROM Win32_Service WHERE StartMode='Auto'"))
        {
            filas.Add(new ServiceRow
            {
                Nombre = Wmi.Str(s, "DisplayName"),
                Descripcion = Wmi.Str(s, "Name"),
                Inicio = Wmi.Str(s, "StartMode"),
                Estado = Wmi.Str(s, "State")
            });
        }

        r.Servicios = filas.OrderBy(x => x.Nombre).ToList();
        AppLog.Write($"Servicios en inicio automático: {filas.Count}");

        // Varios motores antivirus activos a la vez es causa clásica de
        // lentitud silenciosa y de bloqueos entre ellos.
        string[] motores = { "avast", "avg", "mcafee", "norton", "kaspersky", "eset", "bitdefender", "malwarebytes", "panda" };
        var presentes = filas
            .Where(f => motores.Any(m => f.Nombre.ToLowerInvariant().Contains(m)
                                      || f.Descripcion.ToLowerInvariant().Contains(m)))
            .Select(f => motores.First(m => f.Nombre.ToLowerInvariant().Contains(m)
                                         || f.Descripcion.ToLowerInvariant().Contains(m)))
            .Distinct()
            .ToList();

        if (presentes.Count >= 2)
            r.Add(Severity.Bad, "Seguridad",
                $"Hay {presentes.Count} motores antivirus de terceros instalados: {string.Join(", ", presentes)}.",
                "Dos antivirus en tiempo real se escanean mutuamente y compiten por el disco. Deja solo uno (o solo Defender) y desinstala el resto.");
    }

    private static void Programas(DiagnosticReport r)
    {
        // Se lee del registro y no de Win32_Product: consultar esa clase WMI
        // dispara una reconfiguración de cada paquete MSI y puede tardar
        // minutos, además de escribir en el registro de eventos.
        var filas = new List<ProgramRow>();
        var vistos = new HashSet<string>();

        var raices = new (RegistryKey Hive, string Ruta)[]
        {
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.CurrentUser,  @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
        };

        foreach (var (hive, ruta) in raices)
        {
            try
            {
                using var key = hive.OpenSubKey(ruta);
                if (key == null) continue;

                foreach (string nombre in key.GetSubKeyNames())
                {
                    try
                    {
                        using var sub = key.OpenSubKey(nombre);
                        if (sub == null) continue;

                        string display = sub.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(display)) continue;
                        if (sub.GetValue("SystemComponent") is int sc && sc == 1) continue;
                        if (!vistos.Add(display)) continue;

                        string fecha = sub.GetValue("InstallDate") as string ?? "";
                        if (fecha.Length == 8)
                            fecha = $"{fecha.Substring(0, 4)}-{fecha.Substring(4, 2)}-{fecha.Substring(6, 2)}";

                        filas.Add(new ProgramRow
                        {
                            Nombre = display,
                            Editor = sub.GetValue("Publisher") as string ?? "",
                            Version = sub.GetValue("DisplayVersion") as string ?? "",
                            Instalado = fecha
                        });
                    }
                    catch { /* entrada de desinstalación malformada */ }
                }
            }
            catch (Exception ex)
            {
                AppLog.Write($"No se pudo leer {ruta}: {ex.Message}", "WARN");
            }
        }

        r.Programas = filas.OrderBy(x => x.Nombre).ToList();
        AppLog.Write($"Programas instalados: {filas.Count}");
    }
}
