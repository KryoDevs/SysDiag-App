using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SysDiag.Core;
using SysDiag.Core.Performance;

namespace SysDiag.Ui;

/// <summary>
/// Qué proceso le está moviendo el disco al equipo, y quién tiene la red abierta.
///
/// Es la otra mitad del diagnóstico de rendimiento: la CPU por proceso ya estaba,
/// y sin esto la pregunta «¿quién tiene el disco al 100 %?» exigía salir de la
/// aplicación y abrir el Monitor de recursos.
///
/// Dos cosas que esta pantalla declara en lugar de disimular:
///
/// - **De la red se cuentan conexiones, no bytes.** Windows no expone un
///   contador de red por proceso sin ETW. Dibujar uno a partir de otra cosa
///   sería inventar justo la medición más difícil.
/// - **Tarda lo que dura el muestreo.** Una diferencia de contadores necesita
///   dos lecturas separadas en el tiempo; una lectura instantánea daría el
///   acumulado del proceso y premiaría al más antiguo.
/// </summary>
public partial class ConsumoWindow : Window
{
    private const int Segundos = 5;
    private const int MaximoFilas = 25;

    private CancellationTokenSource _cancelacion;

    public ConsumoWindow()
    {
        InitializeComponent();
        // Se ajusta antes de cualquier otra cosa: si la pantalla es más chica
        // que el alto declarado en el XAML, el pie de la ventana quedaría fuera
        // del área de trabajo y no habría cómo arrastrarla de vuelta.
        Ventana.AjustarAPantalla(this);
        Loaded += async (_, _) => await MedirAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _cancelacion?.Cancel();
        base.OnClosed(e);
    }

    private async Task MedirAsync()
    {
        _cancelacion?.Cancel();
        _cancelacion = new CancellationTokenSource();
        var token = _cancelacion.Token;

        BtnMedir.IsEnabled = false;
        PanelFilas.Children.Clear();
        TxtEstado.Text = $"midiendo {Segundos} segundos…";
        PanelFilas.Children.Add(Texto($"Midiendo durante {Segundos} segundos. Se leen los contadores del proceso dos veces, así que la espera es parte de la medición.", "Dim"));

        List<ProcesoIo> filas;
        try
        {
            filas = await Task.Run(() => ProcessIoModule.Medir(Segundos, token));
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            AppLog.Write("No se pudo medir el consumo por proceso: " + ex.Message, "WARN");
            BtnMedir.IsEnabled = true;
            TxtEstado.Text = "no se pudo medir";
            PanelFilas.Children.Clear();
            PanelFilas.Children.Add(Texto("No se pudo medir el consumo: " + ex.Message, "Dim"));
            return;
        }

        if (token.IsCancellationRequested) return;

        BtnMedir.IsEnabled = true;
        PanelFilas.Children.Clear();

        var visibles = ProcesoIoMath.Top(filas, MaximoFilas);
        int conRed = visibles.Count(f => f.Conexiones > 0);

        TxtEstado.Text = visibles.Count == 0
            ? "nadie movió el disco en ese intervalo"
            : $"{visibles.Count} proceso(s) · {conRed} con la red abierta · {Segundos} s de muestreo";

        if (visibles.Count == 0)
        {
            PanelFilas.Children.Add(Texto(
                "Ningún proceso leyó ni escribió en el disco durante el intervalo, y ninguno tiene conexiones abiertas. " +
                "Si el disco está al 100 % en el Administrador de tareas, volvé a medir en ese momento: la medición es una foto, no un promedio.",
                "Dim"));
            return;
        }

        PanelFilas.Children.Add(Encabezado());
        foreach (var fila in visibles) PanelFilas.Children.Add(Fila(fila));
        PanelFilas.Children.Add(Nota());
    }

    private Grid Encabezado()
    {
        var grilla = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        foreach (var ancho in Anchos())
            grilla.ColumnDefinitions.Add(new ColumnDefinition { Width = ancho });

        Agregar(grilla, 1, "Proceso", "SectionLabel");
        Agregar(grilla, 2, "PID", "SectionLabel");
        Agregar(grilla, 3, "Lectura", "SectionLabel");
        Agregar(grilla, 4, "Escritura", "SectionLabel");
        Agregar(grilla, 5, "Total", "SectionLabel");
        Agregar(grilla, 6, "Red", "SectionLabel");
        Agregar(grilla, 7, "Habla con", "SectionLabel");
        return grilla;
    }

    private UIElement Fila(ProcesoIo fila)
    {
        var grilla = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        foreach (var ancho in Anchos())
            grilla.ColumnDefinitions.Add(new ColumnDefinition { Width = ancho });

        Agregar(grilla, 1, fila.Nombre, "Body");

        var pid = new TextBlock
        {
            Text = fila.Pid.ToString(CultureInfo.CurrentCulture),
            Style = (Style)FindResource("Dim"),
            FontFamily = (FontFamily)FindResource("FMono")
        };
        Grid.SetColumn(pid, 2);
        grilla.Children.Add(pid);

        // El total va en negrita: es la columna por la que la lista está
        // ordenada, y sin esa pista hay que leer las tres para saber por qué
        // ese proceso está primero.
        Agregar(grilla, 3, TextoVelocidad(fila.LecturaBytesS), "Dim", "FMono");
        Agregar(grilla, 4, TextoVelocidad(fila.EscrituraBytesS), "Dim", "FMono");
        Agregar(grilla, 5, TextoVelocidad(fila.TotalBytesS), "Body", "FMono");

        var red = new TextBlock
        {
            Text = fila.Conexiones > 0 ? fila.Conexiones.ToString(CultureInfo.CurrentCulture) : "—",
            Style = (Style)FindResource("Dim"),
            FontFamily = (FontFamily)FindResource("FMono")
        };
        Grid.SetColumn(red, 6);
        grilla.Children.Add(red);

        var destinos = new TextBlock
        {
            Text = fila.Destinos,
            Style = (Style)FindResource("Dim"),
            Foreground = Res(fila.Conexiones > 0 ? "BTextDim" : "BTextMuted"),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(destinos, 7);
        grilla.Children.Add(destinos);

        return grilla;
    }

    private static GridLength[] Anchos() => new[]
    {
        GridLength.Auto,
        new GridLength(180),
        new GridLength(70),
        new GridLength(100),
        new GridLength(100),
        new GridLength(100),
        new GridLength(58),
        new GridLength(1, GridUnitType.Star)
    };

    private static string TextoVelocidad(double bytesPorSegundo)
    {
        if (bytesPorSegundo <= 0) return "—";
        var (valor, unidad) = ProcesoIoMath.Velocidad(bytesPorSegundo);
        return $"{valor.ToString("0.0", CultureInfo.CurrentCulture)} {unidad}";
    }

    private void Agregar(Grid grilla, int columna, string texto, string estilo, string fuente = null)
    {
        var bloque = new TextBlock { Text = texto, Style = (Style)FindResource(estilo) };
        if (fuente != null) bloque.FontFamily = (FontFamily)FindResource(fuente);
        bloque.Margin = new Thickness(0, 0, 12, 0);
        bloque.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(bloque, columna);
        grilla.Children.Add(bloque);
    }

    /// <summary>
    /// El cierre de la tabla. Sin esta nota, dos columnas juntas («Red» y
    /// «Lectura») dan a entender que la red también se mide en bytes, y no es
    /// así; y sin decir que el muestreo es una foto, un cero se lee como «este
    /// proceso no hace nada».
    /// </summary>
    private UIElement Nota() => new TextBlock
    {
        Text = "La columna «Red» cuenta conexiones TCP abiertas y nombra sus destinos: Windows no expone bytes por proceso sin ETW, y esta tabla no finge tenerlos. " +
               "Los números son un promedio del intervalo medido, no un máximo: un proceso que escribe en ráfagas puede aparecer abajo y ser el responsable de un pico.",
        Style = (Style)FindResource("Dim"),
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Res("BTextMuted"),
        Margin = new Thickness(0, 14, 0, 0)
    };

    private static UIElement Texto(string contenido, string estilo) => new TextBlock
    {
        Text = contenido,
        Style = (Style)Application.Current.Resources[estilo],
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(2, 20, 0, 8)
    };

    private static Brush Res(string clave) =>
        Application.Current.Resources[clave] as Brush ?? Brushes.Gray;

    // ---- Acciones ----------------------------------------------------------

    private async void Medir_Click(object sender, RoutedEventArgs e) => await MedirAsync();

    private void Cerrar_Click(object sender, RoutedEventArgs e) => Close();
}
