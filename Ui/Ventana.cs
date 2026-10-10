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
        double disponibleAlto = Math.Max(pisoAlto, area.Height - Margen);
        double disponibleAncho = Math.Max(pisoAncho, area.Width - Margen);

        // NaN = "no lo fijé", y no hay nada que recortar.
        if (!double.IsNaN(window.MaxHeight) && window.MaxHeight > disponibleAlto)
            window.MaxHeight = disponibleAlto;

        if (!double.IsNaN(window.Height) && window.Height > disponibleAlto)
            window.Height = disponibleAlto;

        if (!double.IsNaN(window.MaxWidth) && window.MaxWidth > disponibleAncho)
            window.MaxWidth = disponibleAncho;

        if (!double.IsNaN(window.Width) && window.Width > disponibleAncho)
            window.Width = disponibleAncho;
    }
}
