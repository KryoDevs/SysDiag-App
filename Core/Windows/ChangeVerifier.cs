using System;
using System.Globalization;

namespace SysDiag.Core.Windows;

/// <summary>
/// Resultado de releer un valor después de escribirlo.
/// </summary>
public sealed class Verificacion
{
    /// <summary>Qué se intentó cambiar, en palabras del usuario.</summary>
    public string Que { get; init; } = "";

    /// <summary>True solo si el valor releído coincide con el que se pidió.</summary>
    public bool Aplicado { get; init; }

    public string Leido { get; init; } = "";
    public string Esperado { get; init; } = "";

    /// <summary>
    /// Por qué puede no haberse aplicado. Nunca se afirma una causa que no se
    /// pudo comprobar: se enumeran las plausibles, en el orden en que suelen
    /// darse, para que el usuario sepa dónde mirar.
    /// </summary>
    public string Motivo { get; init; } = "";

    public override string ToString() => Aplicado
        ? $"{Que}: aplicado ({Leido})"
        : $"{Que}: no se aplicó. Se pidió «{Esperado}» y Windows devuelve «{Leido}». {Motivo}";
}

/// <summary>
/// Verificación posterior al cambio: releer y comparar en lugar de suponer.
///
/// Es la corrección del defecto más dañino que puede tener una herramienta
/// como esta: informar éxito donde no lo hubo. Un ajuste de registro se
/// escribe y la llamada devuelve sin error en tres casos muy distintos —se
/// aplicó, se aplicó y algo lo revirtió enseguida, o no se aplicó porque una
/// directiva de grupo lo repone, porque un antivirus lo bloquea o porque el
/// proceso no estaba elevado—. Los tres se ven igual desde el código que
/// escribe, y solo el tercero se puede distinguir volviendo a leer.
///
/// Releer cuesta una llamada. No hacerlo cuesta la confianza en el informe.
/// </summary>
public static class ChangeVerifier
{
    /// <summary>
    /// Compara un valor releído con el texto que se pidió escribir. Acepta
    /// enteros en cualquiera de sus formas (el registro devuelve `int` donde se
    /// escribió un `0`, y `long` donde se escribió un `1.000.000`).
    /// </summary>
    public static bool Coincide(object actual, string objetivo)
    {
        if (actual == null) return false;
        if (long.TryParse(objetivo, NumberStyles.Integer, CultureInfo.InvariantCulture, out long numerico))
        {
            try { return Convert.ToInt64(actual, CultureInfo.InvariantCulture) == numerico; }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) { }
        }
        return string.Equals(actual.ToString(), objetivo, StringComparison.Ordinal);
    }

    /// <summary>
    /// Relee y compara. El delegado se invoca acá —no recibe un valor ya
    /// leído— porque la lectura tiene que ocurrir después de la escritura, y
    /// pasarle un valor cacheado haría que esta función siempre diera «bien».
    /// </summary>
    public static Verificacion Verificar(string que, Func<object> leer, string objetivo, bool requiereAdmin = false)
    {
        object actual;
        try { actual = leer(); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.IO.IOException or System.Security.SecurityException)
        {
            return new Verificacion
            {
                Que = que,
                Aplicado = false,
                Esperado = objetivo ?? "",
                Leido = "sin acceso",
                Motivo = $"No se pudo releer el valor: {ex.Message}."
            };
        }

        bool ok = Coincide(actual, objetivo ?? "");
        return new Verificacion
        {
            Que = que,
            Aplicado = ok,
            Esperado = objetivo ?? "",
            Leido = actual?.ToString() ?? "(vacío)",
            Motivo = ok ? "" : MotivoPosible(requiereAdmin)
        };
    }

    /// <summary>
    /// Explicación de por qué un cambio puede no quedar. Se enumera en orden de
    /// frecuencia y sin afirmar: la diferencia entre «una directiva de grupo lo
    /// repone» y «lo repone una directiva de grupo» es que la segunda se puede
    /// comprobar y la primera no.
    /// </summary>
    public static string MotivoPosible(bool requiereAdmin)
    {
        string primero = requiereAdmin && !AppEnv.IsAdmin
            ? "SysDiag no está elevado y este cambio necesita permisos de administrador."
            : "Es habitual que una directiva de grupo, una protección del propio Windows o un antivirus reponga el valor justo después de escribirlo.";
        return primero + " También puede hacer falta cerrar sesión para que el cambio se lea desde donde se está consultando.";
    }
}
