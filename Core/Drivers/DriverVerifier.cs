using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SysDiag.Models;

namespace SysDiag.Core.Drivers;

/// <summary>
/// Verificación de un archivo de driver bajado de cualquier origen.
///
/// La idea es invertir el problema: en vez de confiar en el sitio, se comprueba
/// el archivo. Un driver legítimo de Intel, NVIDIA, Realtek o ASUS viene
/// firmado por su fabricante con un certificado válido; uno alterado o
/// empaquetado con basura falla la verificación de firma, porque el hash del
/// contenido deja de coincidir con el que el certificado ampara.
///
/// Lo que esto NO puede detectar: que el driver sea auténtico pero no
/// corresponda al hardware de este equipo. Esa es la razón de fondo por la que
/// la app no descarga desde agregadores automáticos — la firma sería válida y
/// el driver, igualmente equivocado.
/// </summary>
public static class DriverVerifier
{
    public class Resultado
    {
        public string Archivo = "";
        public string Tamano = "";
        public string Sha256 = "";
        public bool Firmado;
        public string Editor = "";
        public string Emisor = "";
        public string ValidoHasta = "";
        public bool CadenaValida;
        public bool IntegridadValida;
        public string EstadoFirma = "";
        public string Antivirus = "";
        public Severity Nivel = Severity.Warn;
        public List<string> Notas = new();
        public bool AptoParaInstalar;
    }

    private static readonly string[] ExtensionesValidas =
        { ".inf", ".cab", ".exe", ".msi", ".sys", ".zip" };

    public static Resultado Verificar(string ruta, Action<string> progreso = null)
    {
        var r = new Resultado { Archivo = Path.GetFileName(ruta) };
        AppLog.Write($"Verificando {r.Archivo}", "STEP");

        if (!File.Exists(ruta))
        {
            r.Notas.Add("El archivo no existe.");
            r.Nivel = Severity.Bad;
            return r;
        }

        ruta = Path.GetFullPath(ruta);

        string ext = Path.GetExtension(ruta).ToLowerInvariant();
        if (!ExtensionesValidas.Contains(ext))
            r.Notas.Add($"Extensión «{ext}» poco habitual para un driver.");

        // ---- Hash: identifica el archivo de forma única -------------------
        progreso?.Invoke("Calculando hash...");
        try
        {
            using var stream = File.OpenRead(ruta);
            r.Tamano = AppEnv.FormatBytes(stream.Length);
            r.Sha256 = Convert.ToHexString(SHA256.HashData(stream));
            AppLog.Write($"SHA-256: {r.Sha256}");
        }
        catch (Exception ex)
        {
            r.Notas.Add($"No se pudo calcular el hash: {ex.Message}");
        }

        // ---- Firma digital ------------------------------------------------
        progreso?.Invoke("Comprobando la firma digital...");
        VerificarFirma(ruta, r);

        // ---- Antivirus ----------------------------------------------------
        progreso?.Invoke("Analizando con Microsoft Defender...");
        r.Antivirus = EscanearConDefender(ruta);

        // ---- Veredicto ----------------------------------------------------
        Concluir(r);

        AppLog.Write($"Veredicto: {r.EstadoFirma} · {r.Antivirus}",
            r.Nivel == Severity.Ok ? "OK" : r.Nivel == Severity.Warn ? "WARN" : "ERROR");

        return r;
    }

    private static void VerificarFirma(string ruta, Resultado r)
    {
        try
        {
            int status = AuthenticodeVerifier.VerifyFile(ruta);
            if (status != 0)
            {
                r.EstadoFirma = $"Firma Authenticode no válida (0x{status:X8})";
                r.Notas.Add("Windows no pudo validar la integridad, la confianza, la fecha o la revocación de la firma. " +
                            "Extraer un certificado no es prueba de integridad. Los INF/ZIP y drivers firmados solo mediante catálogo requieren el canal oficial de Windows Update.");
                return;
            }
            r.Firmado = r.CadenaValida = r.IntegridadValida = true;
            // Solo metadatos: la decisión de confianza YA la tomó WinVerifyTrust.
            try
            {
                using var certificate = X509Certificate.CreateFromSignedFile(ruta);
                using var cert = new X509Certificate2(certificate);
                r.Editor = NombreComun(cert.Subject);
                r.Emisor = NombreComun(cert.Issuer);
                r.ValidoHasta = cert.NotAfter.ToString("yyyy-MM-dd");
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                r.Editor = "validado por Windows (catálogo)";
            }
            r.EstadoFirma = $"Integridad Authenticode validada por Windows · {r.Editor}";
        }
        catch (Exception ex)
        {
            r.EstadoFirma = "No se pudo verificar Authenticode";
            r.Notas.Add(ex.Message);
        }
    }

    /// <summary>
    /// Análisis bajo demanda con el antivirus que ya trae Windows. No sustituye
    /// a la firma: detecta código malicioso conocido, no drivers equivocados.
    /// </summary>
    private static string EscanearConDefender(string ruta)
    {
        string mpcmd = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Windows Defender", "MpCmdRun.exe");

        if (!File.Exists(mpcmd))
        {
            AppLog.Write("Microsoft Defender no está disponible para el análisis.", "WARN");
            return "no analizado (Defender no disponible)";
        }

        var result = ProcessRunner.Run(ProcessRunner.CreateStartInfo(mpcmd,
            new[] { "-Scan", "-ScanType", "3", "-File", Path.GetFullPath(ruta) }), 180000);
        if (!result.Success)
        {
            AppLog.Write(result.Describe("Microsoft Defender"), "WARN");
            return result.TimedOut ? "no analizado (tiempo agotado)"
                : result.ExitCode == 2 ? "AMENAZA O ERROR DE ANÁLISIS (código 2)"
                : "no analizado o resultado no concluyente";
        }
        // Defender puede remediar una amenaza y devolver 0: no dar por apto un archivo retirado.
        return File.Exists(ruta) ? "sin amenazas pendientes" : "AMENAZA: el archivo fue retirado durante el análisis";
    }

    /// <summary>La falta de antivirus, hash o confianza es un resultado NO concluyente, nunca verde.</summary>
    public static bool VerificacionSatisfactoria(Resultado result) =>
        result.Firmado && result.CadenaValida && result.IntegridadValida
        && result.Sha256.Length == 64 && result.Antivirus == "sin amenazas pendientes";

    private static void Concluir(Resultado r)
    {
        r.AptoParaInstalar = false; // No ejecutar archivos locales sin validar hardware/paquete completo.
        if (r.Antivirus.Contains("AMENAZA", StringComparison.Ordinal))
        {
            r.Nivel = Severity.Bad;
            r.Notas.Insert(0, "El análisis detectó una amenaza o falló. No ejecutes el archivo.");
        }
        else if (!r.IntegridadValida || !r.Firmado)
            r.Nivel = Severity.Bad;
        else if (!VerificacionSatisfactoria(r))
        {
            r.Nivel = Severity.Warn;
            r.Notas.Insert(0, "La verificación está incompleta. No se confirmó un análisis antivirus satisfactorio y la identidad del archivo.");
        }
        else
        {
            r.Nivel = Severity.Ok;
            r.Notas.Insert(0, "Firma e integridad validadas por Windows y sin amenazas pendientes en el análisis. " +
                              "Esto NO demuestra compatibilidad con tu hardware ni ausencia de malware desconocido.");
        }
        r.Notas.Add("SysDiag no ejecuta instaladores de drivers descargados. Instala por Windows Update o sigue las instrucciones del fabricante para tu modelo exacto.");
    }

    /// <summary>Se conserva el contrato, pero se cierra la ejecución privilegiada de archivos locales.</summary>
    public static string Instalar(string ruta) =>
        "Por seguridad SysDiag no ejecuta instaladores locales de drivers. Usa Windows Update o el soporte oficial de tu modelo.";

    private static string NombreComun(string dn)
    {
        foreach (var parte in dn.Split(','))
        {
            var t = parte.Trim();
            if (t.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                return t.Substring(3).Trim('"');
        }
        return dn;
    }

    /// <summary>Página oficial de descarga según quién publica el driver.</summary>
    /// <summary>
    /// Página de soporte según quién publica el driver, o según el fabricante
    /// del equipo cuando se le pasa el texto completo de "Equipo" (que WMI
    /// entrega como "FABRICANTE MODELO", p. ej. "ASUSTeK COMPUTER INC. ASUS
    /// TUF Gaming F15..."). Son enlaces genéricos al portal de soporte, no a
    /// un modelo específico: no hay forma confiable de adivinar la URL exacta
    /// de cada modelo para cada fabricante sin arriesgarse a mandar a alguien
    /// a la página de un equipo que no es el suyo.
    /// </summary>
    public static string SitioOficial(string proveedor)
    {
        string p = (proveedor ?? "").ToLowerInvariant();

        if (p.Contains("intel")) return "https://www.intel.com/content/www/us/en/download-center/home.html";
        if (p.Contains("nvidia")) return "https://www.nvidia.com/Download/index.aspx";
        if (p.Contains("advanced micro") || p.Contains("amd")) return "https://www.amd.com/en/support";
        if (p.Contains("realtek")) return "https://www.realtek.com/downloads";
        if (p.Contains("mediatek")) return "https://www.mediatek.com/";
        if (p.Contains("microsoft") || p.Contains("surface")) return "https://support.microsoft.com/surface";

        // Fabricantes de equipo completo. "asustek" antes que "asus" a secas
        // porque WMI suele devolver la razón social completa.
        if (p.Contains("asustek") || p.Contains("asus")) return "https://www.asus.com/support/";
        if (p.Contains("dell")) return "https://www.dell.com/support/home/";
        if (p.Contains("hp") || p.Contains("hewlett")) return "https://support.hp.com/";
        if (p.Contains("lenovo")) return "https://pcsupport.lenovo.com/";
        if (p.Contains("acer")) return "https://www.acer.com/support";
        if (p.Contains("msi") || p.Contains("micro-star")) return "https://www.msi.com/support";
        if (p.Contains("samsung")) return "https://www.samsung.com/support/";
        if (p.Contains("gigabyte")) return "https://www.gigabyte.com/Support";
        if (p.Contains("toshiba") || p.Contains("dynabook")) return "https://dynabook.com/support/";
        if (p.Contains("system76")) return "https://support.system76.com/";
        if (p.Contains("lg electronics") || p.Contains("lg ")) return "https://www.lg.com/support";
        if (p.Contains("huawei")) return "https://consumer.huawei.com/en/support/";
        if (p.Contains("razer")) return "https://mysupport.razer.com/";
        if (p.Contains("framework")) return "https://frame.work/support";

        // Catálogo oficial de Microsoft: se puede buscar por ID de hardware y
        // todo lo publicado ahí está firmado y validado. Es el respaldo
        // razonable cuando no se reconoce el fabricante.
        return "https://catalog.update.microsoft.com/";
    }
}
