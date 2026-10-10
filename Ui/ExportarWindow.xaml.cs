using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using SysDiag.Core;
using SysDiag.Core.Diagnostics;
using SysDiag.Models;

namespace SysDiag.Ui;

/// <summary>
/// Elección de formato al exportar.
///
/// Antes había un solo botón, «Generar informe», que producía HTML con todo:
/// nombre del equipo, nombre del usuario, ruta del perfil, número de serie y
/// el SSID de la red de la casa. Para quedárselo está perfecto; para pegarlo en
/// un foro, no. Y el README lo único que decía era «editalo a mano antes de
/// compartirlo», que nadie hace.
///
/// Ofrecer la versión redactada al lado de la completa convierte una recomendación
/// escrita en una decisión de un clic.
/// </summary>
public partial class ExportarWindow : Window
{
    private enum Formato { Html, HtmlRedactado, Markdown, MarkdownRedactado, Json }

    private readonly List<(RadioButton Boton, Formato Valor)> _opciones = new();
    private readonly DiagnosticReport _reporte;

    /// <summary>Archivo generado, o null si se canceló.</summary>
    public string ArchivoGenerado { get; private set; }

    public ExportarWindow(DiagnosticReport reporte)
    {
        _reporte = reporte ?? throw new ArgumentNullException(nameof(reporte));
        InitializeComponent();
        // Se ajusta antes de cualquier otra cosa: si la pantalla es más chica
        // que el alto declarado en el XAML, el pie de la ventana quedaría fuera
        // del área de trabajo y no habría cómo arrastrarla de vuelta.
        Ventana.AjustarAPantalla(this);
        ConstruirOpciones();
    }

    private void ConstruirOpciones()
    {
        Agregar("html", "Informe HTML", "Para abrir en el navegador o archivar.", Formato.Html,
            "Incluye todos los datos medidos, sin ocultar nada.");
        Agregar("html-redactado", "Informe HTML redactado",
            "Para mandar a soporte o publicar en un foro.", Formato.HtmlRedactado,
            "Oculta el nombre del equipo, el del usuario, las rutas del perfil, los números de serie, " +
            "los nombres de red Wi-Fi y las direcciones MAC. Dos redes distintas siguen siendo distinguibles, " +
            "así que un problema de solapamiento de canales se sigue pudiendo ver.");
        Agregar("markdown", "Markdown", "Para pegar en un foro o un ticket.", Formato.Markdown,
            "Resumen con puntaje, alcance, hallazgos, recomendaciones y las tablas principales.");
        Agregar("markdown-redactado", "Markdown redactado", "Lo mismo, sin datos identificables.",
            Formato.MarkdownRedactado, "Igual que el Markdown, con la misma redacción que el HTML redactado.");
        Agregar("json", "JSON completo", "Todos los datos, para analizar por otro programa.", Formato.Json,
            "Úsalo para comparar dos diagnósticos o para mandárselo a alguien que vaya a procesarlo.");
    }

    private void Agregar(string id, string titulo, string subtitulo, Formato valor, string detalle)
    {
        var opcion = new RadioButton
        {
            GroupName = "FormatoExportar",
            Tag = valor,
            Margin = new Thickness(0, 0, 0, 10),
            IsChecked = _opciones.Count == 0
        };

        var contenido = new Grid();
        contenido.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        contenido.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var punto = new Border
        {
            Width = 8, Height = 8, CornerRadius = new CornerRadius(4),
            Background = (System.Windows.Media.Brush)Application.Current.Resources["BStroke"],
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 12, 0)
        };
        Grid.SetColumn(punto, 0);
        contenido.Children.Add(punto);

        var textos = new StackPanel();
        textos.Children.Add(new TextBlock
        {
            Text = titulo,
            Style = (Style)FindResource("Body"),
            FontWeight = FontWeights.SemiBold
        });
        textos.Children.Add(new TextBlock
        {
            Text = subtitulo,
            Style = (Style)FindResource("Dim"),
            FontSize = 12,
            Margin = new Thickness(0, 2, 0, 0)
        });
        textos.Children.Add(new TextBlock
        {
            Text = detalle,
            Style = (Style)FindResource("Dim"),
            FontSize = 12,
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });
        Grid.SetColumn(textos, 1);
        contenido.Children.Add(textos);

        opcion.Content = contenido;
        // El Tag lleva el enum y no un texto: un `switch` sobre cadenas sería
        // un quinto duplicado de la misma lista, y de los que quedan
        // desincronizados en cuanto se agrega un formato.
        opcion.Checked += (_, _) =>
        {
            if (opcion.IsChecked != true) return;
            punto.Background = (System.Windows.Media.Brush)Application.Current.Resources["BAccent"];
        };
        opcion.Unchecked += (_, _) =>
            punto.Background = (System.Windows.Media.Brush)Application.Current.Resources["BStroke"];

        if (opcion.IsChecked == true)
            punto.Background = (System.Windows.Media.Brush)Application.Current.Resources["BAccent"];

        _opciones.Add((opcion, valor));
        PanelFormatos.Children.Add(opcion);
    }

    private void Generar_Click(object sender, RoutedEventArgs e)
    {
        Formato elegido = Formato.Html;
        foreach (var (boton, valor) in _opciones)
            if (boton.IsChecked == true) elegido = valor;

        BtnGenerar.IsEnabled = false;
        try
        {
            string archivo = elegido switch
            {
                Formato.Html => ReportBuilder.Build(_reporte),
                Formato.HtmlRedactado => ReportBuilder.Build(Redactor.Aplicar(_reporte)),
                Formato.Markdown => MarkdownReport.Build(_reporte),
                Formato.MarkdownRedactado => MarkdownReport.Build(Redactor.Aplicar(_reporte)),
                _ => Exporter.ToJson(_reporte)
            };

            ArchivoGenerado = archivo;
            DialogResult = true;

            // El HTML y el Markdown se abren: son para leer. El JSON no: es
            // para procesarlo, y abrirlo en el navegador no dice nada útil.
            if (elegido is Formato.Html or Formato.HtmlRedactado or Formato.Markdown or Formato.MarkdownRedactado)
                Process.Start(new ProcessStartInfo(archivo) { UseShellExecute = true })?.Dispose();
            else
            {
                Close();
                Dialog.Info("Diagnóstico exportado", archivo);
                return;
            }
            Close();
        }
        catch (Exception ex)
        {
            Dialog.Error("No se pudo exportar", ex.Message);
        }
        finally
        {
            BtnGenerar.IsEnabled = true;
        }
    }

    private void Cerrar_Click(object sender, RoutedEventArgs e) => Close();
}
