using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using SysDiag.Core;
using SysDiag.Core.Network;
using SysDiag.Models;

namespace SysDiag.Ui;

/// <summary>
/// Mapa de canales Wi-Fi: de las redes que se ven alrededor, cuáles comparten
/// espectro con qué canal y cuál conviene usar.
///
/// El diagnóstico general ya dice «N redes comparten tu canal». Esta ventana
/// contesta la pregunta que sigue —«¿me muevo a cuál?»— y en 2.4 GHz agrega lo
/// que un número suelto no puede: los canales de al lado también molestan.
/// Toda la aritmética vive en <see cref="WifiChannelModule"/>, que es puro y
/// tiene pruebas; acá solo se dibuja y se dice lo que no se pudo medir.
/// </summary>
public partial class CanalesWindow : Window
{
    private CancellationTokenSource _cancelacion;
    private AnalisisCanales _analisis;

    public CanalesWindow()
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
        // Sin esto, cerrar mientras se escanea deja la tarea viva hasta que
        // termine por su cuenta.
        _cancelacion?.Cancel();
        base.OnClosed(e);
    }

    private void Cerrar_Click(object sender, RoutedEventArgs e) => Close();

    private async void Medir_Click(object sender, RoutedEventArgs e) => await CargarAsync();

    private async Task CargarAsync()
    {
        _cancelacion?.Cancel();
        _cancelacion = new CancellationTokenSource();
        var token = _cancelacion.Token;

        BtnMedir.IsEnabled = false;
        PanelBarras.Children.Clear();
        TxtNota.Visibility = Visibility.Collapsed;
        TxtRecomendacion.Text = "Midiendo…";
        TxtEstado.Text = "escaneando las redes cercanas";

        AnalisisCanales analisis;
        try
        {
            // Fuera del hilo de la interfaz: son dos llamadas a netsh y el
            // escaneo puede tardar varios segundos.
            analisis = await Task.Run(() => WifiChannelModule.Medir(token), token);
        }
        catch (Exception ex)
        {
            AppLog.Write("No se pudo medir el mapa de canales: " + ex.Message, "WARN");
            BtnMedir.IsEnabled = true;
            if (token.IsCancellationRequested) return;
            TxtEstado.Text = "no se pudo medir";
            TxtRecomendacion.Text = "No se pudo leer la información de las redes Wi-Fi: " + ex.Message;
            return;
        }

        if (token.IsCancellationRequested) return;

        _analisis = analisis;
        BtnMedir.IsEnabled = true;
        Dibujar(analisis);
    }

    private void Dibujar(AnalisisCanales a)
    {
        PanelBarras.Children.Clear();

        TxtEstado.Text = a.CanalPropio > 0
            ? $"{a.RedesDetectadas} punto(s) de acceso detectado(s) · {a.VecinasDetectadas} de otras redes · tu canal: {a.CanalPropio}"
            : $"{a.RedesDetectadas} punto(s) de acceso detectado(s) · {a.VecinasDetectadas} de otras redes · tu canal: no se pudo leer";

        TxtRecomendacion.Text = a.Recomendado > 0
            ? "Sugerencia: " + a.Motivo
            : a.Motivo.Length > 0 ? a.Motivo
            : "Sin sugerencia: no se pudo leer el canal de tu conexión, así que no hay contra qué comparar.";

        // La nota se muestra solo cuando hay algo que aclarar: un aviso que se
        // repite siempre (aunque no haya nada que decir) enseña a ignorarlo.
        TxtNota.Text = a.Nota;
        TxtNota.Visibility = string.IsNullOrWhiteSpace(a.Nota) ? Visibility.Collapsed : Visibility.Visible;

        if (!a.HayDatos)
        {
            TxtLeyenda.Visibility = Visibility.Collapsed;
            PanelBarras.Children.Add(Texto("No hay redes que dibujar.", "Dim"));
            return;
        }

        TxtLeyenda.Visibility = Visibility.Visible;
        Seccion("2.4 GHz · canales 1 a 13, que se solapan entre sí", a.Canales24);
        if (a.Canales5.Count > 0)
            Seccion("5 GHz · solo los canales que se ven (no se solapan entre sí)", a.Canales5);
    }

    private void Seccion(string titulo, List<CanalOcupacion> canales)
    {
        PanelBarras.Children.Add(new TextBlock
        {
            Text = titulo,
            Style = (Style)FindResource("SectionLabel"),
            Margin = new Thickness(0, 10, 0, 8)
        });

        int maximo = canales.Count == 0 ? 0 : canales.Max(c => c.PuntosDeAcceso);
        foreach (var canal in canales) PanelBarras.Children.Add(Fila(canal, maximo));
    }

    /// <summary>
    /// Una fila por canal: etiqueta, barra, distintivos y el dato. La barra se
    /// reparte con dos columnas en estrella y no con un ancho calculado a mano:
    /// así el recorrido es exacto a cualquier ancho o DPI, que es el mismo
    /// problema que tuvo la barra indeterminada del pie (auditoría 2026-10-10).
    /// </summary>
    private UIElement Fila(CanalOcupacion canal, int maximo)
    {
        var fila = new Grid { Margin = new Thickness(0, 0, 0, 7) };
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });

        var etiqueta = new TextBlock
        {
            Text = $"Canal {canal.Canal}",
            Style = (Style)FindResource(canal.Propio ? "Body" : "Dim"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(etiqueta, 0);
        fila.Children.Add(etiqueta);

        var pista = new Border
        {
            Background = (Brush)FindResource("BTrack"),
            Height = 10,
            CornerRadius = new CornerRadius(5),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        double fraccion = maximo <= 0 ? 0 : (double)canal.PuntosDeAcceso / maximo;
        var relleno = new Grid();
        relleno.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fraccion, GridUnitType.Star) });
        relleno.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - fraccion), GridUnitType.Star) });
        var barra = new Border
        {
            Background = (Brush)FindResource(canal.Estado switch
            {
                Severity.Bad => "BBad",
                Severity.Warn => "BWarn",
                _ => "BOk"
            }),
            CornerRadius = new CornerRadius(5)
        };
        Grid.SetColumn(barra, 0);
        relleno.Children.Add(barra);
        pista.Child = relleno;
        Grid.SetColumn(pista, 1);
        fila.Children.Add(pista);

        var marcas = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (canal.Propio) marcas.Children.Add(Chip("tu canal"));
        else if (_analisis != null && canal.Canal == _analisis.Recomendado) marcas.Children.Add(Chip("sugerido"));
        Grid.SetColumn(marcas, 2);
        fila.Children.Add(marcas);

        string redes = canal.PuntosDeAcceso == 0 ? "sin redes"
            : canal.PuntosDeAcceso == 1 ? $"1 red · {canal.Senal}"
            : $"{canal.PuntosDeAcceso} redes · {canal.Senal}";
        var dato = new TextBlock
        {
            Text = redes,
            Style = (Style)FindResource(canal.PuntosDeAcceso == 0 ? "Dim" : "Body"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(10, 0, 0, 0)
        };
        Grid.SetColumn(dato, 3);
        fila.Children.Add(dato);

        // Un lector de pantalla no ve barras: la misma información tiene que
        // estar en texto.
        AutomationProperties.SetName(fila, $"Canal {canal.Canal}: {redes}" + (canal.Propio ? ", es tu canal" : ""));
        return fila;
    }

    private UIElement Chip(string texto)
    {
        var chip = new Border { Style = (Style)FindResource("MiniChip"), Margin = new Thickness(0, 0, 6, 0) };
        chip.Child = new TextBlock { Text = texto, Style = (Style)FindResource("MiniChipText") };
        return chip;
    }

    private static TextBlock Texto(string texto, string estilo) => new()
    {
        Text = texto,
        Style = (Style)Application.Current.FindResource(estilo),
        TextWrapping = TextWrapping.Wrap
    };
}
