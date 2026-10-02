using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.ServiceProcess;
using SysDiag.Core.Windows;
using SysDiag.Models;

namespace SysDiag.Core.Diagnostics;

/// <summary>
/// Reparaciones de un clic.
///
/// Solo entran aquí acciones que cumplen tres condiciones: son seguras, son
/// reversibles y de verdad corrigen el hallazgo. Un hallazgo sin acción no es
/// una carencia: un SSD con errores no corregidos, jitter del proveedor o un
/// driver que el fabricante no ha publicado no se arreglan con un botón, y
/// fingir que sí es peor que no ofrecer nada.
/// </summary>
public static class Remediation
{
    public record Accion(string Id, string Titulo, string Descripcion, bool RequiereAdmin);

    private static readonly Dictionary<string, Accion> Catalogo = new()
    {
        ["wlan-autoconfig"] = new("wlan-autoconfig", "Rehabilitar Wi-Fi automático",
            "Vuelve a activar la configuración automática de WLAN para que el equipo se reconecte solo a las redes guardadas.", true),

        ["flush-dns"] = new("flush-dns", "Vaciar caché DNS",
            "Fuerza a resolver de nuevo los nombres de dominio. Sin riesgo.", false),

        ["wifi-power"] = new("wifi-power", "Quitar ahorro de energía del Wi-Fi",
            "Pone la radio en máximo rendimiento cuando el equipo está enchufado. Es causa habitual de picos de ping.", true),

        ["limpiar-temp"] = new("limpiar-temp", "Liberar espacio",
            "Borra archivos temporales que Windows y las aplicaciones regeneran solos.", false),

        ["buscar-drivers"] = new("buscar-drivers", "Buscar driver en Windows Update",
            "Consulta si Microsoft publica una versión más nueva, firmada y validada para este hardware.", false),

        ["abrir-inicio"] = new("abrir-inicio", "Abrir programas de inicio",
            "Abre el Administrador de tareas en la pestaña Inicio para desactivar lo que no necesites.", false),

        ["plan-energia"] = new("plan-energia", "Cambiar plan de energía",
            "Pasa el equipo a alto rendimiento. Reversible desde «Restaurar estado».", true),

        ["sfc"] = new("sfc", "Reparar archivos de sistema",
            "Ejecuta sfc /scannow en una consola visible: comprueba y repara archivos de Windows dañados.", true),

        ["reset-wu"] = new("reset-wu", "Reiniciar componentes de Windows Update",
            "Detiene los servicios de Windows Update, renombra su caché local (no la borra) y los vuelve a iniciar. Windows la reconstruye sola. Es el arreglo estándar de Microsoft para fallos de búsqueda persistentes.", true),

        ["punto-restauracion"] = new("punto-restauracion", "Crear punto de restauración",
            "Guarda un punto al que Windows puede volver completo si algo sale mal más adelante. No cambia nada del sistema ahora mismo.", true)
    };

    /// <summary>
    /// Detiene wuauserv/BITS, renombra la caché local de Windows Update
    /// (no la borra: queda con sufijo .bak, por si algo saliera mal) y
    /// reinicia los servicios. Windows regenera las carpetas solo.
    /// </summary>
    private static string ReiniciarWindowsUpdate()
    {
        var point = RestorePointModule.Crear("Antes de reiniciar Windows Update");
        if (!point.Exito) throw new InvalidOperationException("No se modificó Windows Update porque no se pudo crear un punto de restauración. " + point.Mensaje);
        using var update = new ServiceController("wuauserv");
        using var bits = new ServiceController("BITS");
        var services = new[] { update, bits };
        var previous = services.ToDictionary(s => s.ServiceName, s => s.Status);
        if (previous.Values.Any(status => status is not ServiceControllerStatus.Running and not ServiceControllerStatus.Stopped))
            throw new InvalidOperationException("Los servicios están cambiando de estado. Espera y vuelve a intentar.");
        Exception failure = null;
        bool moved = false;
        try
        {
            foreach (var service in services.Where(s => previous[s.ServiceName] == ServiceControllerStatus.Running))
            {
                service.Stop();
                service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            }
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution");
            if (Directory.Exists(root))
            {
                Directory.Move(root, root + ".bak_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N"));
                moved = true;
            }
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            // Incluso si el renombrado/Stop falla, restaurar el estado original de AMBOS servicios.
            foreach (var service in services.Where(s => previous[s.ServiceName] == ServiceControllerStatus.Running))
            {
                try
                {
                    service.Refresh();
                    if (service.Status != ServiceControllerStatus.Running)
                    {
                        service.Start();
                        service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                    }
                }
                catch (Exception ex)
                {
                    failure ??= ex;
                    AppLog.Write($"No se pudo restaurar el servicio {service.ServiceName}: {ex.Message}", "ERROR");
                }
            }
        }
        if (failure != null) throw new InvalidOperationException("La reparación de Windows Update fue incompleta. Revisa el registro y el punto de restauración.", failure);
        return moved
            ? "Caché de Windows Update respaldada por renombrado. Se restauró el estado original de los servicios."
            : "No había caché que renombrar. Se restauró el estado original de los servicios.";
    }

    public static Accion Obtener(string id) =>
        string.IsNullOrEmpty(id) ? null : Catalogo.GetValueOrDefault(id);

    /// <summary>Ejecuta la reparación y devuelve el resultado en texto.</summary>
    public static string Ejecutar(string id, DiagnosticReport r)
    {
        var accion = Obtener(id);
        if (accion == null) return "Esa reparación no existe.";

        if (accion.RequiereAdmin && !AppEnv.IsAdmin)
            return "Esta reparación necesita que SysDiag se ejecute como administrador.";

        AppLog.Write($"Reparación: {accion.Titulo}", "STEP");

        try
        {
            switch (id)
            {
                case "flush-dns":
                    AppEnv.RunRequired("ipconfig", new[] { "/flushdns" });
                    return "Caché DNS vaciada.";

                case "wlan-autoconfig":
                    OptimizeModule.Run(r, new OptimizeModule.Options { FixWlanAutoconfig = true });
                    return "Se completó la comprobación de configuración automática WLAN. Revisa el registro para las interfaces disponibles.";

                case "wifi-power":
                    var opts = new OptimizeModule.Options
                    {
                        FlushDns = false,
                        FlushArp = false,
                        FixWlanAutoconfig = false,
                        WifiMaxPerformance = true
                    };
                    OptimizeModule.Run(r, opts);
                    return "Adaptador inalámbrico en máximo rendimiento. Se revierte desde «Restaurar estado».";

                case "plan-energia":
                    OptimizeModule.Run(r, new OptimizeModule.Options
                    {
                        FlushDns = false,
                        FlushArp = false,
                        FixWlanAutoconfig = false,
                        HighPerformancePlan = true
                    });
                    return "Plan de energía en alto rendimiento. Se revierte desde «Restaurar estado».";

                case "abrir-inicio":
                    Process.Start(new ProcessStartInfo(AppEnv.SystemTool("taskmgr"), "/7 /startup") { UseShellExecute = true });
                    return "Administrador de tareas abierto en la pestaña Inicio.";

                case "reset-wu":
                    return ReiniciarWindowsUpdate();

                case "punto-restauracion":
                    var manual = RestorePointModule.Crear("Punto manual desde SysDiag");
                    return manual.Mensaje;

                case "sfc":
                    // En consola visible y a propósito: la comprobación tarda
                    // varios minutos y conviene que el usuario vea el avance.
                    Process.Start(new ProcessStartInfo(AppEnv.SystemTool("sfc"), "/scannow")
                    { UseShellExecute = true, Verb = "runas" });
                    return "Comprobación de archivos de sistema lanzada en una consola aparte.";

                default:
                    return "Esa reparación se maneja desde su propio módulo.";
            }
        }
        catch (Exception ex)
        {
            AppLog.Write($"La reparación falló: {ex.Message}", "ERROR");
            throw;
        }
    }
}
