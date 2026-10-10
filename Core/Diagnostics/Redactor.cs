using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using SysDiag.Models;

namespace SysDiag.Core.Diagnostics;

/// <summary>
/// Redacción de un informe para poder compartirlo.
///
/// El README venía aconsejando editar a mano el informe antes de mandarlo a un
/// foro o a soporte. Nadie lo hace: el archivo tiene cientos de líneas y lo que
/// hay que quitar son cuatro datos repartidos entre tablas distintas. Así que
/// el diagnóstico se compartía completo, con el nombre del equipo, el del
/// usuario, la ruta del perfil y el nombre de la red de la casa.
///
/// Ninguno de esos datos hace falta para diagnosticar un problema de drivers,
/// de disco o de memoria. Y son exactamente los datos que conviene no publicar.
///
/// La redacción se aplica sobre una COPIA serializada: el informe que guarda el
/// usuario en su carpeta sigue intacto, porque es suyo y lo necesita legible.
/// </summary>
public sealed class Redactor
{
    /// <summary>Qué se quitó, para poder decirlo en el propio informe.</summary>
    public List<string> Conceptos { get; } = new();

    private readonly Dictionary<string, string> _mapa = new(StringComparer.Ordinal);

    // Una MAC identifica un equipo en una red concreta y, con bases de datos
    // públicas, suele bastar para ubicarlo. Un BSSID identifica un router y
    // por tanto una dirección.
    private static readonly Regex Mac = new(@"\b(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}\b", RegexOptions.Compiled);

    // Rutas de perfil: C:\Users\pepe, C:\Users\pepe\Desktop, /home/pepe.
    private static readonly Regex RutaPerfil = new(
        @"(?:[A-Za-z]:\\+Users\\+|[A-Za-z]:\\+Documents and Settings\\+|/home/+)([^\\\r\n""']+)",
        RegexOptions.Compiled);

    private void Agregar(string concepto, string original, string reemplazo)
    {
        // Menos de tres caracteres y el riesgo supera al beneficio: un usuario
        // llamado «Ana» haría desaparecer la «Ana» de «Analytics» en el nombre
        // de un driver, y un informe con datos inventados es peor que uno sin
        // redactar. Esos casos los cubre el patrón de la ruta del perfil, que
        // sí tiene contexto.
        if (string.IsNullOrWhiteSpace(original) || original.Trim().Length < 3) return;
        if (string.Equals(original, reemplazo, StringComparison.Ordinal)) return;
        _mapa[original.Trim()] = reemplazo;
        // En JSON la barra invertida va escapada: sin esta variante la ruta del
        // perfil quedaría sin redactar justo en el campo donde aparece.
        _mapa[original.Trim().Replace("\\", "\\\\")] = reemplazo.Replace("\\", "\\\\");
        if (!Conceptos.Contains(concepto)) Conceptos.Add(concepto);
    }

    /// <summary>
    /// Expresión para una sustitución. Con límite de palabra solo cuando el
    /// valor empieza y termina en carácter de palabra: en «DESKTOP-4F2A» el
    /// límite evita tocar «DESKTOP-4F2AB», y en una ruta no se puede pedir
    /// (empieza por «C:» y el límite no aplicaría igual).
    /// </summary>
    private static Regex Patron(string literal)
    {
        string escapado = Regex.Escape(literal);
        bool conLimite = char.IsLetterOrDigit(literal[0]) && char.IsLetterOrDigit(literal[literal.Length - 1]);
        return new Regex(conLimite ? $@"\b{escapado}\b" : escapado,
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }

    private Redactor Construir(DiagnosticReport r)
    {
        string equipo = (r.Equipo ?? "").Trim();
        if (equipo.Length > 0) Agregar("nombre del equipo", equipo, "EQUIPO");

        string usuario = "";
        try { usuario = Environment.UserName ?? ""; } catch { /* un entorno sin usuario no impide redactar */ }
        if (usuario.Length > 0) Agregar("nombre del usuario", usuario, "USUARIO");

        foreach (var fila in r.Sistema ?? new List<KeyValueRow>())
        {
            if (fila?.Clave == null) continue;
            bool esSerie = fila.Clave.Contains("serie", StringComparison.OrdinalIgnoreCase)
                        || fila.Clave.Contains("serial", StringComparison.OrdinalIgnoreCase);
            if (esSerie && !string.IsNullOrWhiteSpace(fila.Valor))
                Agregar("números de serie", fila.Valor, "SERIE-OCULTA");
        }

        // Los SSID se numeran: si dos redes distintas se redactaran con el mismo
        // texto, una tabla de redes cercanas quedaría ilegible y el problema de
        // solapamiento de canales —que es justo lo que se suele compartir— no
        // se podría ver.
        int indice = 0;
        foreach (var fila in r.WiFi ?? new List<KeyValueRow>())
        {
            if (fila?.Clave == null || !fila.Clave.Contains("SSID", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrWhiteSpace(fila.Valor)) continue;
            indice++;
            Agregar("nombres de red Wi-Fi", fila.Valor, $"RED-{indice}");
        }
        foreach (var red in (r.RedesCercanas ?? new List<WifiNetworkRow>()).Take(40))
        {
            if (string.IsNullOrWhiteSpace(red?.Ssid)) continue;
            if (_mapa.ContainsKey(red.Ssid)) continue;
            indice++;
            Agregar("nombres de redes cercanas", red.Ssid, $"RED-{indice}");
        }

        return this;
    }

    /// <summary>
    /// Devuelve una copia redactada. El informe original no se modifica: es del
    /// usuario y lo necesita legible.
    /// </summary>
    public static DiagnosticReport Aplicar(DiagnosticReport original)
    {
        if (original == null) throw new ArgumentNullException(nameof(original));

        var redactor = new Redactor().Construir(original);
        string json = Exporter.Serializar(original);

        // De más largo a más corto: «C:\\Users\\pepe\\Desktop» antes que
        // «pepe», o el reemplazo corto dejaría la ruta a medio redactar y con
        // una forma que llama la atención justo por lo que intentaba ocultar.
        foreach (var par in redactor._mapa.OrderByDescending(p => p.Key.Length))
            json = Patron(par.Key).Replace(json, par.Value);

        // Después de los reemplazos concretos, los patrones: una MAC que no
        // estuviera en ningún campo conocido también se va.
        json = RutaPerfil.Replace(json, m =>
            m.Value.Substring(0, m.Value.Length - m.Groups[1].Length) + "USUARIO");
        json = Mac.Replace(json, "XX:XX:XX:XX:XX:XX");

        var copia = Exporter.Deserializar(json);
        if (copia == null) return original;

        redactor.Conceptos.Add("direcciones MAC y rutas del perfil");
        copia.Sistema ??= new List<KeyValueRow>();
        copia.Sistema.Insert(0, new KeyValueRow("Informe redactado",
            "SysDiag ocultó: " + string.Join(", ", redactor.Conceptos) + "."));
        copia.Sistema.Insert(1, new KeyValueRow("Advertencia",
            "Los valores ocultos se sustituyeron de forma consistente, así que dos redes distintas siguen siendo distinguibles. " +
            "Antes de compartirlo, revisa que no quede ningún dato que no quieras publicar."));
        return copia;
    }
}
