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

    internal void Configurar(string titulo, string cuerpo, string aceptar, bool conCancelar, string acentoKey)
    {
        Titulo.Text = titulo;
        Cuerpo.Text = cuerpo;
        BtnAceptar.Content = aceptar;
        BtnCancelar.Visibility = conCancelar ? Visibility.Visible : Visibility.Collapsed;
        Acento.Fill = (Brush)Application.Current.Resources[acentoKey];
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

    private static bool Mostrar(string titulo, string cuerpo, string aceptar, bool conCancelar, string acento)
    {
        var w = new DialogWindow();
        var owner = Owner;
        if (owner != null && owner != w) w.Owner = owner;

        w.Configurar(titulo, cuerpo, aceptar, conCancelar, acento);
        return w.ShowDialog() == true;
    }

    public static void Info(string titulo, string cuerpo)
        => Mostrar(titulo, cuerpo, "Entendido", false, "BAccent");

    public static void Error(string titulo, string cuerpo)
        => Mostrar(titulo, cuerpo, "Entendido", false, "BBad");

    public static bool Confirm(string titulo, string cuerpo, string aceptar)
        => Mostrar(titulo, cuerpo, aceptar, true, "BWarn");
}
