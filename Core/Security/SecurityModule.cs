using System.Runtime.InteropServices;

using System;
using System.Linq;
using System.IO;
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
        foreach (var f in filas)
        {
            AppLog.Write($"{f.Componente,-14}: {f.Estado}  ({f.Detalle})");
            if (f.Nivel == Severity.Warn && (f.Estado.StartsWith("No ") || f.Estado.StartsWith("Consulta incompleta")))
                r.Add(Severity.Warn, "Seguridad", $"{f.Componente}: {f.Estado}.",
                    "No se confirmó el estado de protección. Revisa permisos y Seguridad de Windows; no equivale a un resultado sano.");
        }
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

        if (!Wmi.TryBool(d, "RealTimeProtectionEnabled", out bool tiempoReal)
            || !Wmi.TryBool(d, "AntivirusEnabled", out bool antivirusActivo))
            return new SecurityCheckRow { Componente = "Windows Defender", Estado = "No se pudo consultar",
                Detalle = "Faltan propiedades verificables del motor de protección.", Nivel = Severity.Warn };

        uint? health = null;
        try { if (WscGetSecurityProviderHealth(4 /* ANTIVIRUS */, out uint value) == 0) health = value; }
        catch (Exception ex) { AppLog.Write($"Centro de seguridad no disponible: {ex.Message}", "WARN"); }
        var fila = AssessDefender(tiempoReal, antivirusActivo, health);
        if (fila.Nivel == Severity.Warn)
            r.Add(Severity.Warn, "Seguridad", "No se confirmó protección antivirus completa.", fila.Detalle);
        if (fila.Nivel == Severity.Bad)
            r.Add(Severity.Bad, "Seguridad", "Defender no informa protección activa y el Centro de seguridad reporta antivirus sin protección suficiente.",
                "Revisa Seguridad de Windows. No se asumirá que la protección de otro producto está activa.");
        if (tiempoReal && antivirusActivo && Wmi.TryNum(d, "AntivirusSignatureAge", out double age) && age >= 3)
        {
            fila.Nivel = fila.Nivel == Severity.Bad ? Severity.Bad : Severity.Warn;
            fila.Detalle += $" · Firmas con {age:0} días de antigüedad";
            r.Add(Severity.Warn, "Seguridad", "Las firmas de Windows Defender no están actualizadas.",
                "Actualízalas desde Seguridad de Windows antes de confiar en el análisis antivirus.");
        }
        return fila;
    }

    public static SecurityCheckRow AssessDefender(bool realTime, bool engine, uint? centerHealth)
    {
        var row = new SecurityCheckRow { Componente = "Windows Defender" };
        if (realTime && engine) { row.Estado = "Activo"; row.Detalle = "Motor y tiempo real activos"; row.Nivel = Severity.Ok; }
        else if (centerHealth == 0)
        { row.Estado = "Protección informada por el Centro de seguridad"; row.Detalle = "Defender no informa protección completa; Windows informa salud antivirus correcta (puede ser otro producto)."; row.Nivel = Severity.Ok; }
        else
        { row.Estado = "Protección incompleta o no confirmada"; row.Detalle = "Consulta Seguridad de Windows; no se infiere protección de terceros."; row.Nivel = centerHealth == 2 ? Severity.Bad : Severity.Warn; }
        return row;
    }

    [DllImport("wscapi.dll", ExactSpelling = true)]
    private static extern int WscGetSecurityProviderHealth(uint providers, out uint health);

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
        var row = ParseFirewall(AppEnv.RunConsole("netsh", "advfirewall show allprofiles state"));
        if (row.Nivel == Severity.Bad)
            r.Add(Severity.Bad, "Seguridad", $"Firewall: {row.Estado}.",
                "Actívalo en Seguridad de Windows salvo que un firewall de terceros gestione esos perfiles.");
        return row;
    }

    public static SecurityCheckRow ParseFirewall(string salida)
    {
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
            fila.Detalle = perfiles.Count == 3 ? "Se consultaron los 3 perfiles." : $"Consulta parcial: solo {perfiles.Count}/3 perfiles.";
            fila.Nivel = Severity.Bad;

        }
        else if (perfiles.Count != 3)
        {
            fila.Estado = $"Consulta incompleta ({perfiles.Count}/3 perfiles)";
            fila.Detalle = "No se puede confirmar protección en los perfiles no consultados.";
            fila.Nivel = Severity.Warn;
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
        string drive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))?.TrimEnd('\\') ?? "";
        var fila = new SecurityCheckRow { Componente = $"BitLocker ({drive})" };
        var v = Regex.IsMatch(drive, @"^[A-Za-z]:$")
            ? Wmi.Query($"SELECT * FROM Win32_EncryptableVolume WHERE DriveLetter='{drive}'",
                @"root\cimv2\security\MicrosoftVolumeEncryption").FirstOrDefault() : null;

        if (v == null)
        {
            fila.Estado = "No disponible";
            fila.Detalle = "BitLocker no está presente en esta edición de Windows, o requiere administrador para consultarse.";
            fila.Nivel = Severity.Warn;
            return fila;
        }

        double estado = Wmi.TryNum(v, "ProtectionStatus", out double protection) ? protection : 2;

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

        if (!Wmi.TryBool(t, "IsActivated_InitialValue", out bool presente)
            || !Wmi.TryBool(t, "IsEnabled_InitialValue", out bool habilitado))
        {
            fila.Estado = "No se pudo consultar";
            fila.Nivel = Severity.Warn;
            return fila;
        }
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
            if (k?.GetValue("EnableLUA") is not int valor)
            {
                fila.Estado = "No se pudo consultar";
                fila.Detalle = "No se leyó el valor EnableLUA; no se asumirá que UAC está activo.";
                fila.Nivel = Severity.Warn;
                return fila;
            }

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

}
