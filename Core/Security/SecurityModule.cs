using System;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using SysDiag.Models;

namespace SysDiag.Core.Security;

/// <summary>
/// Centro de seguridad: Defender, Firewall, BitLocker, TPM y Secure Boot.
/// Todo de solo lectura — la app NUNCA deshabilita ni modifica ninguno de
/// estos componentes, ni siquiera con el sistema de reparación de un clic.
/// Cada dato viene de una fuente verificable; si algo no se puede leer, se
/// informa como tal en vez de inventar un estado.
/// </summary>
public static class SecurityModule
{
    public static void Run(DiagnosticReport r)
    {
        AppLog.Write("Seguridad de Windows", "STEP");

        var filas = new System.Collections.Generic.List<SecurityCheckRow>();

        filas.Add(LeerDefender(r));
        filas.Add(LeerFirewall(r));
        filas.Add(LeerBitLocker(r));
        filas.Add(LeerTpm(r));
        filas.Add(LeerSecureBoot(r));
        filas.Add(LeerUac(r));

        r.Seguridad = filas;
        foreach (var f in filas) AppLog.Write($"{f.Componente,-14}: {f.Estado}  ({f.Detalle})");
    }

    /// <summary>MSFT_MpComputerStatus: la misma fuente que consulta el propio Centro de seguridad de Windows.</summary>
    private static SecurityCheckRow LeerDefender(DiagnosticReport r)
    {
        var d = Wmi.Query("SELECT * FROM MSFT_MpComputerStatus", @"root\Microsoft\Windows\Defender")
                   .FirstOrDefault();

        if (d == null)
        {
            return new SecurityCheckRow
            {
                Componente = "Windows Defender",
                Estado = "No se pudo consultar",
                Detalle = "Puede haber otro antivirus gestionando la protección en tiempo real.",
                Nivel = Severity.Warn
            };
        }

        bool tiempoReal = LeerBool(d, "RealTimeProtectionEnabled");
        bool antivirusActivo = LeerBool(d, "AntivirusEnabled");
        string firmas = Wmi.Str(d, "AntivirusSignatureLastUpdated");

        var fila = new SecurityCheckRow { Componente = "Windows Defender" };

        if (!tiempoReal)
        {
            fila.Estado = "Protección en tiempo real desactivada";
            fila.Detalle = "Puede ser intencional si usas otro antivirus, o un problema real si no.";
            fila.Nivel = Severity.Bad;
            r.Add(Severity.Bad, "Seguridad", "La protección en tiempo real de Windows Defender está desactivada.",
                "Si no tienes otro antivirus activo, el equipo queda expuesto. Revísalo en Seguridad de Windows ▸ Protección antivirus y contra amenazas.");
        }
        else
        {
            fila.Estado = "Activo";
            fila.Detalle = antivirusActivo ? "Protección en tiempo real activa" : "Motor activo, verificar estado completo";
            fila.Nivel = Severity.Ok;
        }

        return fila;
    }

    // Nombre del perfil y estado de "ON": netsh devuelve el texto en el
    // idioma de la interfaz de Windows, no en el del sistema operativo en
    // general. Antes solo se reconocía inglés; en español "Perfil de
    // dominio" / "Estado" / "Activado" no calzaban con nada y el chequeo
    // devolvía "no se pudo consultar" siempre, en silencio.
    private static readonly (string Patron, string Nombre)[] PerfilesFirewall =
    {
        (@"(?i)domain\s*profile|perfil\s*(?:de\s*)?dominio", "Dominio"),
        (@"(?i)private\s*profile|perfil\s*privado", "Privado"),
        (@"(?i)public\s*profile|perfil\s*p[uú]blico", "Público"),
    };

    /// <summary>netsh, porque HNetCfg.FwPolicy2 exige interoperabilidad COM más pesada para un solo dato.</summary>
    private static SecurityCheckRow LeerFirewall(DiagnosticReport r)
    {
        string salida = AppEnv.RunConsole("netsh", "advfirewall show allprofiles state");

        var perfiles = new System.Collections.Generic.Dictionary<string, bool>();
        string perfilActual = null;

        foreach (string linea in (salida ?? "").Split('\n'))
        {
            foreach (var (patron, nombre) in PerfilesFirewall)
            {
                if (Regex.IsMatch(linea, patron)) { perfilActual = nombre; break; }
            }

            if (perfilActual == null || !Regex.IsMatch(linea, @"(?i)\bstate\b|\bestado\b")) continue;

            // "desactivado" contiene "activado" como subcadena, así que se
            // comprueba primero para no leerlo al revés.
            if (Regex.IsMatch(linea, @"(?i)desactivad|\boff\b"))
            {
                perfiles[perfilActual] = false;
                perfilActual = null;
            }
            else if (Regex.IsMatch(linea, @"(?i)activad|\bon\b"))
            {
                perfiles[perfilActual] = true;
                perfilActual = null;
            }
        }

        var fila = new SecurityCheckRow { Componente = "Firewall de Windows" };

        if (perfiles.Count == 0)
        {
            fila.Estado = "No se pudo consultar";
            fila.Detalle = "netsh no devolvió un resultado interpretable.";
            fila.Nivel = Severity.Warn;
            return fila;
        }

        var apagados = perfiles.Where(p => !p.Value).Select(p => p.Key).ToList();

        if (apagados.Count > 0)
        {
            fila.Estado = $"Desactivado en: {string.Join(", ", apagados)}";
            fila.Detalle = "El resto de los perfiles sí lo tiene activo.";
            fila.Nivel = Severity.Bad;
            r.Add(Severity.Bad, "Seguridad", $"El firewall está desactivado en el perfil {string.Join(", ", apagados)}.",
                "Actívalo en Firewall de Windows Defender, salvo que tengas un firewall de terceros gestionando ese perfil.");
        }
        else
        {
            fila.Estado = "Activo en los 3 perfiles";
            fila.Detalle = "Dominio, privado y público";
            fila.Nivel = Severity.Ok;
        }

        return fila;
    }

    private static SecurityCheckRow LeerBitLocker(DiagnosticReport r)
    {
        var v = Wmi.Query("SELECT * FROM Win32_EncryptableVolume WHERE DriveLetter='C:'",
                          @"root\cimv2\security\MicrosoftVolumeEncryption").FirstOrDefault();

        var fila = new SecurityCheckRow { Componente = "BitLocker (C:)" };

        if (v == null)
        {
            fila.Estado = "No disponible";
            fila.Detalle = "BitLocker no está presente en esta edición de Windows, o requiere administrador para consultarse.";
            fila.Nivel = Severity.Warn;
            return fila;
        }

        double estado = Wmi.Num(v, "ProtectionStatus"); // 0 desactivado, 1 activado, 2 desconocido

        fila.Estado = estado switch { 1 => "Activado", 0 => "Desactivado", _ => "Desconocido" };
        fila.Detalle = "Unidad del sistema";
        fila.Nivel = estado == 1 ? Severity.Ok : Severity.Warn;

        if (estado == 0)
            r.Add(Severity.Warn, "Seguridad", "BitLocker no está activado en la unidad del sistema.",
                "Si el equipo se pierde o lo roban, el disco se puede leer directamente conectándolo a otro equipo.");

        return fila;
    }

    private static SecurityCheckRow LeerTpm(DiagnosticReport r)
    {
        var t = Wmi.Query("SELECT * FROM Win32_Tpm", @"root\cimv2\security\MicrosoftTpm").FirstOrDefault();

        var fila = new SecurityCheckRow { Componente = "TPM" };

        if (t == null)
        {
            fila.Estado = "No disponible";
            fila.Detalle = "";
            fila.Nivel = Severity.Warn;
            return fila;
        }

        bool presente = LeerBool(t, "IsActivated_InitialValue");
        bool habilitado = LeerBool(t, "IsEnabled_InitialValue");
        string version = Wmi.Str(t, "SpecVersion");

        fila.Estado = (presente && habilitado) ? "Activo" : "Inactivo";
        fila.Detalle = string.IsNullOrEmpty(version) ? "" : $"Versión {version}";
        fila.Nivel = (presente && habilitado) ? Severity.Ok : Severity.Warn;

        return fila;
    }

    /// <summary>El estado de Secure Boot lo publica el firmware en esta clave, sin necesitar WMI.</summary>
    private static SecurityCheckRow LeerSecureBoot(DiagnosticReport r)
    {
        var fila = new SecurityCheckRow { Componente = "Secure Boot" };
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            if (k?.GetValue("UEFISecureBootEnabled") is int v)
            {
                fila.Estado = v == 1 ? "Activado" : "Desactivado";
                fila.Nivel = v == 1 ? Severity.Ok : Severity.Warn;
                if (v == 0)
                    r.Add(Severity.Warn, "Seguridad", "Secure Boot está desactivado.",
                        "Protege contra malware que se carga antes que Windows. Se activa desde la BIOS/UEFI.");
            }
            else
            {
                fila.Estado = "No disponible";
                fila.Detalle = "Equipo con BIOS heredada, no UEFI.";
                fila.Nivel = Severity.Warn;
            }
        }
        catch (Exception ex)
        {
            fila.Estado = "No se pudo leer";
            fila.Detalle = ex.Message;
            fila.Nivel = Severity.Warn;
        }
        return fila;
    }

    private static SecurityCheckRow LeerUac(DiagnosticReport r)
    {
        var fila = new SecurityCheckRow { Componente = "Control de cuentas de usuario" };
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
            int valor = k?.GetValue("EnableLUA") is int v ? v : 1;

            fila.Estado = valor == 1 ? "Activo" : "Desactivado";
            fila.Nivel = valor == 1 ? Severity.Ok : Severity.Bad;

            if (valor == 0)
                r.Add(Severity.Bad, "Seguridad", "El Control de cuentas de usuario (UAC) está desactivado.",
                    "Cualquier programa puede hacer cambios de administrador sin avisar. Actívalo en Cuentas de usuario ▸ Cambiar la configuración de Control de cuentas de usuario.");
        }
        catch (Exception ex)
        {
            fila.Estado = "No se pudo leer";
            fila.Detalle = ex.Message;
            fila.Nivel = Severity.Warn;
        }
        return fila;
    }

    private static bool LeerBool(WmiRow obj, string prop)
    {
        try { return Convert.ToBoolean(obj[prop]); }
        catch { return false; }
    }
}
