using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// Reglas del sistema visual, medidas sobre <c>Ui/Theme.xaml</c> en lugar de
/// verificadas a ojo. nacieron de dos cosas concretas: encontrar tokens de texto
/// por debajo del mínimo de contraste (docs/MEJORAS.md) y comprobar que un color
/// cambiado «porque sí» no rompía ninguna prueba. El autotest del ejecutable
/// valida que los recursos existan y se puedan construir, no que se puedan leer.
///
/// El tema es un diccionario de tokens, así que la comprobación es XML +
/// aritmética: no hace falta WPF ni un render para saber si un rótulo tenue sobre
/// una superficie alterna se ve.
/// </summary>
public class ThemeRegressionTests
{
    private const string XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>Fondos donde vive texto de cuerpo: panel, rail y las dos superficies.</summary>
    private static readonly string[] FondosPanel = { "CBase", "CRail", "CSurface", "CSurfaceAlt" };

    /// <summary>
    /// Tintes que se usan como texto o como dato sobre esos fondos. Los
    /// semánticos van aquí y no en una lista aparte porque en este tema un número
    /// de métrica es texto: si no se lee, no informa.
    /// </summary>
    private static readonly string[] TintesDeTexto =
        { "CText", "CTextDim", "CTextMuted", "CAccent", "CAccent2", "COk", "CWarn", "CBad" };

    /// <summary>Mínimo de WCAG 2.1 AA para texto normal. El tema no tiene texto grande.</summary>
    private const double MinimoAA = 4.5;

    [Theory]
    [MemberData(nameof(TodasLasCombinaciones))]
    public void Theme_TextTintMeetsContrastMinimumOnItsBackground(string tint, string fondo)
    {
        Dictionary<string, string> colores = LeerColoresDelTema();
        double ratio = Contraste(colores[tint], colores[fondo]);
        Assert.True(ratio >= MinimoAA,
            $"{tint} sobre {fondo}: {ratio:0.00}:1, mínimo {MinimoAA}:1 " +
            $"({colores[tint]} sobre {colores[fondo]}). Cambiar un token del tema " +
            "sin mirar el contraste afecta a las catorce etiquetas que lo usan.");
    }

    /// <summary>
    /// El fondo del ToolTip es la superficie más clara del tema y solo carga una
    /// cosa: el texto con <c>BText</c> que la propia plantilla del ToolTip fuerza.
    /// Se comprueba ese par y no todos: CTextMuted sobre CSurfaceHi daría 4,06, y
    /// eso es una restricción real del sistema (no poner rótulos tenues sobre la
    /// superficie elevada), no un fallo para arreglar a presión.
    /// </summary>
    [Fact]
    public void Theme_TooltipBackgroundOnlyCarriesPrimaryText()
    {
        Dictionary<string, string> colores = LeerColoresDelTema();
        double ratio = Contraste(colores["CText"], colores["CSurfaceHi"]);
        Assert.True(ratio >= MinimoAA, $"CText sobre CSurfaceHi (ToolTip): {ratio:0.00}:1");
    }

    /// <summary>
    /// El embudo de ejecución de comandos tiene que seguir siendo uno solo.
    /// <c>ProcessRunner</c> es lo que aporta timeout, cancelación, drenaje
    /// concurrente de las dos tuberías y muerte del árbol; la auditoría anterior
    /// tuvo que arreglar seis procesos sin liberar, que es el tipo exacto de cosa
    /// que se cuela cuando cada sitio construye su propio proceso.
    ///
    /// No cuenta <c>Process.Start(new ProcessStartInfo(...))</c>: abrir una URL,
    /// un archivo o una página de ms-settings es otro problema (no hay nada que
    /// esperar ni drenar) y pasarlo por el runner sería peor. Ese es el motivo
    /// por el que el patrón busca la construcción del objeto, no la palabra.
    /// </summary>
    [Fact]
    public void Core_OnlyProcessRunnerCreatesAProcess()
    {
        string raiz = RaizDelRepositorio();
        string permitido = Path.GetFullPath(Path.Combine(raiz, "Core", "ProcessRunner.cs"));
        var patron = new Regex(@"new\s+Process\s*[\{\(]");
        var fuera = new List<string>();

        foreach (string archivo in Directory.EnumerateFiles(raiz, "*.cs", SearchOption.AllDirectories))
        {
            string normal = Path.GetFullPath(archivo);
            if (EstaFueraDelAmbito(normal)) continue;
            if (normal == permitido) continue;
            if (patron.IsMatch(File.ReadAllText(normal))) fuera.Add(Path.GetRelativePath(raiz, normal));
        }

        Assert.True(fuera.Count == 0,
            "Construir un Process fuera de Core/ProcessRunner.cs deja de tener timeout, " +
            "cancelación ni drenaje de tuberías. Sitios: " + string.Join(", ", fuera));
    }

    public static IEnumerable<object[]> TodasLasCombinaciones()
    {
        foreach (string tint in TintesDeTexto)
            foreach (string fondo in FondosPanel)
                yield return new object[] { tint, fondo };
    }

    // ---- medición ----------------------------------------------------------

    private static bool EstaFueraDelAmbito(string rutaNormalizada)
    {
        foreach (string parte in new[] { "Tools", "obj", "bin", "publish", "installer" })
            if (rutaNormalizada.Contains(Path.DirectorySeparatorChar + parte + Path.DirectorySeparatorChar))
                return true;
        return false;
    }

    /// <summary>
    /// Razón de contraste de WCAG 2.1: (L1 + 0,05) / (L2 + 0,05) con luminancia
    /// relativa en sRGB linealizado. Los tintes del tema son opacos; si algún día
    /// uno lleva alfa, hay que componer contra el fondo antes de medir y esta
    /// prueba tiene que decirlo, no aproximar.
    /// </summary>
    private static double Contraste(string hexA, string hexB)
    {
        double la = Luminancia(hexA), lb = Luminancia(hexB);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminancia(string hexArgb)
    {
        string h = hexArgb.TrimStart('#');
        if (h.Length == 8) h = h.Substring(2);
        Assert.True(h.Length == 6, $"color ilegible: {hexArgb}");
        double[] c =
        {
            Convert.ToInt32(h.Substring(0, 2), 16) / 255.0,
            Convert.ToInt32(h.Substring(2, 2), 16) / 255.0,
            Convert.ToInt32(h.Substring(4, 2), 16) / 255.0,
        };
        for (int i = 0; i < 3; i++)
            c[i] = c[i] <= 0.03928 ? c[i] / 12.92 : Math.Pow((c[i] + 0.055) / 1.055, 2.4);
        return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];
    }

    private static Dictionary<string, string> LeerColoresDelTema()
    {
        string ruta = Path.Combine(RaizDelRepositorio(), "Ui", "Theme.xaml");
        Assert.True(File.Exists(ruta), $"no se encontró el tema en {ruta}");

        // Se lee como XML y no con una expresión regular sobre el texto: el
        // archivo ya tiene que estar bien formado para que WPF lo cargue, y así
        // un color declarado dentro de un SolidColorBrush no se queda invisible.
        var xaml = XDocument.Load(ruta);
        XNamespace x = XamlNs;
        var colores = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (XElement elemento in xaml.Descendants().Where(e => e.Name.LocalName == "Color"))
        {
            string? clave = (string?)elemento.Attribute(x + "Key");
            string hex = elemento.Value.Trim();
            if (clave != null && hex.Length > 0) colores[clave] = hex.ToUpperInvariant();
        }

        // Que el día que alguien renombre un token la prueba grite en vez de
        // pasar sin comprobar nada.
        foreach (string clave in FondosPanel.Concat(TintesDeTexto).Append("CSurfaceHi"))
            Assert.True(colores.ContainsKey(clave), $"el tema ya no define un color llamado {clave}");
        return colores;
    }

    private static string RaizDelRepositorio()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SysDiag.csproj"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "Ningún directorio padre contiene SysDiag.csproj: estas pruebas miden el tema desde las " +
            "fuentes, no desde el paquete publicado.");
    }
}
