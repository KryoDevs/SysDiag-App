using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SysDiag.Core;
using SysDiag.Core.Storage;
using SysDiag.Models;

namespace SysDiag.Ui;

/// <summary>
/// Salud del disco atributo por atributo.
///
/// Esta pantalla existe porque el semáforo de Windows llega tarde: dice
/// «correcto» hasta el día que dice «fallido», y entre medio no dice nada. Los
/// atributos que importan son los que avisan antes, y ninguno de ellos se ve en
/// la tabla de discos del diagnóstico general.
///
/// Dos cosas que se cuidan acá:
///
/// - **Los atributos que SysDiag no sabe interpretar se muestran sin
///   semáforo.** Pintar de verde «no sabemos qué significa esto» es la forma
///   más fácil de que una herramienta de diagnóstico mienta sin mentir.
/// - **Cuando no hay datos, se dice por qué.** Un «n/d» sin motivo deja la
///   duda de si el disco está sano o si no se pudo medir, y esas dos cosas
///   llevan a decisiones opuestas.
/// </summary>
public partial class SmartWindow : Window
{
    private CancellationTokenSource _cancelacion;

    public SmartWindow()
    {
        InitializeComponent();
        // Se ajusta antes de cualquier otra cosa: si la pantalla es más chica
        // que el alto declarado en el XAML, el pie de la ventana quedaría fuera
        // del área de trabajo y no habría cómo arrastrarla de vuelta.
        Ventana.AjustarAPantalla(this);
        Loaded += async (_, _) => await CargarAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        // Sin esto, cerrar mientras se lee deja la tarea viva hasta que
        // termine por su cuenta.
        _cancelacion?.Cancel();
        base.OnClosed(e);
    }

    private async Task CargarAsync()
    {
        _cancelacion?.Cancel();
        _cancelacion = new CancellationTokenSource();
        var token = _cancelacion.Token;

        BtnLeer.IsEnabled = false;
        PanelDiscos.Children.Clear();
        TxtEstado.Text = "Leyendo los atributos…";
        PanelDiscos.Children.Add(Texto("Leyendo los atributos de los discos. Puede tardar unos segundos.", "Dim"));

        List<SmartDisco> discos;
        try
        {
            // Fuera del hilo de la interfaz: son dos consultas WMI y la de los
            // contadores de fiabilidad puede demorar.
            discos = await Task.Run(() => SmartModule.Consultar(token));
        }
        catch (Exception ex)
        {
            AppLog.Write("No se pudieron leer los atributos SMART: " + ex.Message, "WARN");
            BtnLeer.IsEnabled = true;
            TxtEstado.Text = "no se pudo leer";
            PanelDiscos.Children.Clear();
            PanelDiscos.Children.Add(Texto("No se pudieron leer los atributos SMART: " + ex.Message, "Dim"));
            return;
        }

        if (token.IsCancellationRequested) return;

        BtnLeer.IsEnabled = true;
        PanelDiscos.Children.Clear();

        int criticos = discos.Sum(d => d.Atributos.Count(a => a.Estado == Severity.Bad));
        int avisos = discos.Sum(d => d.Atributos.Count(a => a.Estado == Severity.Warn));
        int legibles = discos.Count(d => d.Disponible);

        TxtEstado.Text = legibles == 0
            ? "sin datos en ningún disco"
            : $"{legibles} disco(s) con datos · {criticos} atributo(s) crítico(s) · {avisos} en atención";

        foreach (var disco in discos) PanelDiscos.Children.Add(Tarjeta(disco));
    }

    private UIElement Tarjeta(SmartDisco disco)
    {
        Brush acento = ColorDe(disco.Estado);
        bool sinDatos = !disco.Disponible;

        var tarjeta = new Border
        {
            Style = (Style)FindResource("CardOutline"),
            Margin = new Thickness(0, 0, 0, 14),
            Padding = new Thickness(18, 16, 18, 16)
        };

        var cuerpo = new StackPanel();

        // ---- Encabezado ----------------------------------------------------
        var cabecera = new Grid();
        cabecera.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cabecera.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titulo = new StackPanel();
        titulo.Children.Add(new TextBlock
        {
            Text = disco.Nombre,
            Style = (Style)FindResource("Body"),
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });

        var datos = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
        datos.Children.Add(Chip(disco.Tipo, "BTextMuted"));
        if (!string.IsNullOrWhiteSpace(disco.Firmware))
            datos.Children.Add(Chip("firmware " + disco.Firmware, "BTextMuted"));
        if (!string.IsNullOrWhiteSpace(disco.Fuente)) datos.Children.Add(Chip(disco.Fuente, "BTextMuted"));
        titulo.Children.Add(datos);

        Grid.SetColumn(titulo, 0);
        cabecera.Children.Add(titulo);

        var estado = new Border
        {
            Style = (Style)FindResource("Chip"),
            Background = acento,
            Opacity = 0.85,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(14, 0, 0, 0)
        };
        estado.Child = new TextBlock
        {
            Text = Etiqueta(disco.Estado, sinDatos).ToUpperInvariant(),
            Style = (Style)FindResource("Eyebrow"),
            Foreground = Res("BBase")
        };
        Grid.SetColumn(estado, 1);
        cabecera.Children.Add(estado);
        cuerpo.Children.Add(cabecera);

        // ---- Resumen o motivo ----------------------------------------------
        if (sinDatos)
        {
            // El motivo va primero y no al final: es la diferencia entre «el
            // disco está bien» y «no se pudo medir», que son decisiones
            // opuestas.
            cuerpo.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(disco.Motivo) ? "Sin datos para este disco." : disco.Motivo,
                Style = (Style)FindResource("Body"),
                Foreground = Res("BWarn"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 12, 0, 0)
            });
            tarjeta.Child = cuerpo;
            return tarjeta;
        }

        if (!string.IsNullOrWhiteSpace(disco.Resumen))
            cuerpo.Children.Add(new TextBlock
            {
                Text = disco.Resumen,
                Style = (Style)FindResource("Body"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            });

        if (!string.IsNullOrWhiteSpace(disco.Motivo))
            cuerpo.Children.Add(new TextBlock
            {
                Text = disco.Motivo,
                Style = (Style)FindResource("Dim"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
                FontSize = 12
            });

        cuerpo.Children.Add(EncabezadoColumnas());
        foreach (var atributo in disco.Atributos) cuerpo.Children.Add(Fila(atributo));
        cuerpo.Children.Add(Nota());

        tarjeta.Child = cuerpo;
        return tarjeta;
    }

    private Grid EncabezadoColumnas()
    {
        var grilla = new Grid { Margin = new Thickness(0, 14, 0, 4) };
        foreach (var ancho in Anchos())
            grilla.ColumnDefinitions.Add(new ColumnDefinition { Width = ancho });

        Agregar(grilla, 1, "ID", "SectionLabel");
        Agregar(grilla, 2, "Atributo", "SectionLabel");
        Agregar(grilla, 3, "Valor / umbral", "SectionLabel");
        Agregar(grilla, 4, "Crudo", "SectionLabel");
        Agregar(grilla, 5, "Lectura", "SectionLabel");
        return grilla;
    }

    private UIElement Fila(SmartAtributo atributo)
    {
        // Lo que no se interpreta se muestra igual, pero sin semáforo: el
        // punto queda gris y la fila no afirma nada sobre la salud del disco.
        Brush punto = atributo.Interpretable ? ColorDe(atributo.Estado) : Res("BStroke");

        var grilla = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        foreach (var ancho in Anchos())
            grilla.ColumnDefinitions.Add(new ColumnDefinition { Width = ancho });

        var barra = new System.Windows.Shapes.Rectangle
        {
            Width = 4, RadiusX = 2, RadiusY = 2,
            Fill = punto,
            Margin = new Thickness(0, 2, 10, 2)
        };
        Grid.SetColumn(barra, 0);
        grilla.Children.Add(barra);

        Agregar(grilla, 1, atributo.Codigo, "Dim", "FMono");
        Agregar(grilla, 2, atributo.Nombre, "Body");

        var valor = new TextBlock
        {
            Text = atributo.Valor,
            Style = (Style)FindResource("Dim"),
            FontFamily = (FontFamily)FindResource("FMono")
        };
        var valores = new StackPanel { Orientation = Orientation.Horizontal };
        valores.Children.Add(valor);
        valores.Children.Add(new TextBlock
        {
            Text = " / " + atributo.Umbral,
            Style = (Style)FindResource("Dim"),
            FontFamily = (FontFamily)FindResource("FMono"),
            Foreground = Res("BTextMuted")
        });
        Grid.SetColumn(valores, 3);
        grilla.Children.Add(valores);

        var crudo = new TextBlock
        {
            Text = atributo.Crudo,
            Style = (Style)FindResource("Dim"),
            FontFamily = (FontFamily)FindResource("FMono")
        };
        Grid.SetColumn(crudo, 4);
        grilla.Children.Add(crudo);

        var lectura = new TextBlock
        {
            Text = atributo.Lectura,
            Style = (Style)FindResource("Dim"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = atributo.Interpretable && atributo.Estado != Severity.Ok
                ? ColorDe(atributo.Estado)
                : Res("BTextDim")
        };
        Grid.SetColumn(lectura, 5);
        grilla.Children.Add(lectura);

        return grilla;
    }

    private static GridLength[] Anchos() => new[]
    {
        GridLength.Auto,
        new GridLength(38),
        new GridLength(190),
        new GridLength(96),
        new GridLength(104),
        new GridLength(1, GridUnitType.Star)
    };

    private void Agregar(Grid grilla, int columna, string texto, string estilo, string fuente = null)
    {
        var bloque = new TextBlock { Text = texto, Style = (Style)FindResource(estilo) };
        if (fuente != null) bloque.FontFamily = (FontFamily)FindResource(fuente);
        if (columna >= 1) bloque.Margin = new Thickness(0, 0, 12, 0);
        bloque.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(bloque, columna);
        grilla.Children.Add(bloque);
    }

    /// <summary>
    /// El cierre de la tabla. Sin esta nota, la tabla da a entender que el
    /// valor de un atributo alcanza para juzgar el disco, y no alcanza: lo que
    /// dice si un daño avanza es la serie, no la foto.
    /// </summary>
    private UIElement Nota() => new TextBlock
    {
        Text = "Los atributos son una foto de este momento. Un número alto que no se mueve no es una falla, y uno bajo que crece sí lo es: para saber cuál de los dos casos es, hay que volver a leer con el tiempo.",
        Style = (Style)FindResource("Dim"),
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Res("BTextMuted"),
        Margin = new Thickness(0, 12, 0, 0)
    };

    private UIElement Chip(string texto, string clave)
    {
        var borde = new Border
        {
            Style = (Style)FindResource("MiniChip"),
            Margin = new Thickness(0, 0, 6, 0)
        };
        borde.Child = new TextBlock
        {
            Text = texto,
            Style = (Style)FindResource("MiniChipText"),
            Foreground = Res(clave)
        };
        return borde;
    }

    private static UIElement Texto(string contenido, string estilo) => new TextBlock
    {
        Text = contenido,
        Style = (Style)Application.Current.Resources[estilo],
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(2, 20, 0, 8)
    };

    private static string Etiqueta(Severity estado, bool sinDatos)
    {
        if (sinDatos) return "sin datos";
        return estado switch { Severity.Bad => "crítico", Severity.Warn => "atención", _ => "correcto" };
    }

    private static Brush ColorDe(Severity estado)
    {
        string clave = estado switch { Severity.Bad => "BBad", Severity.Warn => "BWarn", _ => "BOk" };
        return Application.Current.Resources[clave] as Brush ?? Brushes.Gray;
    }

    private static Brush Res(string clave) =>
        Application.Current.Resources[clave] as Brush ?? Brushes.Gray;

    // ---- Acciones ----------------------------------------------------------

    private async void Leer_Click(object sender, RoutedEventArgs e) => await CargarAsync();

    private void Cerrar_Click(object sender, RoutedEventArgs e) => Close();
}
