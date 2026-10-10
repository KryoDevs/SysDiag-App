using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SysDiag.Core;
using SysDiag.Core.Licensing;
using SysDiag.Core.Windows;

namespace SysDiag.Ui;

/// <summary>
/// Ajustes de Windows 10/11: catálogo agrupado por intención, con búsqueda,
/// riesgo visible por fila, estado actual y reversión individual o total.
/// Escribir solo ocurre al pulsar «Aplicar seleccionados» y siempre después
/// de guardar el estado anterior (ver <see cref="TweakModule"/>).
/// </summary>
public partial class TweaksWindow : Window
{
    private readonly Dictionary<CheckBox, TweakAjuste> _filas = new();
    private HashSet<string> _conRespaldo = new();

    public TweaksWindow()
    {
        InitializeComponent();
        // Se ajusta antes de cualquier otra cosa: si la pantalla es más chica
        // que el alto declarado en el XAML, el pie de la ventana quedaría fuera
        // del área de trabajo y no habría cómo arrastrarla de vuelta.
        Ventana.AjustarAPantalla(this);
        Render();
    }

    // ---- Pintado -----------------------------------------------------------

    private void Render()
    {
        PanelAjustes.Children.Clear();
        _filas.Clear();
        _conRespaldo = new HashSet<string>(TweakModule.ConRespaldo(), StringComparer.OrdinalIgnoreCase);

        string filtro = (TxtBuscar.Text ?? "").Trim();
        var visibles = TweakModule.Catalogo
            .Where(a => filtro.Length == 0
                || a.Titulo.Contains(filtro, StringComparison.OrdinalIgnoreCase)
                || a.Descripcion.Contains(filtro, StringComparison.OrdinalIgnoreCase)
                || a.Categoria.Contains(filtro, StringComparison.OrdinalIgnoreCase))
            .ToList();

        string categoriaActual = null;
        foreach (var ajuste in visibles)
        {
            if (ajuste.Categoria != categoriaActual)
            {
                categoriaActual = ajuste.Categoria;
                PanelAjustes.Children.Add(CabeceraCategoria(categoriaActual, visibles.Count(a => a.Categoria == categoriaActual)));
            }
            PanelAjustes.Children.Add(CrearFila(ajuste));
        }

        if (visibles.Count == 0)
        {
            PanelAjustes.Children.Add(new TextBlock
            {
                Text = "Ningún ajuste coincide con la búsqueda.",
                Style = (Style)FindResource("Dim"),
                Margin = new Thickness(2, 18, 0, 0)
            });
        }

        ActualizarResumen();
    }

    private void ActualizarResumen()
    {
        int aplicados = TweakModule.Aplicados().Count;
        int pendientes = _conRespaldo.Count;
        TxtResumen.Text = $"{aplicados} de {TweakModule.Catalogo.Count} ajustes activos · {pendientes} con cambios guardados por si quieres revertir";
    }

    private UIElement CabeceraCategoria(string categoria, int cantidad)
    {
        var fila = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 16, 0, 8) };
        fila.Children.Add(new TextBlock
        {
            Text = categoria.ToUpperInvariant(),
            Style = (Style)FindResource("SectionLabel")
        });
        fila.Children.Add(new TextBlock
        {
            Text = $"  ·  {cantidad}",
            Style = (Style)FindResource("Eyebrow")
        });
        return fila;
    }

    private UIElement CrearFila(TweakAjuste ajuste)
    {
        bool aplicado = TweakModule.EstaAplicado(ajuste);
        Brush riesgo = ajuste.Riesgo switch
        {
            "Alto" => Res("BBad"),
            "Medio" => Res("BWarn"),
            _ => Res("BOk")
        };

        var contenido = new StackPanel();

        // Título + chips de estado del ajuste
        var filaTitulo = new Grid();
        filaTitulo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filaTitulo.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titulo = new TextBlock
        {
            Text = ajuste.Titulo,
            Style = (Style)FindResource("Body"),
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(titulo, 0);
        filaTitulo.Children.Add(titulo);

        var chips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (ajuste.SoloWin11) chips.Children.Add(Chip("Win11", Res("BTextMuted")));
        if (ajuste.RequiereAdmin) chips.Children.Add(Chip("Admin", Res("BTextMuted")));
        if (ajuste.Riesgo != "Bajo") chips.Children.Add(Chip(ajuste.Riesgo, riesgo));
        chips.Children.Add(Chip(aplicado ? "Aplicado" : "Sin aplicar", aplicado ? Res("BOk") : Res("BTextMuted")));
        Grid.SetColumn(chips, 1);
        filaTitulo.Children.Add(chips);

        contenido.Children.Add(filaTitulo);

        contenido.Children.Add(new TextBlock
        {
            Text = ajuste.Descripcion,
            Style = (Style)FindResource("Dim"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0)
        });

        if (!string.IsNullOrEmpty(ajuste.NotaAplicacion))
        {
            contenido.Children.Add(new TextBlock
            {
                Text = ajuste.NotaAplicacion,
                Style = (Style)FindResource("Eyebrow"),
                Margin = new Thickness(0, 5, 0, 0)
            });
        }

        if (_conRespaldo.Contains(ajuste.Id))
        {
            var deshacer = new Button
            {
                Content = "Deshacer este cambio",
                Style = (Style)FindResource("BtnQuiet"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 8, 0, 0),
                Padding = new Thickness(10, 5, 10, 5),
                FontSize = 11.5,
                Tag = ajuste.Id
            };
            deshacer.Click += Deshacer_Click;
            contenido.Children.Add(deshacer);
        }

        var check = new CheckBox
        {
            Style = (Style)FindResource("Opcion"),
            Tag = riesgo,
            Content = contenido
        };
        _filas[check] = ajuste;
        return check;
    }

    private static Border Chip(string texto, Brush color)
    {
        return new Border
        {
            Style = (Style)Application.Current.Resources["MiniChip"],
            Margin = new Thickness(6, 0, 0, 0),
            Background = Lavado(color),
            Child = new TextBlock
            {
                Text = texto,
                Style = (Style)Application.Current.Resources["MiniChipText"],
                Foreground = color
            }
        };
    }

    private static Brush Lavado(Brush baseBrush)
    {
        var c = ((SolidColorBrush)baseBrush).Color;
        var wash = new SolidColorBrush(Color.FromArgb(38, c.R, c.G, c.B));
        wash.Freeze();
        return wash;
    }

    private static Brush Res(string clave) => (Brush)Application.Current.Resources[clave];

    // ---- Acciones ----------------------------------------------------------

    private void Buscar_TextChanged(object sender, TextChangedEventArgs e) => Render();

    private void Seleccionar_Click(object sender, RoutedEventArgs e)
    {
        foreach (var par in _filas)
            if (!TweakModule.EstaAplicado(par.Value)) par.Key.IsChecked = true;
    }

    /// <summary>
    /// Ensayo: el plan completo, sin escribir nada. Es la primera de las dos
    /// preguntas que cualquiera se hace antes de tocar el registro —«qué me
    /// vas a cambiar»—, y la única que se puede responder sin riesgo.
    /// </summary>
    private void Ensayar_Click(object sender, RoutedEventArgs e)
    {
        var seleccion = _filas.Where(p => p.Key.IsChecked == true).Select(p => p.Value).ToList();
        if (seleccion.Count == 0)
        {
            Dialog.Info("Nada que ensayar", "Marca los ajustes que quieras revisar y vuelve a pulsar «Ensayar». No se escribe nada en ningún caso.");
            return;
        }

        var lineas = TweakModule.Ensayar(seleccion);
        int cambiarian = lineas.Count(l => !l.YaAplicado);
        int sinPermisos = lineas.Count(l => l.FaltanPermisos && !l.YaAplicado);

        string encabezado = cambiarian == 0
            ? $"Nada que cambiar: los {lineas.Count} valores revisados ya están como quedarían."
            : $"{cambiarian} de {lineas.Count} valores cambiarían.";
        if (sinPermisos > 0)
            encabezado += $" {sinPermisos} necesitan administrador y ahora mismo no lo hay: esos fallarían.";

        Dialog.Detalle("Ensayo — no se aplicó nada",
            encabezado + Environment.NewLine + Environment.NewLine +
            string.Join(Environment.NewLine, lineas.Select(l => l.Texto)));
    }

    private async void Aplicar_Click(object sender, RoutedEventArgs e)
    {
        var seleccion = _filas.Where(p => p.Key.IsChecked == true).Select(p => p.Value).ToList();
        if (seleccion.Count == 0)
        {
            Dialog.Info("Nada seleccionado", "Marca los ajustes que quieras activar y vuelve a pulsar «Aplicar seleccionados».");
            return;
        }

        if (!LicenseService.PuedeModificar)
        {
            new ActivationWindow { Owner = this }.ShowDialog();
            if (!LicenseService.PuedeModificar) return;
        }

        if (seleccion.Any(a => a.RequiereAdmin) && !AppEnv.IsAdmin)
        {
            bool elevar = Dialog.Confirm(
                "Se necesitan permisos de administrador",
                "Algunos ajustes seleccionados escriben en zonas protegidas de Windows. SysDiag puede reiniciarse como administrador para aplicarlos.",
                "Reiniciar como administrador");
            if (elevar) AppEnv.RelaunchElevated();
            return;
        }

        BtnAplicar.IsEnabled = false;
        BtnEnsayar.IsEnabled = false;
        try
        {
            var resultado = await Task.Run(() => TweakModule.Aplicar(seleccion));

            // Releído y comprobado, no supuesto. Si algo no quedó, se dice
            // primero y con el motivo: un «se aplicaron 6 ajustes» que incluye
            // dos que no se aplicaron es peor que no decir nada, porque cierra
            // la puerta a que el usuario lo descubra.
            // Cada ajuste que quedó aplicado entra en el historial por
            // separado: deshacer es por paso, y una sola entrada que agrupe
            // seis ajustes devolvería al usuario al problema del que venía
            // —o revierte todo o no revierte nada—.
            foreach (var (id, nombre) in resultado.AjustesAplicados)
                ActionLog.Registrar(OrigenCambio.Ajustes, nombre,
                    detalle: resultado.Aplicados.FirstOrDefault(l => l.StartsWith(nombre + ":", StringComparison.Ordinal)) ?? "",
                    referencia: id);

            string titulo = resultado.HayFallos ? "Aplicado, con cambios que no quedaron" : "Ajustes aplicados";
            if (resultado.HayFallos)
                Dialog.Detalle(titulo, resultado.Detalle() +
                    Environment.NewLine + Environment.NewLine +
                    "El estado anterior quedó guardado: puedes revertir lo que sí se aplicó desde aquí o desde «Restaurar estado».");
            else
                Dialog.Info(titulo,
                    $"{resultado.Resumen()}. El estado anterior quedó guardado: puedes revertirlo desde aquí o desde «Restaurar estado».");
        }
        catch (Exception ex)
        {
            Dialog.Error("No se pudieron aplicar los ajustes", ex.Message);
        }
        finally
        {
            BtnAplicar.IsEnabled = true;
            BtnEnsayar.IsEnabled = true;
            Render();
        }
    }

    private async void RevertirTodo_Click(object sender, RoutedEventArgs e)
    {
        if (_conRespaldo.Count == 0)
        {
            Dialog.Info("Nada que revertir", "Todavía no has aplicado ningún ajuste desde SysDiag.");
            return;
        }
        if (!Dialog.Confirm("Revertir todos los cambios",
                "Se restaurará el valor anterior de cada ajuste aplicado desde SysDiag. Es seguro: nada queda a medias.",
                "Revertir todo")) return;

        try
        {
            string resumen = await Task.Run(() => TweakModule.RevertirTodo());
            ActionLog.MarcarDeshechos(OrigenCambio.Ajustes, resumen);
            Dialog.Info("Cambios revertidos", resumen);
        }
        catch (Exception ex)
        {
            Dialog.Error("No se pudo revertir", ex.Message);
        }
        Render();
    }

    private void Deshacer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        try
        {
            string resumen = TweakModule.Revertir(id);
            // Se marca por id, no por título: dos ajustes pueden compartir
            // nombre en el historial si se aplicaron en días distintos.
            var entrada = ActionLog.Pendientes()
                .FirstOrDefault(c => c.Origen == OrigenCambio.Ajustes
                    && string.Equals(c.Referencia, id, StringComparison.OrdinalIgnoreCase));
            if (entrada != null) ActionLog.MarcarDeshecho(entrada.Id, resumen);
            Dialog.Info("Cambio deshecho", resumen);
        }
        catch (Exception ex)
        {
            Dialog.Error("No se pudo deshacer", ex.Message);
        }
        Render();
    }

    private void Cerrar_Click(object sender, RoutedEventArgs e) => Close();
}
