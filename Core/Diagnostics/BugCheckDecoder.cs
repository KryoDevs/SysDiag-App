using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SysDiag.Models;

namespace SysDiag.Core.Diagnostics;

/// <summary>
/// Decodificador de pantallazos azules.
///
/// El módulo de estabilidad ya encontraba los volcados y se limitaba a
/// recomendar «abrilos con WinDbg». Ese consejo es correcto y casi nadie lo
/// sigue: instalar las herramientas de depuración para leer un número es una
/// barrera que convierte un dato disponible en un dato perdido.
///
/// Lo que se puede hacer sin DbgHelp, y es lo que hace esta clase:
///
///   1. Leer la cabecera del volcado, su lista de módulos y la versión de
///      Windows que lo generó. Eso es parseo de una estructura documentada y
///      estable, no depuración.
///   2. Leer el código de detención del informe de errores de Windows (evento
///      1001 de WER), que es de donde lo sacan también las herramientas de
///      terceros. El código no vive en el minidump: está en el registro de
///      eventos, y fingir lo contrario sería inventarlo.
///   3. Traducir el código a una frase. Con un catálogo local de los códigos
///      más frecuentes, y «sin descripción local» para el resto: un código
///      inventado es peor que un código sin traducir.
///
/// Lo que NO hace: señalar a un módulo como culpable. Enumera los módulos de
/// terceros cargados en el momento del fallo y los marca como candidatos, que
/// es lo máximo que se puede afirmar sin un análisis de pila.
/// </summary>
public static class BugCheckDecoder
{
    private const uint FirmaMinidump = 0x504D444D;   // 'MDMP' en little-endian
    private const ushort VersionMinidump = 0xA793;

    // --- Catálogo local ----------------------------------------------------
    //
    // Los códigos que explican la gran mayoría de los pantallazos reales. Cada
    // entrada dice qué falló en palabras llanas y hacia dónde mirar; ninguna
    // afirma una causa, porque el mismo código tiene causas distintas.

    private static readonly Dictionary<uint, (string Nombre, string Significado)> Catalogo = new()
    {
        [0x0000000A] = ("IRQL_NOT_LESS_OR_EQUAL",
            "Un controlador tocó memoria del sistema al que no debía. Casi siempre es un driver, casi nunca es la memoria."),
        [0x0000001A] = ("MEMORY_MANAGEMENT",
            "El gestor de memoria detectó una inconsistencia grave. Puede ser RAM defectuosa, un driver o corrupción de archivos de sistema."),
        [0x0000001E] = ("KMODE_EXCEPTION_NOT_HANDLED",
            "Un controlador en modo kernel provocó una excepción que nadie atendió. Apunta a driver."),
        [0x00000024] = ("NTFS_FILE_SYSTEM",
            "Fallo dentro del sistema de archivos NTFS. Revisa el disco antes que nada."),
        [0x0000003B] = ("SYSTEM_SERVICE_EXCEPTION",
            "Una llamada al sistema terminó en excepción. Driver o antivirus, en ese orden de frecuencia."),
        [0x00000050] = ("PAGE_FAULT_IN_NONPAGED_AREA",
            "Se pidió memoria que no estaba donde debía. Driver, o RAM empezando a fallar."),
        [0x0000007E] = ("SYSTEM_THREAD_EXCEPTION_NOT_HANDLED",
            "Un hilo del sistema murió por una excepción no atendida. Suele ser un driver."),
        [0x0000007F] = ("UNEXPECTED_KERNEL_MODE_TRAP",
            "El procesador encontró una condición imposible. Con frecuencia es sobrecalentamiento, overclock inestable o RAM."),
        [0x0000009F] = ("DRIVER_POWER_STATE_FAILURE",
            "Un controlador no completó la transición de energía. Es el pantallazo típico al salir de suspensión."),
        [0x000000D1] = ("DRIVER_IRQL_NOT_LESS_OR_EQUAL",
            "Un driver accedió a memoria paginada con una prioridad demasiado alta. Es driver."),
        [0x000000EF] = ("CRITICAL_PROCESS_DIED",
            "Un proceso del que depende Windows dejó de existir. Puede ser corrupción del sistema, un antivirus o el disco."),
        [0x00000109] = ("CRITICAL_STRUCTURE_CORRUPTION",
            "Windows detectó que una de sus estructuras internas fue modificada. Suele indicar un driver malo o un rootkit."),
        [0x00000116] = ("VIDEO_TDR_FAILURE",
            "La tarjeta de vídeo dejó de responder y Windows reinició su driver. Controlador de GPU, o la GPU misma empezando a fallar."),
        [0x00000119] = ("VIDEO_SCHEDULER_INTERNAL_ERROR",
            "El programador de vídeo detectó un estado inválido. Controlador de GPU."),
        [0x00000124] = ("WHEA_UNCORRECTABLE_ERROR",
            "El hardware reportó un error que no se pudo corregir. Es el código que más seguido significa fallo físico: CPU, RAM o placa."),
        [0x00000133] = ("DPC_WATCHDOG_VIOLATION",
            "Un controlador tardó demasiado en atender una interrupción. Casi siempre storage (NVMe/SATA) o red."),
        [0x00000139] = ("KERNEL_SECURITY_CHECK_FAILURE",
            "Windows detectó corrupción de una estructura protegida. Driver, o memoria inestable."),
        [0x0000014E] = ("WHEA_UNCORRECTABLE_ERROR (legado)",
            "Error de hardware no corregible, variante de firmware antiguo."),
        [0x00000154] = ("UNEXPECTED_STORE_EXCEPTION",
            "Fallo en el almacenamiento del sistema. Disco o su controlador."),
        [0xC000021A] = ("WINLOGON_FATAL_ERROR",
            "El subsistema de inicio de sesión falló de forma irreversible. Corrupción del sistema o un driver crítico."),
        [0xC0000005] = ("STATUS_ACCESS_VIOLATION / KERNEL_MODE",
            "Acceso a memoria no permitido desde modo kernel. Driver.")
    };

    /// <summary>
    /// Decodifica los volcados del informe. Devuelve una fila por volcado, en el
    /// mismo orden en que los recolectó Estabilidad.
    /// </summary>
    public static List<BugcheckRow> Decodificar(IReadOnlyList<string> archivos = null)
    {
        var filas = new List<BugcheckRow>();
        var codigos = LeerCodigosDelRegistro();

        IEnumerable<string> dmp = archivos ?? Enumerable.Empty<string>();
        if (archivos == null)
        {
            string carpeta = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Minidump");
            if (Directory.Exists(carpeta))
            {
                try
                {
                    dmp = new DirectoryInfo(carpeta).GetFiles("*.dmp")
                        .OrderByDescending(f => f.LastWriteTime)
                        .Take(15)
                        .Select(f => f.FullName)
                        .ToList();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    AppLog.Write($"No se pudo leer {carpeta}: {ex.Message}", "WARN");
                }
            }
        }

        foreach (string ruta in dmp)
        {
            var fila = new BugcheckRow
            {
                Archivo = Path.GetFileName(ruta),
                Fecha = File.GetLastWriteTime(ruta).ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)
            };

            var volcado = Leer(ruta);
            if (volcado == null)
            {
                fila.Nombre = "no se pudo leer";
                fila.Significado = "El archivo no es un minidump válido o está dañado. No se puede decir nada más sin WinDbg.";
                filas.Add(fila);
                continue;
            }

            fila.Sistema = volcado.Sistema;

            // El código se empareja por cercanía de fecha: hay un volcado por
            // pantallazo y un evento 1001 por pantallazo, y ninguno de los dos
            // guarda una referencia al otro. Con diferencia de minutos la
            // correspondencia es fiable; si no hay nada cerca, se dice que no
            // se encontró en vez de adjudicar el de otro fallo.
            var cercano = codigos
                .Where(c => Math.Abs((c.Cuando - File.GetLastWriteTime(ruta)).TotalMinutes) <= 10)
                .OrderBy(c => Math.Abs((c.Cuando - File.GetLastWriteTime(ruta)).TotalMinutes))
                .FirstOrDefault();

            if (cercano != null && cercano.Codigo != 0)
            {
                fila.Codigo = $"0x{cercano.Codigo:X8}";
                if (Catalogo.TryGetValue(cercano.Codigo, out var conocido))
                {
                    fila.Nombre = conocido.Nombre;
                    fila.Significado = conocido.Significado;
                }
                else
                {
                    fila.Nombre = "código sin descripción local";
                    fila.Significado = "SysDiag no traduce este código. Búscalo en la documentación de Microsoft con el número de arriba; " +
                                       "inventar una explicación sería peor que no darla.";
                }
                if (cercano.Parametros.Count > 0)
                    fila.Significado += " Parámetros: " + string.Join(", ", cercano.Parametros);
            }
            else
            {
                fila.Nombre = "sin código asociado";
                fila.Significado = "No hay un informe de error de Windows cercano a esta hora. Puede ser un volcado manual o generado al suspender.";
            }

            // Los módulos de terceros son candidatos, no culpables. Se listan
            // porque es la pista accionable que queda sin depurador, y se dice
            // explícitamente que son candidatos: afirmar cuál falló exigiría
            // analizar la pila, que es justo lo que esta clase no hace.
            var terceros = volcado.Modulos
                .Where(m => m.EndsWith(".sys", StringComparison.OrdinalIgnoreCase) && !EsDeMicrosoft(m))
                .Take(6)
                .ToList();
            fila.Sospechosos = terceros.Count == 0
                ? $"{volcado.Modulos.Count} módulos cargados, ninguno externo identificado"
                : string.Join(", ", terceros) + (volcado.Modulos.Count > terceros.Count ? " (y más)" : "");

            filas.Add(fila);
        }

        return filas;
    }

    // ---- Lectura del volcado ----------------------------------------------

    private sealed class Volcado
    {
        public string Sistema = "";
        public List<string> Modulos = new();
    }

    private static Volcado Leer(string ruta)
    {
        try
        {
            using var fs = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var br = new BinaryReader(fs, Encoding.Unicode, leaveOpen: true);

            if (fs.Length < 32) return null;
            if (br.ReadUInt32() != FirmaMinidump) return null;
            uint version = br.ReadUInt32();
            if ((version & 0xFFFF) != VersionMinidump) return null;

            uint numeroFlujos = br.ReadUInt32();
            uint rvaDirectorio = br.ReadUInt32();
            if (numeroFlujos == 0 || numeroFlujos > 4096) return null;
            if (rvaDirectorio + numeroFlujos * 12 > fs.Length) return null;

            var resultado = new Volcado();
            long directorio = rvaDirectorio;

            for (uint i = 0; i < numeroFlujos; i++)
            {
                fs.Seek(directorio + i * 12, SeekOrigin.Begin);
                uint tipo = br.ReadUInt32();
                uint tamaño = br.ReadUInt32();
                uint rva = br.ReadUInt32();
                if (rva + tamaño > fs.Length || tamaño == 0) continue;

                switch (tipo)
                {
                    case 7:  // SystemInfoStream
                        resultado.Sistema = LeerSistema(fs, br, rva, tamaño);
                        break;
                    case 4:  // ModuleListStream
                        resultado.Modulos = LeerModulos(fs, br, rva, tamaño);
                        break;
                }
            }
            return resultado;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or EndOfStreamException)
        {
            AppLog.Write($"No se pudo decodificar {Path.GetFileName(ruta)}: {ex.Message}", "WARN");
            return null;
        }
    }

    private static string LeerSistema(FileStream fs, BinaryReader br, uint rva, uint tamaño)
    {
        if (tamaño < 20) return "";
        fs.Seek(rva, SeekOrigin.Begin);
        ushort arquitectura = br.ReadUInt16();
        br.ReadUInt16(); // ProcessorLevel
        br.ReadUInt16(); // ProcessorRevision
        byte nucleos = br.ReadByte();
        byte producto = br.ReadByte();
        uint mayor = br.ReadUInt32();
        uint menor = br.ReadUInt32();
        uint build = br.ReadUInt32();

        string arco = arquitectura switch
        {
            0 => "x86", 9 => "x64", 5 => "ARM", 12 => "ARM64",
            _ => $"arquitectura {arquitectura}"
        };
        // La versión que reporta el volcado es la del kernel que lo escribió,
        // no la del equipo ahora: si Windows se actualizó después, el número
        // corresponde al momento del fallo. Por eso vale la pena mostrarlo.
        return $"Windows {mayor}.{menor} build {build} · {arco} · {nucleos} núcleo(s)";
    }

    private static List<string> LeerModulos(FileStream fs, BinaryReader br, uint rva, uint tamaño)
    {
        var nombres = new List<string>();
        if (tamaño < 4) return nombres;
        fs.Seek(rva, SeekOrigin.Begin);
        uint cantidad = br.ReadUInt32();
        // Cada entrada MINIDUMP_MODULE son 108 bytes en 64 bits.
        const int Entrada = 108;
        if (cantidad > 4096 || rva + 4 + cantidad * (long)Entrada > fs.Length) return nombres;

        for (uint i = 0; i < cantidad; i++)
        {
            long entrada = rva + 4 + i * (long)Entrada;
            fs.Seek(entrada + 20, SeekOrigin.Begin);
            uint rvaNombre = br.ReadUInt32();
            if (rvaNombre == 0 || rvaNombre >= fs.Length) continue;

            string nombre = LeerCadena(fs, br, rvaNombre);
            if (nombre.Length == 0) continue;
            // Solo el nombre de archivo: la ruta completa dice poco y ocupa
            // el ancho de la columna entera.
            nombres.Add(Path.GetFileName(nombre));
            if (nombres.Count >= 400) break;
        }
        return nombres;
    }

    /// <summary>MINIDUMP_STRING: largo en bytes, sin contar el terminador, seguido de UTF-16LE.</summary>
    private static string LeerCadena(FileStream fs, BinaryReader br, uint rva)
    {
        try
        {
            fs.Seek(rva, SeekOrigin.Begin);
            uint largo = br.ReadUInt32();
            if (largo == 0 || largo > 64 * 1024) return "";
            if (rva + 4 + largo > fs.Length) return "";
            int caracteres = (int)(largo / 2);
            var chars = new char[caracteres];
            for (int i = 0; i < caracteres; i++) chars[i] = br.ReadChar();
            return new string(chars);
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or ArgumentException)
        {
            return "";
        }
    }

    /// <summary>
    /// Heurística, y declarada como tal: los módulos de Microsoft más frecuentes
    /// en un volcado con los nombres que siempre tienen. Sirve para separar lo
    /// que viene con Windows de lo que alguien instaló; no afirma nada sobre
    /// ningún módulo en particular.
    /// </summary>
    private static bool EsDeMicrosoft(string modulo)
    {
        if (modulo.StartsWith("ntoskrnl", StringComparison.OrdinalIgnoreCase)) return true;
        if (modulo.StartsWith("hal", StringComparison.OrdinalIgnoreCase)) return true;
        if (modulo.StartsWith("ntfs", StringComparison.OrdinalIgnoreCase)) return true;
        if (modulo.StartsWith("win32k", StringComparison.OrdinalIgnoreCase)) return true;
        if (modulo.StartsWith("dxgkrnl", StringComparison.OrdinalIgnoreCase)) return true;
        if (modulo.StartsWith("clipsp", StringComparison.OrdinalIgnoreCase)) return true;
        if (modulo.StartsWith("Wdf0", StringComparison.OrdinalIgnoreCase)) return true;
        if (modulo.StartsWith("storport", StringComparison.OrdinalIgnoreCase)) return true;
        if (modulo.StartsWith("nvdimm", StringComparison.OrdinalIgnoreCase)) return true;
        if (modulo.StartsWith("CI.", StringComparison.OrdinalIgnoreCase)) return true;
        if (modulo.StartsWith("msrpc", StringComparison.OrdinalIgnoreCase)) return true;
        if (modulo.StartsWith("ksec", StringComparison.OrdinalIgnoreCase)) return true;
        if (modulo.StartsWith("tm.", StringComparison.OrdinalIgnoreCase)) return true;
        if (modulo.StartsWith("pshed", StringComparison.OrdinalIgnoreCase)) return true;
        if (modulo.StartsWith("werkernel", StringComparison.OrdinalIgnoreCase)) return true;
        if (modulo.StartsWith("mcupdate_", StringComparison.OrdinalIgnoreCase)) return true;
        if (modulo.StartsWith("cng", StringComparison.OrdinalIgnoreCase)) return true;
        if (modulo.StartsWith("fwpkclnt", StringComparison.OrdinalIgnoreCase)) return true;
        if (modulo.StartsWith("tcpip", StringComparison.OrdinalIgnoreCase)) return true;
        if (modulo.StartsWith("netio", StringComparison.OrdinalIgnoreCase)) return true;
        if (modulo.StartsWith("fvevol", StringComparison.OrdinalIgnoreCase)) return true;
        if (modulo.StartsWith("volmgr", StringComparison.OrdinalIgnoreCase)) return true;
        if (modulo.StartsWith("ndis", StringComparison.OrdinalIgnoreCase)) return true;
        if (modulo.StartsWith("usbehci", StringComparison.OrdinalIgnoreCase)) return true;
        if (modulo.StartsWith("USBXHCI", StringComparison.OrdinalIgnoreCase)) return true;
        if (modulo.StartsWith(" acpi", StringComparison.OrdinalIgnoreCase)) return true;
        return modulo.Equals("acpi.sys", StringComparison.OrdinalIgnoreCase);
    }

    // ---- Código de detención desde el registro de eventos ------------------

    private sealed class RegistroWer
    {
        public DateTime Cuando;
        public uint Codigo;
        public List<string> Parametros = new();
    }

    /// <summary>
    /// Lee los informes de error de Windows (evento 1001 de
    /// Microsoft-Windows-WER-SystemErrorReporting), que es donde Windows deja el
    /// código de detención y sus cuatro parámetros.
    ///
    /// El código no está en el minidump: quien diga lo contrario está leyendo
    /// otra cosa. Todas las herramientas que lo muestran lo sacan de acá.
    /// </summary>
    private static List<RegistroWer> LeerCodigosDelRegistro()
    {
        var lista = new List<RegistroWer>();
        try
        {
            string xpath = "*[System[Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting']" +
                           " and EventID=1001 and TimeCreated[timediff(@SystemTime) <= 2592000000]]]";
            var query = new EventLogQuery("System", PathType.LogName, xpath) { ReverseDirection = true };
            using var reader = new EventLogReader(query);

            for (EventRecord rec = reader.ReadEvent(); rec != null; rec = reader.ReadEvent())
            {
                using (rec)
                {
                    if (lista.Count >= 40) break;
                    string xml = "";
                    try { xml = rec.ToXml(); }
                    catch (EventLogException) { continue; }
                    if (xml.Length == 0) continue;

                    var hexadecimales = Hexadecimales.Matches(xml)
                        .Select(m => m.Value)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(5)
                        .ToList();
                    if (hexadecimales.Count == 0) continue;

                    // El primero es el código de detención; los siguientes, los
                    // parámetros. Es el orden en que WER los escribe.
                    if (!uint.TryParse(hexadecimales[0].TrimStart('0').Length == 0 ? "0" : hexadecimales[0].Substring(2),
                            NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint codigo)) continue;

                    lista.Add(new RegistroWer
                    {
                        Cuando = rec.TimeCreated?.ToLocalTime() ?? DateTime.MinValue,
                        Codigo = codigo,
                        Parametros = hexadecimales.Skip(1).Take(4).ToList()
                    });
                }
            }
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            // Sin el registro no se puede dar el código, pero sí el resto: no
            // se propaga, para que un fallo acá no tumbe el módulo entero.
            AppLog.Write($"No se pudo leer el informe de errores de Windows: {ex.Message}", "WARN");
        }
        return lista;
    }

    private static readonly Regex Hexadecimales = new(@"0x[0-9A-Fa-f]{8}", RegexOptions.Compiled);
}
