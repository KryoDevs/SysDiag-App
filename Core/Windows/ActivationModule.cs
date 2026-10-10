using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SysDiag.Models;

namespace SysDiag.Core.Windows;

/// <summary>
/// Estado y gestión de la licencia de Windows 10 y 11 por los canales
/// oficiales de Microsoft.
///
/// Este módulo **no** es un activador. No emula un servidor KMS, no inyecta
/// licencias digitales (HWID/KMS38), no falsifica claves ni modifica el
/// servicio de licencias para dar por válida una instalación sin derechos.
/// Hacer cualquiera de esas cosas es saltarse una medida de protección
/// tecnológica, incumple los términos de licencia de Microsoft y expone al
/// usuario a malware: la mayoría de los «activadores» que circulan son, de
/// hecho, la forma más común de distribuir troyanos.
///
/// Lo que sí hace, y es lo que una herramienta profesional hace:
///   · leer el estado real de la licencia (WMI <c>SoftwareLicensingProduct</c>);
///   · instalar y activar una clave legítima que el usuario ya tenga
///     (<c>slmgr.vbs /ipk</c> + <c>/ato</c>);
///   · apuntar a un host KMS corporativo propio de la organización
///     (<c>slmgr.vbs /skms</c>), que es el mecanismo de activación por
///     volumen previsto para empresas;
///   · abrir los caminos oficiales: Ajustes ▸ Activación, Microsoft Store,
///     activación telefónica y el solucionador de problemas de Microsoft.
///
/// Todo cambio que altera el estado del sistema se ejecuta con la utilidad
/// del propio Windows, con los argumentos en una lista (nunca en una línea de
/// comandos interpretada) y registrando en el log únicamente los últimos cinco
/// caracteres de la clave: una clave completa en un archivo de texto es una
/// filtración, y los logs de SysDiag se comparten cuando se pide soporte.
/// </summary>
public static class ActivationModule
{
    /// <summary>
    /// GUID de aplicación de Windows en el servicio de licencias por software.
    /// Sin filtrar por esto, la consulta devuelve también Office, SQL Server y
    /// todo lo demás que use el mismo servicio.
    /// </summary>
    private const string AplicacionWindows = "55c92734-d682-4d71-983e-d6ec3f16059f";

    private static string Cscript => Path.Combine(Environment.SystemDirectory, "cscript.exe");
    private static string SlmgrVbs => Path.Combine(Environment.SystemDirectory, "slmgr.vbs");

    /// <summary>Estado de la licencia, tal como la reporta Windows.</summary>
    public sealed class Estado
    {
        public bool Consultado { get; init; }
        public bool Activado { get; init; }
        public Severity Nivel { get; init; } = Severity.Warn;
        public string Titulo { get; init; } = "Sin datos";
        public string Explicacion { get; init; } = "";
        public string Edicion { get; init; } = "";
        public string ClaveParcial { get; init; } = "";
        public string Canal { get; init; } = "";
        public string Vencimiento { get; init; } = "";
        public string ServidorKms { get; init; } = "";
        public string IdInstalacion { get; init; } = "";
        public int? GraciaDias { get; init; }
        public List<KeyValueRow> Filas { get; init; } = new();
        public string Aviso { get; init; } = "";
        /// <summary>Cierto cuando la licencia es por volumen: es el único caso
        /// en el que tiene sentido configurar un host KMS.</summary>
        public bool EsVolumen { get; init; }
    }

    public sealed class Resultado
    {
        public bool Exito { get; init; }
        public string Mensaje { get; init; } = "";
        /// <summary>Salida completa de la utilidad, para el panel de detalle.</summary>
        public string Salida { get; init; } = "";

        public static Resultado Ok(string mensaje, string salida = "") =>
            new() { Exito = true, Mensaje = mensaje, Salida = salida };

        public static Resultado Falla(string mensaje, string salida = "") =>
            new() { Exito = false, Mensaje = mensaje, Salida = salida };
    }

    // ---- Consulta ---------------------------------------------------------

    /// <summary>
    /// Lee el estado de la licencia de Windows. No requiere permisos elevados:
    /// consultar es de lectura, y es justamente lo que la herramienta debe
    /// poder hacer siempre para orientar al usuario antes de pedirle nada.
    /// </summary>
    public static Estado Consultar()
    {
        if (!OperatingSystem.IsWindows())
            return new Estado { Aviso = "La activación de Windows solo se puede consultar sobre Windows." };

        var productos = Wmi.Query(
            "SELECT * FROM SoftwareLicensingProduct " +
            $"WHERE ApplicationID='{AplicacionWindows}' AND PartialProductKey IS NOT NULL");

        // Un equipo puede tener varias licencias de Windows instaladas (una
        // de la versión con la que venía y otra de una actualización de
        // edición). Se elige la activada y, si ninguna lo está, la que tenga
        // mejor estado.
        var licencias = productos
            .Where(p => !string.IsNullOrWhiteSpace(Wmi.Str(p, "PartialProductKey")))
            .Where(p => !Wmi.TryBool(p, "LicenseIsAddon", out bool addon) || !addon)
            .OrderBy(p => Wmi.Num(p, "LicenseStatus") == 1 ? 0 : 1)
            .ToList();

        if (licencias.Count == 0)
        {
            return new Estado
            {
                Consultado = true,
                Titulo = "Sin clave de producto instalada",
                Explicacion = "Windows no reporta ninguna licencia con clave parcial. " +
                              "Puede ser una instalación sin activar, una edición de evaluación " +
                              "o un equipo con el servicio de licencias detenido.",
                Aviso = "Si el equipo se activó con una licencia digital vinculada al hardware " +
                        "(lo habitual en equipos comprados con Windows preinstalado), Windows la " +
                        "reactiva solo al conectarse a internet después de un cambio grande de hardware.",
                Filas = new List<KeyValueRow> { new("Licencias encontradas", "0") }
            };
        }

        var licencia = licencias[0];
        int estado = (int)Wmi.Num(licencia, "LicenseStatus");
        var (titulo, nivel, explicacion) = MapearEstado(estado);

        double graciaMinutos = Wmi.Num(licencia, "GracePeriodRemaining");
        int? graciaDias = graciaMinutos > 0 ? (int)Math.Round(graciaMinutos / 1440.0) : null;

        string canal = Wmi.Str(licencia, "ProductKeyChannel");
        string nombre = Wmi.Str(licencia, "Name");
        string vencimiento = LeerVencimiento(licencia);

        string kms = Wmi.Str(licencia, "KeyManagementServiceMachine");
        if (string.IsNullOrWhiteSpace(kms)) kms = Wmi.Str(licencia, "DiscoveredKeyManagementServiceMachineName");

        var filas = new List<KeyValueRow>
        {
            new("Edición", string.IsNullOrWhiteSpace(nombre) ? "n/d" : nombre),
            new("Estado", $"{titulo} (código {estado})"),
            new("Clave parcial", Wmi.Str(licencia, "PartialProductKey")),
            new("Canal", TraducirCanal(canal)),
            new("Familia de licencia", Wmi.Str(licencia, "LicenseFamily")),
        };

        if (!string.IsNullOrWhiteSpace(vencimiento)) filas.Add(new("Vencimiento", vencimiento));
        if (graciaDias.HasValue) filas.Add(new("Período de gracia", $"{graciaDias} días restantes"));
        if (!string.IsNullOrWhiteSpace(kms)) filas.Add(new("Host KMS", kms));

        string idInstalacion = Wmi.Str(licencia, "OfflineInstallationId");
        if (!string.IsNullOrWhiteSpace(idInstalacion))
            filas.Add(new("Id. de instalación", idInstalacion));

        filas.Add(new("Licencias de Windows", licencias.Count.ToString(CultureInfo.InvariantCulture)));

        string aviso = nivel == Severity.Ok
            ? ""
            : "Para activar hace falta una licencia legítima: una clave retail o OEM propia, " +
              "una licencia digital vinculada a tu cuenta Microsoft, o un host KMS de tu " +
              "organización. SysDiag no vende licencias ni puede activar un equipo sin una.";

        return new Estado
        {
            Consultado = true,
            Activado = estado == 1,
            Nivel = nivel,
            Titulo = titulo,
            Explicacion = explicacion,
            Edicion = nombre,
            ClaveParcial = Wmi.Str(licencia, "PartialProductKey"),
            Canal = TraducirCanal(canal),
            Vencimiento = vencimiento,
            ServidorKms = kms,
            IdInstalacion = idInstalacion,
            GraciaDias = graciaDias,
            EsVolumen = canal.StartsWith("Volume", StringComparison.OrdinalIgnoreCase),
            Filas = filas,
            Aviso = aviso
        };
    }

    private static (string Titulo, Severity Nivel, string Explicacion) MapearEstado(int estado) => estado switch
    {
        1 => ("Activado", Severity.Ok,
              "La licencia de Windows está activada y validada contra los servidores de Microsoft."),
        0 => ("Sin licencia", Severity.Bad,
              "Windows no tiene una licencia instalada. Hay que cargar una clave propia o " +
              "comprar una licencia en la Microsoft Store."),
        2 => ("Período de gracia inicial", Severity.Warn,
              "Windows todavía no se activó y corre en el período de gracia inicial. " +
              "Al terminar, el sistema limita la personalización."),
        3 => ("Período de gracia adicional", Severity.Warn,
              "Se agotó el período inicial y Windows extendió la gracia. Conviene activar " +
              "ahora: el sistema avisa en pantalla y restringe funciones."),
        4 => ("Período de gracia no genuino", Severity.Bad,
              "Windows considera que la instalación no es genuina. Esto pasa cuando la clave " +
              "fue revocada o es de un canal que no corresponde a esta edición."),
        5 => ("Notificación", Severity.Bad,
              "La licencia entró en estado de notificación: Windows muestra avisos permanentes " +
              "y desactiva la personalización hasta que se active con una licencia válida."),
        6 => ("Período de gracia extendido", Severity.Warn,
              "Gracia extendida por activación por volumen. El equipo necesita contactar su " +
              "host KMS dentro del plazo indicado."),
        _ => ("Estado desconocido", Severity.Warn,
              "Windows reporta un código de licencia que esta versión no reconoce. " +
              "Abrí los ajustes de activación para ver el detalle completo.")
    };

    private static string TraducirCanal(string canal)
    {
        if (string.IsNullOrWhiteSpace(canal)) return "n/d";
        return canal.ToLowerInvariant() switch
        {
            "retail" => "Retail (clave de venta al público)",
            "oem:dm" => "OEM (fabricante del equipo)",
            "oem:slp" => "OEM SLP (fabricante, activación en BIOS)",
            "oem:nonslp" => "OEM (fabricante)",
            "volume:gvlk" => "Volumen · KMS (clave GVLK)",
            "volume:mak" => "Volumen · MAK",
            _ => canal
        };
    }

    /// <summary>EvaluationEndDate llega como fecha WMI (AAAAMMDDHHMMSS.ffffff+mmm).</summary>
    private static string LeerVencimiento(WmiRow licencia)
    {
        string crudo = Wmi.Str(licencia, "EvaluationEndDate");
        if (string.IsNullOrWhiteSpace(crudo)) return "";
        try
        {
            var fecha = System.Management.ManagementDateTimeConverter.ToDateTime(crudo);
            if (fecha.Year > 3000) return "";   // «9999» es el centinela de «sin vencimiento»
            return fecha.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException)
        {
            return "";
        }
    }

    // ---- Acciones ---------------------------------------------------------

    /// <summary>
    /// Instala una clave de producto propia y solicita la activación en línea.
    /// Los dos pasos van juntos porque instalar la clave sin activar deja al
    /// equipo exactamente igual de inactivo, y eso confunde.
    /// </summary>
    public static Resultado InstalarClave(string clave)
    {
        string normalizada = NormalizarClave(clave);
        string problema = ValidarClave(normalizada);
        if (problema != null) return Resultado.Falla(problema);

        if (!AppEnv.IsAdmin)
            return Resultado.Falla("Instalar una clave requiere permisos de administrador. " +
                                   "Reiniciá SysDiag como administrador y volvé a intentarlo.");

        AppLog.Write($"Activación: instalando clave terminada en …{Enmascarar(normalizada)}", "STEP");

        var instalada = Slmgr("/ipk", normalizada);
        if (!instalada.Success)
            return Resultado.Falla("Windows rechazó la clave. Revisá que corresponda a esta " +
                                   "edición (Home y Pro no comparten claves) y que esté completa.\n\n" +
                                   Texto(instalada), instalada.StandardOutput);

        var activacion = Slmgr("/ato");
        return activacion.Success
            ? Resultado.Ok("Clave instalada y Windows activado. Podés verificarlo en " +
                           "Ajustes ▸ Activación.", activacion.StandardOutput)
            : Resultado.Ok("La clave se instaló, pero la activación en línea no se completó. " +
                           "Casi siempre es falta de conexión o un servidor de Microsoft " +
                           "momentáneamente ocupado: probá de nuevo en unos minutos.\n\n" +
                           activacion.StandardOutput, activacion.StandardOutput);
    }

    /// <summary>Repite la activación en línea contra los servidores de Microsoft.</summary>
    public static Resultado ActivarEnLinea()
    {
        var res = Slmgr("/ato");
        return res.Success
            ? Resultado.Ok("Activación completada.", res.StandardOutput)
            : Resultado.Falla("Windows no pudo activarse ahora. " +
                              "Revisá la conexión y que la clave corresponda a esta edición.\n\n" +
                              res.StandardOutput, res.StandardOutput);
    }

    /// <summary>
    /// Apunta la activación por volumen a un host KMS propio. Es el camino
    /// previsto para empresas y laboratorios con su propio servidor; apuntarlo
    /// a un host ajeno activaría la instalación sin licencia, que es justo lo
    /// que este módulo no hace.
    /// </summary>
    public static Resultado ConfigurarKms(string servidor, string puerto = "1688")
    {
        if (string.IsNullOrWhiteSpace(servidor))
            return Resultado.Falla("Indicá el nombre o la dirección del host KMS.");

        servidor = servidor.Trim();
        puerto = string.IsNullOrWhiteSpace(puerto) ? "1688" : puerto.Trim();

        if (!Regex.IsMatch(servidor, @"^[A-Za-z0-9.\-_]{1,255}$"))
            return Resultado.Falla("Ese nombre de host tiene caracteres que no corresponden " +
                                   "a un nombre DNS ni a una dirección IP.");
        if (!int.TryParse(puerto, out int puertoNumero) || puertoNumero is < 1 or > 65535)
            return Resultado.Falla("El puerto tiene que ser un número entre 1 y 65535.");

        AppLog.Write($"Activación: configurando host KMS {servidor}:{puertoNumero}", "STEP");

        var configurado = Slmgr("/skms", $"{servidor}:{puertoNumero}");
        if (!configurado.Success)
            return Resultado.Falla("Windows no aceptó ese host KMS.\n\n" + configurado.StandardOutput,
                configurado.StandardOutput);

        var activacion = Slmgr("/ato");
        return activacion.Success
            ? Resultado.Ok($"Equipo activado contra {servidor}.", activacion.StandardOutput)
            : Resultado.Ok($"El host quedó configurado, pero la activación contra {servidor} " +
                           "no se completó. Revisá que el servidor KMS esté alcanzable desde " +
                           "esta red y que publique esta edición.\n\n" + activacion.StandardOutput,
                activacion.StandardOutput);
    }

    /// <summary>Salida completa del estado de licencia (<c>slmgr /dlv</c>), para el panel de detalle.</summary>
    public static string LicenciaDetallada()
    {
        var res = Slmgr("/dlv");
        return res.Success ? res.StandardOutput.Trim()
                           : "No se pudo leer el detalle de la licencia.\n\n" + res.StandardOutput;
    }

    /// <summary>Si la activación es permanente o vence (<c>slmgr /xpr</c>).</summary>
    public static string VencimientoDetallado()
    {
        var res = Slmgr("/xpr");
        return res.Success ? res.StandardOutput.Trim() : "";
    }

    // ---- Atajos a los canales oficiales -----------------------------------

    public static void AbrirConfiguracion() => Abrir("ms-settings:activation");

    public static void AbrirTienda() => Abrir("ms-windows-store://");

    public static void AbrirSolucionador() => Abrir("ms-settings:troubleshoot");

    private static void Abrir(string destino)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(destino)
            { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AppLog.Write($"No se pudo abrir {destino}: {ex.Message}", "WARN");
            Notificar?.Invoke("No se pudo abrir", $"Windows no permitió abrir {destino}.");
        }
    }

    /// <summary>
    /// Los módulos de Core no conocen la UI; la ventana registra acá su forma
    /// de avisar. Sin esto, un fallo al abrir un canal oficial se perdería en
    /// silencio y el usuario pensaría que la app no hizo nada.
    /// </summary>
    public static Action<string, string> Notificar { get; set; }

    // ---- Utilidades -------------------------------------------------------

    /// <summary>Quita guiones, espacios y normaliza a mayúsculas.</summary>
    public static string NormalizarClave(string clave)
        => Regex.Replace((clave ?? "").Trim().ToUpperInvariant(), @"[\s\-−]", "");

    /// <summary>
    /// Valida la forma, no la existencia: que la clave tenga 25 caracteres
    /// alfanuméricos. Si Windows la acepta es asunto de Windows; acá solo se
    /// evita enviar a la utilidad algo que seguro está mal escrito.
    /// </summary>
    public static string ValidarClave(string clave)
    {
        if (string.IsNullOrEmpty(clave)) return "Escribí la clave de producto.";
        if (!Regex.IsMatch(clave, @"^[0-9A-Z]{25}$"))
            return "La clave tiene que tener 25 caracteres entre letras y números, " +
                   "en cinco grupos de cinco. Los guiones son opcionales.";
        if (clave.Distinct().Count() <= 1)
            return "Esa clave no es válida.";
        return null;
    }

    /// <summary>Últimos cinco caracteres: suficiente para reconocer cuál se usó, insuficiente para filtrarla.</summary>
    public static string Enmascarar(string clave)
        => string.IsNullOrEmpty(clave) ? "" : clave.Length <= 5 ? "•••••" : "…" + clave[^5..];

    /// <summary>
    /// Ejecuta <c>slmgr.vbs</c> con cscript. Los argumentos van en una lista y
    /// nunca en una cadena interpretada por un shell: una clave o un host con
    /// caracteres raros no puede convertirse en un comando.
    /// </summary>
    private static ConsoleResult Slmgr(params string[] argumentos)
    {
        var lista = new List<string> { "//nologo", SlmgrVbs };
        lista.AddRange(argumentos);

        // slmgr escribe con la codificación de consola del sistema: en Windows
        // en español, leerla como UTF-8 rompe los acentos de la respuesta, y la
        // respuesta es justo lo que el usuario necesita leer.
        var info = ProcessRunner.CreateStartInfo(Cscript, lista);
        AppEnv.AplicarCodificacionConsola("slmgr.vbs", info);

        // slmgr abre una ventana de consola y tarda: 60 s y no 15.
        var resultado = ProcessRunner.Run(info, 60000);
        AppLog.Write($"slmgr {string.Join(" ", argumentos.Where(a => !EsSecreto(a)))} → " +
                     $"código {resultado.ExitCode}", resultado.Success ? "INFO" : "WARN");
        return resultado;
    }

    /// <summary>No registrar una clave completa ni en el log.</summary>
    private static bool EsSecreto(string argumento)
        => Regex.IsMatch(argumento ?? "", @"^[0-9A-Z]{25}$");

    /// <summary>cscript escribe en la consola: a veces la respuesta va en stdout y a veces en stderr.</summary>
    private static string Texto(ConsoleResult resultado)
    {
        string salida = string.IsNullOrWhiteSpace(resultado.StandardOutput)
            ? resultado.StandardError
            : resultado.StandardOutput;
        return salida.Trim();
    }
}
