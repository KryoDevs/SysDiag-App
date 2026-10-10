using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using SysDiag.Models;

namespace SysDiag.Core.Windows;

/// <summary>
/// Actualizaciones de programas mediante winget, el gestor de paquetes oficial
/// de Microsoft. Se usa winget y no descargas directas a propósito: los
/// paquetes vienen de repositorios validados por Microsoft, con el instalador
/// del propio fabricante y su hash verificado. Es la diferencia entre
/// actualizar por un canal auditado y bajar binarios de un espejo cualquiera.
/// </summary>
public static class UpdateModule
{
    public static bool Disponible(CancellationToken token = default) =>
        AppEnv.RunCommand("winget", new[] { "--version" }, 6000, token).Success;

    public static void Run(DiagnosticReport r, CancellationToken token = default)
    {
        AppLog.Write("Actualizaciones de programas (winget)", "STEP");

        if (!Disponible(token))
        {
            AppLog.Write("winget no está disponible en este equipo.", "WARN");
            r.Add(Severity.Warn, "Actualizaciones", "winget no está instalado.",
                "Se instala desde la Microsoft Store como «Instalador de aplicaciones». Sin él no se puede auditar qué programas tienen versión nueva.");
            return;
        }

        var result = AppEnv.RunCommand("winget", new[] { "upgrade", "--source", "winget", "--include-unknown",
            "--accept-source-agreements", "--disable-interactivity" }, 90000, token);

        // winget puede salir con código distinto de cero cuando simplemente no hay nada que actualizar.
        // Ese mensaje es el resultado real de la consulta, así que se interpreta antes que el código de salida.
        if (EsSinActualizaciones(result.StandardOutput + "\n" + result.StandardError))
        {
            r.Add(Severity.Ok, "Actualizaciones", "winget no ofrece actualizaciones aplicables en su catálogo comunitario.");
            return;
        }
        if (!result.Success)
        {
            r.Add(Severity.Warn, "Actualizaciones", "No se pudo consultar winget.", result.Describe("winget"));
            return;
        }
        string salida = result.StandardOutput;
        if (!TryParseTable(salida, out var filas))
        {
            r.Add(Severity.Warn, "Actualizaciones", "La salida de winget no pudo interpretarse.",
                "No se asumirá que los programas están al día. Revisa la consulta en una consola visible.");
            return;
        }
        r.Actualizaciones = filas;

        AppLog.Write($"Programas con actualización disponible: {filas.Count}");
        foreach (var f in filas.Take(20))
            AppLog.Write($"  {f.Nombre,-42} {f.Actual,-16} -> {f.Disponible}");

        if (filas.Count >= 10)
            r.Add(Severity.Warn, "Actualizaciones", $"{filas.Count} programas tienen versión más reciente.",
                "Actualizar cierra fallos conocidos y agujeros de seguridad. Puedes hacerlo desde el botón «Actualizar con winget» en la vista Datos.");
        else if (filas.Count > 0)
            r.Add(Severity.Ok, "Actualizaciones", $"{filas.Count} programa(s) con versión más reciente disponible.");
        else
            r.Add(Severity.Ok, "Actualizaciones", "Todos los programas gestionables están al día.");
    }

    /// <summary>
    /// winget imprime una tabla de ancho fijo cuyos encabezados cambian con el
    /// idioma, así que las columnas se ubican por la posición de la línea de
    /// guiones en vez de por el nombre del encabezado.
    /// </summary>
    public static bool TryParseTable(string salida, out List<UpdateRow> filas)
    {
        filas = new List<UpdateRow>();
        if (string.IsNullOrWhiteSpace(salida)) return false;
        salida = Regex.Replace(salida, @"\x1B\[[0-?]*[ -/]*[@-~]", "");

        var lineas = salida.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        int sep = lineas.FindIndex(l => l.StartsWith("---") || Regex.IsMatch(l, @"^-{5,}"));
        if (sep <= 0) return false;

        string encabezado = lineas[sep - 1];

        // Cada columna empieza donde empieza su palabra en el encabezado.
        var inicios = Regex.Matches(encabezado, @"\S+")
            .Select(m => m.Index)
            .ToList();

        if (inicios.Count < 4 || inicios.Count > 5) return false;
        var names = Regex.Matches(encabezado, @"\S+").Select(match => match.Value).ToList();
        if (!names[1].Equals("Id", StringComparison.OrdinalIgnoreCase)
            || !Regex.IsMatch(names[2], "^(Version|Versión)$", RegexOptions.IgnoreCase)
            || !Regex.IsMatch(names[3], "^(Available|Disponible)$", RegexOptions.IgnoreCase)) return false;
        bool invalidRows = false;

        foreach (string linea in lineas.Skip(sep + 1))
        {
            if (string.IsNullOrWhiteSpace(linea)) continue;
            if (linea.StartsWith(" ")) continue;


            string Campo(int i)
            {
                int ini = inicios[i];
                if (ini >= linea.Length) return "";
                int fin = i + 1 < inicios.Count ? Math.Min(inicios[i + 1], linea.Length) : linea.Length;
                return linea.Substring(ini, fin - ini).Trim();
            }

            string nombre = Campo(0);
            string id = Campo(1);
            if (Regex.IsMatch(linea, @"^\d+\s.*(?:upgrades?|actualiz|packages?)", RegexOptions.IgnoreCase)) continue;
            if (string.IsNullOrWhiteSpace(nombre) || !IsSafePackageId(id) || string.IsNullOrWhiteSpace(Campo(3)))
            { invalidRows = true; continue; }

            filas.Add(new UpdateRow
            {
                Nombre = nombre,
                Id = id,
                Actual = Campo(2),
                Disponible = Campo(3)
            });
        }

        return !invalidRows;
    }

    private static readonly Regex SinActualizacionesRegex = new(
        "No available upgrade found|No applicable upgrade found|No (?:se encontraron|hay) actualizaciones (?:disponibles|aplicables)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Reconoce el mensaje con el que winget indica que el catálogo no tiene nada aplicable.</summary>
    public static bool EsSinActualizaciones(string salida) =>
        !string.IsNullOrWhiteSpace(salida) && SinActualizacionesRegex.IsMatch(salida);

    public static bool IsSafePackageId(string id) => !string.IsNullOrEmpty(id) && id.Length <= 200
        && Regex.IsMatch(id, @"^[A-Za-z0-9][A-Za-z0-9._-]*$");

    /// <summary>
    /// Lanza winget en una consola visible. A propósito NO se ejecuta oculto:
    /// el usuario ve qué se está instalando y puede cortarlo. La app no
    /// descarga ni ejecuta nada por su cuenta.
    /// </summary>
    public static bool LanzarActualizacion(string id = null)
    {
        try
        {
            if (id != null && !IsSafePackageId(id)) throw new ArgumentException("El identificador del paquete no es válido.");
            var arguments = new List<string> { "upgrade", "--source", "winget", "--include-unknown" };
            if (id == null) arguments.Add("--all");
            else { arguments.Add("--id"); arguments.Add(id); arguments.Add("--exact"); }
            // No cmd.exe ni interpolación de texto externo en una orden de shell.
            var info = ProcessRunner.CreateStartInfo(AppEnv.SystemTool("winget"), arguments);
            info.UseShellExecute = true;
            using var process = Process.Start(info);
            if (process == null) return false;
            AppLog.Write("winget abierto en una consola visible, usando solo el origen comunitario oficial.", "OK");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write($"No se pudo lanzar winget: {ex.Message}", "ERROR");
            return false;
        }
    }
}
