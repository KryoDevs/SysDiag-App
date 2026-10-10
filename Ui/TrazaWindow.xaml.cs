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
using SysDiag.Core.Network;
using SysDiag.Models;

namespace SysDiag.Ui;

/// <summary>
/// Pérdida y jitter por salto, para localizar el tramo que falla.
///
/// El traceroute que ya tenía la app dice por dónde pasan los paquetes, no
/// dónde se pierden: un solo ping por salto no alcanza para medir pérdida, y
/// medir pérdida es justo lo que localiza el tramo.
///
/// La parte difícil no es medir, es no concluir de más: un router intermedio
/// con pérdida y los saltos siguientes limpios no está perdiendo paquetes,
/// está limitando los ICMP que contesta. Sin esa distinción, la conclusión
/// siempre es la cómoda —«es el proveedor»— y casi siempre es falsa. La lectura
/// la hace `TraceMath.Interpretar`, que mira la serie completa.
/// </summary>
public partial class TrazaWindow : Window
{
    private CancellationTokenSource _cancelacion;
    private bool _midiendo;

    public TrazaWindow()
    {
        InitializeComponent();
        // Se ajusta antes de cualquier otra cosa: si la pantalla es más chica
        // que el alto declarado en el XAML, el pie de la ventana quedaría fuera
        // del área de trabajo y no habría cómo arrastrarla de vuelta.
        Ventana.AjustarAPantalla(this);

        ComboDestino.Items.Add("1.1.1.1");
        ComboDestino.Items.Add("8.8.8.8");
        ComboDestino.SelectedIndex = 0;

        foreach (int muestras in new[] { 10, 20, 30 })
            ComboMuestras.Items.Add($"{muestras} muestras por salto");
        ComboMuestras.SelectedIndex = 0;
    }

    protected override void OnClosed(EventArgs e)
    {
        _cancelacion?.Cancel();
        base.OnClosed(e);
    }

    private async void Medir_Click(object sender, RoutedEventArgs e)
    {
        // El mismo botón cancela: la medición puede tardar medio minuto y sin
        // una salida la ventana parece colgada.
        if (_midiendo)
        {
            _cancelacion?.Cancel();
            return;
        }

        string objetivo = (ComboDestino.Text ?? "").Trim();
        if (objetivo.Length == 0)
        {
            TxtEstado.Text = "escribí un destino";
            return;
        }

        int muestras = ComboMuestras.SelectedIndex switch { 1 => 20, 2 => 30, _ => 10 };

        _cancelacion = new CancellationTokenSource();
        var token = _cancelacion.Token;
        _midiendo = true;
        BtnMedir.Content = "Cancelar";
        BtnMedir.Style = (Style)FindResource("BtnGhost");
        ComboDestino.IsEnabled = false;
        ComboMuestras.IsEnabled = false;

        PanelResultado.Children.Clear();
        TxtEstado.Text = "buscando la ruta…";

        // El avance se reporta desde el módulo: «salto N de M» es la única
        // forma de que medio minuto de espera no parezca un cuelgue.
        var progreso = new Progress<int>(salto => TxtEstado.Text = $"midiendo el salto {salto}…");

        List<HopLossRow> ruta;
        try
        {
            ruta = await TraceRouteModule.MedirAsync(objetivo, progreso, token, muestras);
        }
        catch (OperationCanceledException)
        {
            Terminar();
            TxtEstado.Text = "cancelado";
            return;
        }
        catch (Exception ex)
        {
            AppLog.Write("No se pudo medir la ruta: " + ex.Message, "WARN");
            Terminar();
            TxtEstado.Text = "no se pudo medir";
            PanelResultado.Children.Add(Texto("No se pudo medir la ruta: " + ex.Message, "Dim"));
            return;
        }

        Terminar();

        if (ruta.Count == 0)
        {
            TxtEstado.Text = "sin ruta";
            PanelResultado.Children.Add(Texto(
                "Ningún equipo del camino respondió. Puede que la red bloquee los ICMP, o que el destino no conteste este tipo de tráfico: con eso no hay pérdida que medir.",
                "Dim"));
            return;
        }

        int medibles = ruta.Count(h => h.Respondidos > 0);
        TxtEstado.Text = $"{ruta.Count} salto(s) · {medibles} medible(s) · {muestras} muestras cada uno";

        PanelResultado.Children.Add(Encabezado());
        foreach (var fila in ruta) PanelResultado.Children.Add(Fila(fila));

        var conclusiones = TraceMath.Interpretar(ruta);
        if (conclusiones.Count > 0) PanelResultado.Children.Add(Conclusiones(conclusiones));
        PanelResultado.Children.Add(Nota());
    }

    private void Terminar()
    {
        _midiendo = false;
        BtnMedir.Content = "Medir";
        BtnMedir.Style = (Style)FindResource("BtnPrimary");
        ComboDestino.IsEnabled = true;
        ComboMuestras.IsEnabled = true;
    }

    private Grid Encabezado()
    {
        var grilla = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        foreach (var ancho in Anchos())
            grilla.ColumnDefinitions.Add(new ColumnDefinition { Width = ancho });

        Agregar(grilla, 0, "Salto", "SectionLabel");
        Agregar(grilla, 1, "Dirección", "SectionLabel");
        Agregar(grilla, 2, "Nombre", "SectionLabel");
        Agregar(grilla, 3, "Pérdida", "SectionLabel");
        Agregar(grilla, 4, "Mín", "SectionLabel");
        Agregar(grilla, 5, "Media", "SectionLabel");
        Agregar(grilla, 6, "Máx", "SectionLabel");
        Agregar(grilla, 7, "Jitter", "SectionLabel");
        Agregar(grilla, 8, "Lectura", "SectionLabel");
        return grilla;
    }

    private UIElement Fila(HopLossRow fila)
    {
        var grilla = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        foreach (var ancho in Anchos())
            grilla.ColumnDefinitions.Add(new ColumnDefinition { Width = ancho });

        Brush tinta = ColorDe(fila.Estado);

        Agregar(grilla, 0, fila.Salto.ToString(CultureInfo.CurrentCulture), "Dim", "FMono");
        Agregar(grilla, 1, fila.Direccion, "Body", "FMono");
        Agregar(grilla, 2, fila.Nombre, "Dim");

        var perdida = new TextBlock
        {
            Text = fila.Respondidos == 0 ? "—" : Numero(fila.PerdidaPct) + " %",
            Style = (Style)FindResource("Body"),
            FontFamily = (FontFamily)FindResource("FMono"),
            Foreground = fila.Respondidos == 0 ? Res("BTextMuted") : tinta
        };
        Grid.SetColumn(perdida, 3);
        grilla.Children.Add(perdida);

        Agregar(grilla, 4, fila.Respondidos == 0 ? "—" : Numero(fila.Minimo), "Dim", "FMono");
        Agregar(grilla, 5, fila.Respondidos == 0 ? "—" : Numero(fila.Media), "Body", "FMono");
        Agregar(grilla, 6, fila.Respondidos == 0 ? "—" : Numero(fila.Maximo), "Dim", "FMono");
        Agregar(grilla, 7, fila.Respondidos == 0 ? "—" : Numero(fila.Jitter), "Dim", "FMono");

        var nota = new TextBlock
        {
            Text = fila.Nota,
            Style = (Style)FindResource("Dim"),
            Foreground = fila.Respondidos == 0 ? Res("BTextMuted") : Res("BTextDim"),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(nota, 8);
        grilla.Children.Add(nota);

        return grilla;
    }

    private static GridLength[] Anchos() => new[]
    {
        new GridLength(52),
        new GridLength(132),
        new GridLength(150),
        new GridLength(74),
        new GridLength(64),
        new GridLength(70),
        new GridLength(64),
        new GridLength(64),
        new GridLength(1, GridUnitType.Star)
    };

    private static string Numero(double valor) => valor.ToString("0.#", CultureInfo.CurrentCulture);

    private UIElement Conclusiones(List<ConclusionRuta> conclusiones)
    {
        var tarjeta = new Border
        {
            Style = (Style)FindResource("CardOutline"),
            Margin = new Thickness(0, 18, 0, 0),
            Padding = new Thickness(18, 16, 18, 16)
        };

        var cuerpo = new StackPanel();
        cuerpo.Children.Add(new TextBlock
        {
            Text = "Qué dice la ruta",
            Style = (Style)FindResource("H2"),
            Margin = new Thickness(0, 0, 0, 10)
        });

        foreach (var c in conclusiones)
        {
            var grilla = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            grilla.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grilla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var barra = new System.Windows.Shapes.Rectangle
            {
                Width = 4, RadiusX = 2, RadiusY = 2,
                Fill = ColorDe(c.Estado),
                Margin = new Thickness(0, 2, 12, 2)
            };
            Grid.SetColumn(barra, 0);
            grilla.Children.Add(barra);

            var texto = new TextBlock
            {
                Text = c.Texto,
                Style = (Style)FindResource("Body"),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(texto, 1);
            grilla.Children.Add(texto);

            cuerpo.Children.Add(grilla);
        }

        tarjeta.Child = cuerpo;
        return tarjeta;
    }

    /// <summary>
    /// El cierre. Sin esta nota, una tabla con una columna de pérdida invita a
    /// leer cada salto por separado, que es justo lo que lleva a la conclusión
    /// equivocada.
    /// </summary>
    private UIElement Nota() => new TextBlock
    {
        Text = "La pérdida de un salto se lee junto con la de los siguientes: si un salto pierde y los demás no, casi siempre es el router limitando los ICMP que contesta, no un enlace roto. " +
               "Los saltos marcados con «*» no respondieron, y eso no es pérdida: no se puede medir lo que no contesta.",
        Style = (Style)FindResource("Dim"),
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Res("BTextMuted"),
        Margin = new Thickness(0, 16, 0, 0)
    };

    private void Agregar(Grid grilla, int columna, string texto, string estilo, string fuente = null)
    {
        var bloque = new TextBlock { Text = texto, Style = (Style)FindResource(estilo) };
        if (fuente != null) bloque.FontFamily = (FontFamily)FindResource(fuente);
        bloque.Margin = new Thickness(0, 0, 12, 0);
        bloque.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(bloque, columna);
        grilla.Children.Add(bloque);
    }

    private static UIElement Texto(string contenido, string estilo) => new TextBlock
    {
        Text = contenido,
        Style = (Style)Application.Current.Resources[estilo],
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(2, 20, 0, 8)
    };

    private static Brush ColorDe(Severity estado)
    {
        string clave = estado switch { Severity.Bad => "BBad", Severity.Warn => "BWarn", _ => "BOk" };
        return Application.Current.Resources[clave] as Brush ?? Brushes.Gray;
    }

    private static Brush Res(string clave) =>
        Application.Current.Resources[clave] as Brush ?? Brushes.Gray;

    private void Cerrar_Click(object sender, RoutedEventArgs e) => Close();
}
