using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SysDiag.Core;
using SysDiag.Core.Diagnostics;
using SysDiag.Models;

namespace SysDiag.Ui;

/// <summary>
/// Comparación de dos diagnósticos guardados.
///
/// Es la pantalla que faltaba para que el historial sirviera de algo más que de
/// archivo: los datos ya se guardaban, pero solo se podían mirar de a uno, y
/// «¿mejoró o empeoró?» exigía acordarse de lo que decía el anterior. Después
/// de aplicar un arreglo, esta es la forma de saber si sirvió.
///
/// La carga de los JSON se hace en segundo plano: un historial de sesenta
/// diagnósticos son decenas de megabytes, y leerlos en el hilo de la interfaz
/// congela la ventana durante un par de segundos.
/// </summary>
public partial class CompararWindow : Window
{
    private List<Exporter.EntradaHistorial> _entradas = new();

    public CompararWindow()
    {
        InitializeComponent();
        // Se ajusta antes de cualquier otra cosa: si la pantalla es más chica
        // que el alto declarado en el XAML, el pie de la ventana quedaría fuera
        // del área de trabajo y no habría cómo arrastrarla de vuelta.
        Ventana.AjustarAPantalla(this);
        Cargar();
    }

    private void Cargar()
    {
        // Más reciente primero, que es el orden en que se guardan y en que se
        // busca: casi siempre se quiere comparar lo último contra algo.
        _entradas = Exporter.Listar(60);

        foreach (var entrada in _entradas)
        {
            string texto = $"{entrada.Fecha:dd-MM-yyyy HH:mm}  ·  {entrada.Puntaje}/100  ·  {Cobertura(entrada)}";
            ComboAnterior.Items.Add(new ComboBoxItem { Content = texto, Tag = entrada.Archivo });
            ComboReciente.Items.Add(new ComboBoxItem { Content = texto, Tag = entrada.Archivo });
        }

        if (_entradas.Count < 2)
        {
            MostrarAviso("Se necesitan al menos dos diagnósticos guardados para comparar. " +
                         "Corrí un diagnóstico completo, esperá a que cambie algo y volvé.");
            BtnComparar.IsEnabled = false;
            return;
        }

        // Por defecto: los dos más recientes. Es la comparación que alguien
        // abre esta ventana a buscar, y ahorra dos clics.
        ComboAnterior.SelectedIndex = 1;
        ComboReciente.SelectedIndex = 0;
        BtnComparar.IsEnabled = true;
    }

    private static string Cobertura(Exporter.EntradaHistorial entrada)
    {
        if (entrada.Modulos == null || entrada.Modulos.Length == 0) return "sin registrar";
        var nombres = entrada.Modulos
            .Select(m => DiagnosticReport.NombresModulos.TryGetValue(m, out var n) ? n : m)
            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return nombres.Count >= 8 ? "completo" : string.Join(", ", nombres);
    }

    private void Seleccion_Cambiada(object sender, SelectionChangedEventArgs e) => LimpiarAviso();

    private void Invertir_Click(object sender, RoutedEventArgs e)
    {
        int a = ComboAnterior.SelectedIndex;
        ComboAnterior.SelectedIndex = ComboReciente.SelectedIndex;
        ComboReciente.SelectedIndex = a;
        if (BtnComparar.IsEnabled) Comparar_Click(sender, e);
    }

    private async void Comparar_Click(object sender, RoutedEventArgs e)
    {
        if (ComboAnterior.SelectedItem is not ComboBoxItem { Tag: string archivoAnterior }) return;
        if (ComboReciente.SelectedItem is not ComboBoxItem { Tag: string archivoReciente }) return;
        if (string.Equals(archivoAnterior, archivoReciente, StringComparison.OrdinalIgnoreCase))
        {
            MostrarAviso("Elegí dos diagnósticos distintos.");
            return;
        }

        BtnComparar.IsEnabled = false;
        PanelResultado.Children.Clear();
        PanelResultado.Children.Add(new TextBlock
        {
            Text = "Leyendo los dos diagnósticos…",
            Style = (Style)FindResource("Dim")
        });

        try
        {
            var par = await Task.Run(() => (
                Anterior: Exporter.Cargar(archivoAnterior),
                Reciente: Exporter.Cargar(archivoReciente)));

            var diff = DiffEngine.Comparar(par.Anterior, par.Reciente);
            Render(diff);
        }
        catch (Exception ex)
        {
            PanelResultado.Children.Clear();
            MostrarAviso($"No se pudieron leer los diagnósticos: {ex.Message}");
        }
        finally
        {
            BtnComparar.IsEnabled = true;
        }
    }

    private void Render(ReportDiff diff)
    {
        PanelResultado.Children.Clear();

        // --- Cobertura ------------------------------------------------------
        // Se dice antes que nada. Comparar un «Red» suelto con un completo da
        // un puntaje que bajó sin que nada empeorara, y si eso no se aclara la
        // lectura es exactamente la contraria.
        if (!diff.MismaCobertura)
        {
            var aviso = new Border
            {
                Style = (Style)FindResource("CardOutline"),
                BorderBrush = (Brush)FindResource("BWarn"),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 16)
            };
            var textos = new StackPanel();
            textos.Children.Add(new TextBlock
            {
                Text = "Los dos diagnósticos no midieron lo mismo",
                Style = (Style)FindResource("Body"),
                FontWeight = FontWeights.SemiBold
            });
            textos.Children.Add(new TextBlock
            {
                Text = $"Anterior: {diff.CoberturaAnterior}.{Environment.NewLine}Reciente: {diff.CoberturaActual}.",
                Style = (Style)FindResource("Dim"), Margin = new Thickness(0, 4, 0, 0)
            });
            textos.Children.Add(new TextBlock
            {
                Text = "La diferencia de puntaje puede ser de cobertura y no del equipo. " +
                       "Para comparar de verdad, elegí dos diagnósticos del mismo tipo.",
                Style = (Style)FindResource("Dim"), TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            });
            aviso.Child = textos;
            PanelResultado.Children.Add(aviso);
        }

        // --- Encabezado ------------------------------------------------------
        var cabecera = new Border
        {
            Style = (Style)FindResource("CardOutline"),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(0, 0, 0, 16)
        };
        var cabeceraTextos = new StackPanel();
        cabeceraTextos.Children.Add(new TextBlock
        {
            Text = $"{diff.FechaAnterior:dd-MM-yyyy HH:mm}  →  {diff.FechaActual:dd-MM-yyyy HH:mm}",
            Style = (Style)FindResource("Eyebrow")
        });
        cabeceraTextos.Children.Add(new TextBlock
        {
            Text = diff.PuntajeTexto(),
            Style = (Style)FindResource("H1"),
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = diff.DeltaPuntaje < 0 ? (Brush)FindResource("BBad")
                : diff.DeltaPuntaje > 0 ? (Brush)FindResource("BOk")
                : (Brush)FindResource("BText")
        });
        cabeceraTextos.Children.Add(new TextBlock
        {
            Text = diff.Resumen(),
            Style = (Style)FindResource("Dim"),
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        cabecera.Child = cabeceraTextos;
        PanelResultado.Children.Add(cabecera);

        if (diff.Vacio && diff.DeltaPuntaje == 0)
        {
            PanelResultado.Children.Add(new TextBlock
            {
                Text = "No se detectaron diferencias entre las dos fechas.",
                Style = (Style)FindResource("Dim"),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        // Empeoraron antes que nuevos: lo que ya se sabía y empeoró suele ser
        // más urgente que lo que apareció, porque ya venía avisado.
        Seccion("Empeoraron", diff.Empeoraron, "BBad", "Estos hallazgos ya existían y ahora son más graves.");
        Seccion("Nuevos", diff.Nuevos, "BWarn", "Aparecieron después de la medición anterior.");
        Seccion("Mejoraron", diff.Mejoraron, "BOk", "Siguen presentes, pero con menos severidad.");
        Seccion("Resueltos", diff.Resueltos, "BOk", "Estaban antes y ya no aparecen.");

        if (diff.Mediciones.Count > 0)
        {
            PanelResultado.Children.Add(Titulo("Mediciones que se movieron"));
            foreach (var m in diff.Mediciones)
                PanelResultado.Children.Add(FilaMedicion(m));
        }

        if (diff.Iguales.Count > 0)
        {
            PanelResultado.Children.Add(new TextBlock
            {
                Text = $"{diff.Iguales.Count} hallazgo(s) siguen exactamente igual.",
                Style = (Style)FindResource("Dim"),
                Margin = new Thickness(0, 16, 0, 0)
            });
        }
    }

    private void Seccion(string titulo, List<HallazgoComparado> lista, string color, string nota)
    {
        if (lista.Count == 0) return;

        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        panel.Children.Add(Titulo($"{titulo} ({lista.Count})"));

        if (!string.IsNullOrEmpty(nota))
            panel.Children.Add(new TextBlock
            {
                Text = nota,
                Style = (Style)FindResource("Dim"),
                FontSize = 12, Margin = new Thickness(0, 0, 0, 8)
            });

        foreach (var h in lista) panel.Children.Add(FilaHallazgo(h, color));
        PanelResultado.Children.Add(panel);
    }

    private UIElement Titulo(string texto) => new TextBlock
    {
        Text = texto.ToUpperInvariant(),
        Style = (Style)FindResource("SectionLabel"),
        Margin = new Thickness(0, 8, 0, 8)
    };

    private UIElement FilaHallazgo(HallazgoComparado h, string color)
    {
        var tarjeta = new Border
        {
            Style = (Style)FindResource("CardOutline"),
            Padding = new Thickness(14, 11, 14, 11),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var grilla = new Grid();
        grilla.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grilla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var barra = new Rectangle
        {
            Width = 4, RadiusX = 2, RadiusY = 2,
            Fill = (Brush)FindResource(color),
            Margin = new Thickness(0, 0, 12, 0)
        };
        Grid.SetColumn(barra, 0);
        grilla.Children.Add(barra);

        var textos = new StackPanel();

        var encabezado = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        encabezado.Children.Add(new TextBlock
        {
            Text = h.Etiqueta.ToUpperInvariant(),
            Style = (Style)FindResource("Eyebrow"),
            Foreground = (Brush)FindResource(color)
        });
        encabezado.Children.Add(new TextBlock
        {
            Text = $"  ·  {h.Area}",
            Style = (Style)FindResource("Eyebrow")
        });
        if (h.SeveridadAnterior.HasValue)
            encabezado.Children.Add(new TextBlock
            {
                Text = $"  ·  antes: {EtiquetaDe(h.SeveridadAnterior.Value)}",
                Style = (Style)FindResource("Eyebrow"),
                Foreground = (Brush)FindResource("BTextMuted")
            });
        textos.Children.Add(encabezado);

        textos.Children.Add(new TextBlock
        {
            Text = h.Message,
            Style = (Style)FindResource("Body"),
            TextWrapping = TextWrapping.Wrap
        });

        if (!string.IsNullOrWhiteSpace(h.Action))
            textos.Children.Add(new TextBlock
            {
                Text = h.Action,
                Style = (Style)FindResource("Dim"),
                FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });

        Grid.SetColumn(textos, 1);
        grilla.Children.Add(textos);
        tarjeta.Child = grilla;
        return tarjeta;
    }

    private UIElement FilaMedicion(MedicionComparada m)
    {
        var fila = new Border
        {
            Style = (Style)FindResource("CardOutline"),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 6)
        };

        var grilla = new Grid();
        grilla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grilla.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grilla.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grilla.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var textos = new StackPanel();
        textos.Children.Add(new TextBlock { Text = m.Concepto, Style = (Style)FindResource("Body") });
        if (!string.IsNullOrWhiteSpace(m.Nota))
            textos.Children.Add(new TextBlock
            {
                Text = m.Nota,
                Style = (Style)FindResource("Dim"),
                FontSize = 12, Margin = new Thickness(0, 2, 0, 0)
            });
        Grid.SetColumn(textos, 0);
        grilla.Children.Add(textos);

        var antes = new TextBlock
        {
            Text = m.Antes,
            Style = (Style)FindResource("Dim"),
            FontFamily = (FontFamily)FindResource("FMono"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        Grid.SetColumn(antes, 1);
        grilla.Children.Add(antes);

        var flecha = new TextBlock
        {
            Text = "→",
            Style = (Style)FindResource("Dim"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0)
        };
        Grid.SetColumn(flecha, 2);
        grilla.Children.Add(flecha);

        var despues = new TextBlock
        {
            Text = m.Despues,
            Style = (Style)FindResource("Body"),
            FontFamily = (FontFamily)FindResource("FMono"),
            // El color dice hacia dónde fue el cambio sin tener que leer los dos
            // números y compararlos: es lo que se busca en esta pantalla.
            Foreground = (Brush)FindResource(m.Peor ? "BBad" : "BOk"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(despues, 3);
        grilla.Children.Add(despues);

        fila.Child = grilla;
        return fila;
    }

    private static string EtiquetaDe(Severity s) => s switch
    {
        Severity.Bad => "Crítico",
        Severity.Warn => "Atención",
        _ => "Correcto"
    };

    private void MostrarAviso(string texto)
    {
        TxtAviso.Text = texto;
        TxtAviso.Visibility = Visibility.Visible;
    }

    private void LimpiarAviso() => TxtAviso.Visibility = Visibility.Collapsed;

    private void Cerrar_Click(object sender, RoutedEventArgs e) => Close();
}
