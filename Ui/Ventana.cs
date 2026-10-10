using System;
using System.Windows;

namespace SysDiag.Ui;

/// <summary>
/// Ajuste del tamaño inicial de las ventanas al área de trabajo real.
///
/// El problema: <c>MainWindow</c> nace con <c>Height="860"</c> y
/// <c>TweaksWindow</c> con <c>MaxHeight="820"</c>. En un portátil de 1366×768
/// —que es el equipo típico de una oficina o de una universidad, no un caso
/// exótico— el área de trabajo son ~728 px, así que la ventana asoma por
/// debajo del borde de la pantalla y el pie (Exportar, Generar informe) queda
/// inalcanzable: sin poder arrastrarla, porque el borde también está fuera.
///
/// Se resuelve acá y no en el XAML por dos razones: los números del XAML son la
/// intención de diseño (860 en un monitor grande sigue siendo lo correcto), y el
/// área de trabajo no se puede enlazar en una ventana sin chrome propio.
///
/// Se llama desde cada constructor, justo después de <c>InitializeComponent()</c>:
/// es el punto donde el XAML ya fijó los tamaños declarados y todavía no hubo
/// ninguna pasada de layout, así que la ventana aparece ya con su tamaño
/// ajustado y sin salto. Se prefirió esto a un class handler global sobre
/// <c>InitializedEvent</c> porque ese evento es de enrutado directo —no se
/// propaga por el árbol— y su manejo desde <c>OnStartup</c> no se puede
/// comprobar sin ejecutar; y porque llamar en los diez constructores hace que
/// <c>--self-test</c>, que construye las diez ventanas, recorra este código.
///
/// Limitación conocida y aceptada: <see cref="SystemParameters.WorkArea"/> es
/// el área del monitor principal. Con la ventana centrada en ese monitor es el
/// criterio correcto; si algún día se recuerda la posición de la ventana entre
/// corridas, el límite tiene que pasar a ser el monitor donde recae.
/// </summary>
public static class Ventana
{
    /// <summary>Margen de seguridad para que la sombra y el reloj queden visibles.</summary>
    private const double Margen = 24;

    public static void AjustarAPantalla(Window window)
    {
        if (window is null) return;

        var area = SystemParameters.WorkArea;
        if (area.Width <= 0 || area.Height <= 0) return;

        // MinHeight/MinWidth vienen en NaN si la ventana no los declaró, y
        // Math.Max propagaría el NaN al valor final: la ventana perdería su
        // tamaño. El piso de la comparación es el mínimo declarado o 0.
        double pisoAlto = double.IsNaN(window.MinHeight) ? 0 : window.MinHeight;
        double pisoAncho = double.IsNaN(window.MinWidth) ? 0 : window.MinWidth;
        double disponibleAlto = area.Height - Margen;
        double disponibleAncho = area.Width - Margen;

        window.MaxHeight = Recortar(window.MaxHeight, pisoAlto, disponibleAlto);
        window.Height = Recortar(window.Height, pisoAlto, disponibleAlto);
        window.MaxWidth = Recortar(window.MaxWidth, pisoAncho, disponibleAncho);
        window.Width = Recortar(window.Width, pisoAncho, disponibleAncho);
    }

    /// <summary>
    /// La regla, separada de la ventana para que se pueda probar sin WPF: el
    /// valor declarado se recorta al espacio disponible, pero nunca por debajo del
    /// mínimo de la propia ventana. Que el mínimo gane es deliberado —una ventana
    /// que no cabe desborda el monitor, y eso es peor que un pie que asoma: el
    /// escritorio se puede configurar, el mínimo de una app no se puede leer.
    /// </summary>
    /// <param name="declarado">Lo que dice el XAML. <c>double.NaN</c> —y
    /// <see cref="double.PositiveInfinity"/> en los <c>Max*</c>— significa «no lo fijé».</param>
    /// <param name="piso">El mínimo declarado de la ventana, o 0 si no tiene.</param>
    /// <param name="disponible">Área de trabajo menos el margen de seguridad.</param>
    public static double Recortar(double declarado, double piso, double disponible)
    {
        // Dos centinelas, no uno: Height/Width/Min* sin declarar son NaN, pero el
        // valor por defecto de MaxHeight y MaxWidth es +∞. Comprobar solo el NaN
        // —que es lo que hacía esta función antes de separarla— recortaba el
        // MaxHeight de TODAS las ventanas al área de trabajo aunque el XAML no
        // hubiera declarado ninguno: en la práctica les quitaba el máximo
        // efectivo y, en una segunda pantalla más grande, las dejaba sin poder
        // aprovecharla.
        if (double.IsNaN(declarado) || double.IsPositiveInfinity(declarado)) return declarado;
        double tope = Math.Max(piso, disponible);
        return declarado > tope ? tope : declarado;
    }
}
