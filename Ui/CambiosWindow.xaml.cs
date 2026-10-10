using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SysDiag.Core;
using SysDiag.Core.Windows;

namespace SysDiag.Ui;

/// <summary>
/// Historial de lo que SysDiag cambió, con deshacer por paso.
///
/// Antes de esta ventana, revertir era una decisión de todo o nada: «Restaurar
/// estado» devolvía el conjunto completo, sin decir qué contenía. Quien aplicó
/// seis ajustes y quiere deshacer uno tenía que adivinar cuál era cuál desde la
/// ventana de ajustes. Acá cada paso tiene su propio botón, su fecha y su
/// alcance escrito.
///
/// Y lo que no se puede revertir aparece igual, marcado y con el motivo. Un
/// historial que solo listara lo reversible daría a entender —por omisión— que
/// todo lo que no aparece se puede deshacer.
/// </summary>
public partial class CambiosWindow : Window
{
    public CambiosWindow()
    {
        InitializeComponent();
        // Se ajusta antes de cualquier otra cosa: si la pantalla es más chica
        // que el alto declarado en el XAML, el pie de la ventana quedaría fuera
        // del área de trabajo y no habría cómo arrastrarla de vuelta.
        Ventana.AjustarAPantalla(this);
        Render();
    }

    private void Render()
    {
        PanelCambios.Children.Clear();

        var cambios = ActionLog.Todos();
        int pendientes = cambios.Count(c => c.SePuedeDeshacer);
        int irreversibles = cambios.Count(c => c.Pendiente && !c.Reversible);

        TxtResumen.Text = cambios.Count == 0
            ? "sin cambios registrados"
            : $"{cambios.Count} cambio(s) · {pendientes} reversibles pendientes" +
              (irreversibles > 0 ? $" · {irreversibles} sin vuelta atrás" : "");

        if (cambios.Count == 0)
        {
            PanelCambios.Children.Add(new TextBlock
            {
                Text = "SysDiag no ha aplicado ningún cambio en este equipo desde este registro. " +
                       "Cuando apliques ajustes, optimizaciones o una limpieza, aparecen aquí con su forma de volver atrás.",
                Style = (Style)FindResource("Dim"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 20, 0, 8)
            });
            return;
        }

        foreach (var cambio in cambios) PanelCambios.Children.Add(CrearFila(cambio));
    }

    private UIElement CrearFila(CambioAplicado cambio)
    {
        Brush acento = cambio.Deshecho ? Res("BTextMuted")
            : !cambio.Reversible ? Res("BWarn")
            : Res("BOk");

        var tarjeta = new Border
        {
            Style = (Style)FindResource("CardOutline"),
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(16, 14, 16, 14)
        };

        var grilla = new Grid();
        grilla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grilla.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var textos = new StackPanel();

        // Primera línea: fecha, origen y estado. El estado va arriba y no al
        // final porque es lo que decide si el botón de abajo existe: leer toda
        // la tarjeta para descubrir que no se puede deshacer es perder el
        // tiempo dos veces.
        var encabezado = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        encabezado.Children.Add(new TextBlock
        {
            Text = cambio.FechaTexto,
            Style = (Style)FindResource("Eyebrow"),
            Foreground = Res("BTextMuted")
        });
        encabezado.Children.Add(new TextBlock
        {
            Text = $"  ·  {cambio.OrigenTexto}",
            Style = (Style)FindResource("Eyebrow")
        });

        string estado = cambio.Deshecho ? "deshecho" : !cambio.Reversible ? "sin vuelta atrás" : "pendiente";
        var chip = new Border
        {
            Style = (Style)FindResource("Chip"),
            Background = acento,
            Opacity = 0.85,
            Margin = new Thickness(10, 0, 0, 0)
        };
        chip.Child = new TextBlock
        {
            Text = estado.ToUpperInvariant(),
            Style = (Style)FindResource("Eyebrow"),
            Foreground = Res("BBase")
        };
        encabezado.Children.Add(chip);
        textos.Children.Add(encabezado);

        textos.Children.Add(new TextBlock
        {
            Text = cambio.Titulo,
            Style = (Style)FindResource("Body"),
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });

        if (!string.IsNullOrWhiteSpace(cambio.Detalle))
            textos.Children.Add(new TextBlock
            {
                Text = cambio.Detalle,
                Style = (Style)FindResource("Dim"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });

        // La nota dice el alcance de la reversión o por qué no la hay. Va
        // siempre que exista, incluso en las entradas reversibles: deshacer una
        // optimización restaura el conjunto, y eso hay que saberlo antes.
        if (!string.IsNullOrWhiteSpace(cambio.Nota))
            textos.Children.Add(new TextBlock
            {
                Text = cambio.Nota,
                Style = (Style)FindResource("Dim"),
                Foreground = Res("BWarn"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
                FontSize = 12
            });

        if (cambio.Deshecho && !string.IsNullOrWhiteSpace(cambio.ResultadoDeshacer))
            textos.Children.Add(new TextBlock
            {
                Text = "Resultado: " + cambio.ResultadoDeshacer,
                Style = (Style)FindResource("Dim"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
                FontSize = 12
            });

        Grid.SetColumn(textos, 0);
        grilla.Children.Add(textos);

        var acciones = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(14, 0, 0, 0)
        };

        if (cambio.SePuedeDeshacer)
        {
            var boton = new Button
            {
                Content = "Deshacer",
                Style = (Style)FindResource("BtnQuiet"),
                Tag = cambio.Id,
                ToolTip = string.IsNullOrWhiteSpace(cambio.Nota)
                    ? "Devuelve este paso a su estado anterior. El resto de los cambios no se toca."
                    : cambio.Nota
            };
            boton.Click += Deshacer_Click;
            acciones.Children.Add(boton);
        }

        Grid.SetColumn(acciones, 1);
        grilla.Children.Add(acciones);
        tarjeta.Child = grilla;
        return tarjeta;
    }

    // ---- Acciones ----------------------------------------------------------

    /// <summary>
    /// Deshace un paso. La confirmación dice qué se va a revertir y advierte
    /// del alcance cuando la nota lo declara: deshacer una optimización
    /// restaura el conjunto completo, y enterarse después no sirve.
    /// </summary>
    private async void Deshacer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;

        var cambio = ActionLog.Todos().FirstOrDefault(c => c.Id == id);
        if (cambio == null) return;

        string alcance = string.IsNullOrWhiteSpace(cambio.Nota)
            ? "Solo se revierte este paso; los demás cambios quedan como están."
            : cambio.Nota;

        if (!Dialog.Confirm("Deshacer este cambio",
                $"«{cambio.Titulo}»{Environment.NewLine}{Environment.NewLine}{alcance}",
                "Deshacer")) return;

        ((Button)sender).IsEnabled = false;
        try
        {
            string resultado = await Task.Run(() => ActionLog.Deshacer(id));
            AppLog.Write(resultado, "OK");
            Dialog.Info("Cambio deshecho", resultado);
        }
        catch (Exception ex)
        {
            Dialog.Error("No se pudo deshacer", ex.Message);
        }
        finally
        {
            Render();
        }
    }

    private void Limpiar_Click(object sender, RoutedEventArgs e)
    {
        int pendientes = ActionLog.Pendientes().Count;
        if (pendientes == 0)
        {
            ActionLog.Limpiar();
            Render();
            return;
        }

        // Con cambios pendientes la confirmación tiene que decir lo que este
        // botón NO hace. «Limpiar historial» suena a deshacer, y no lo es.
        if (!Dialog.Confirm("Limpiar el historial de cambios",
                $"Hay {pendientes} cambio(s) registrado(s) como pendientes.{Environment.NewLine}{Environment.NewLine}" +
                "Esto borra la lista. NO revierte nada: lo que esté aplicado sigue aplicado, y perderás la referencia de cómo volver atrás.",
                "Borrar el historial")) return;

        ActionLog.Limpiar();
        Render();
    }

    private void Cerrar_Click(object sender, RoutedEventArgs e) => Close();

    private static Brush Res(string clave) => (Brush)Application.Current.Resources[clave];
}
