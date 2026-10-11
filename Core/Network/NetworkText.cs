using System.Globalization;
using System.Text.RegularExpressions;

namespace SysDiag.Core.Network;

/// <summary>
/// Normalización del texto que devuelven las herramientas de red.
///
/// `netsh` imprime sus etiquetas en el idioma de Windows («Señal» o «Signal»,
/// «Canal» o «Channel»), así que quien lea su salida compara etiquetas sin
/// acentos, sin mayúsculas y sin espacios de sobra. Hay tres lectores —el
/// módulo de red, el de pérdida por salto y el mapa de canales— y una regla
/// duplicada tres veces es una regla que termina divergiendo.
/// </summary>
public static class NetworkText
{
    /// <summary>Minúsculas, sin acentos y sin espacios en los extremos.</summary>
    public static string Normalizar(string texto)
    {
        if (string.IsNullOrEmpty(texto)) return "";
        string s = texto.Trim().ToLowerInvariant();
        return s.Replace('á', 'a').Replace('é', 'e').Replace('í', 'i')
                .Replace('ó', 'o').Replace('ú', 'u').Replace('ü', 'u').Replace('ñ', 'n');
    }

    /// <summary>
    /// El primer entero del texto, o 0 si no hay ninguno. Es lo que se necesita
    /// para leer «11» de «Canal : 11» y «78» de «Señal : 78 %». No lee negativos
    /// —ninguna de las etiquetas que se leen con esto los usa— y por eso un
    /// «-60 dBm» devolvería 60: para leer un valor con signo hay que sumar el
    /// signo al patrón, no confiar en este.
    /// </summary>
    public static int Entero(string texto)
    {
        if (string.IsNullOrEmpty(texto)) return 0;
        var match = Regex.Match(texto, @"\d+");
        return match.Success && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int valor)
            ? valor : 0;
    }
}
