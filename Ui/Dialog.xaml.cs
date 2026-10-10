using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace SysDiag.Ui;

public partial class DialogWindow : Window
{
    public DialogWindow()
    {
        InitializeComponent();
        // Se ajusta antes de cualquier otra cosa: si la pantalla es más chica
        // que el alto declarado en el XAML, el pie de la ventana quedaría fuera
        // del área de trabajo y no habría cómo arrastrarla de vuelta.
        Ventana.AjustarAPantalla(this);
        MouseLeftButtonDown += (_, _) => DragMove();
    }

    private void Aceptar_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
    private void Cancelar_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

    internal void Configurar(string titulo, string cuerpo, string aceptar, bool conCancelar, string acentoKey,
        bool monoespaciado = false)
    {
        Titulo.Text = titulo;
        Cuerpo.Text = cuerpo;
        BtnAceptar.Content = aceptar;
        BtnCancelar.Visibility = conCancelar ? Visibility.Visible : Visibility.Collapsed;
        Acento.Fill = (Brush)Application.Current.Resources[acentoKey];
        // Un plan de cambios o un listado de claves en tipografía
        // proporcional pierde su gracia: las columnas no alinean y lo que se
        // lee de un vistazo pasa a leerse línea por línea.
        Cuerpo.FontFamily = monoespaciado ? (System.Windows.Media.FontFamily)Application.Current.Resources["FMono"] : null;
        Cuerpo.FontSize = monoespaciado ? 12 : 0;
        Cuerpo.TextWrapping = monoespaciado ? TextWrapping.NoWrap : TextWrapping.Wrap;
    }
}

/// <summary>
/// Diálogos propios en vez de MessageBox: el del sistema se dibuja en claro y
/// rompe el conjunto. Mismo lenguaje visual que el resto de la aplicación.
/// </summary>
public static class Dialog
{
    private static Window Owner =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);

    private static bool Mostrar(string titulo, string cuerpo, string aceptar, bool conCancelar, string acento,
        bool monoespaciado = false)
    {
        var w = new DialogWindow();
        var owner = Owner;
        if (owner != null && owner != w) w.Owner = owner;

        w.Configurar(titulo, cuerpo, aceptar, conCancelar, acento, monoespaciado);
        return w.ShowDialog() == true;
    }

    /// <summary>
    /// Cuerpo tabular: planes de cambio, listas de claves, diferencias entre
    /// dos diagnósticos. Sin sangría proporcional, las columnas alinean y lo
    /// que se compara se compara de un vistazo.
    /// </summary>
    public static void Detalle(string titulo, string cuerpo, string aceptar = "Entendido")
        => Mostrar(titulo, cuerpo, aceptar, false, "BAccent", monoespaciado: true);

    public static void Info(string titulo, string cuerpo)
        => Mostrar(titulo, cuerpo, "Entendido", false, "BAccent");

    public static void Error(string titulo, string cuerpo)
        => Mostrar(titulo, cuerpo, "Entendido", false, "BBad");

    public static bool Confirm(string titulo, string cuerpo, string aceptar)
        => Mostrar(titulo, cuerpo, aceptar, true, "BWarn");
}
