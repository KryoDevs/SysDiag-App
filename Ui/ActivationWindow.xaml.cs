using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using SysDiag.Core.Licensing;

namespace SysDiag.Ui;

/// <summary>
/// Diálogo de activación: muestra el estado de la licencia y valida el código
/// que escribe el usuario. La verificación es local (LicenseCrypto); no hay
/// ninguna llamada de red salvo el enlace de compra, que abre el navegador.
/// </summary>
public partial class ActivationWindow : Window
{
    private const string UrlCompra = "https://github.com/KryoDevs/SysDiag-App";

    public ActivationWindow()
    {
        InitializeComponent();
        // Se ajusta antes de cualquier otra cosa: si la pantalla es más chica
        // que el alto declarado en el XAML, el pie de la ventana quedaría fuera
        // del área de trabajo y no habría cómo arrastrarla de vuelta.
        Ventana.AjustarAPantalla(this);
        ActualizarEstado();
    }

    private void ActualizarEstado()
    {
        switch (LicenseService.Estado)
        {
            case EstadoLicencia.Pro:
                TxtEstado.Text = "SysDiag Pro activado";
                TxtEstado.Foreground = Res("BOk");
                TxtDetalle.Text = "Todas las funciones están habilitadas en este equipo. El código se verifica sin conexión y se guarda solo aquí.";
                BtnActivar.Content = "Validar otro código";
                break;

            case EstadoLicencia.Prueba:
                TxtEstado.Text = $"Prueba gratuita · {LicenseService.DiasPruebaRestantes} día(s) restantes";
                TxtEstado.Foreground = Res("BAccent");
                TxtDetalle.Text = "Durante la prueba todas las funciones están habilitadas. Al terminar, el diagnóstico sigue libre y las acciones que modifican el equipo piden un código.";
                break;

            default:
                TxtEstado.Text = "La prueba ha terminado";
                TxtEstado.Foreground = Res("BWarn");
                TxtDetalle.Text = "Puedes seguir diagnosticando y consultando el equipo sin límite. Para optimizar, instalar, limpiar o reparar, activa con un código.";
                break;
        }
    }

    private void Activar_Click(object sender, RoutedEventArgs e)
    {
        string codigo = TxtCodigo.Text == null ? "" : TxtCodigo.Text.Trim();
        if (codigo.Length == 0)
        {
            MostrarError("Escribe el código de activación que recibiste al comprarlo.");
            return;
        }
        if (LicenseService.Activar(codigo))
        {
            TxtError.Visibility = Visibility.Collapsed;
            ActualizarEstado();
            Dialog.Info("Activación completada", "SysDiag Pro quedó activado en este equipo.");
        }
        else
        {
            MostrarError(string.IsNullOrEmpty(LicenseService.UltimoError)
                ? "El código no es válido."
                : LicenseService.UltimoError);
        }
    }

    private void MostrarError(string mensaje)
    {
        TxtError.Text = mensaje;
        TxtError.Visibility = Visibility.Visible;
    }

    private void Comprar_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(UrlCompra) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Dialog.Error("No se pudo abrir el navegador", "Visita " + UrlCompra + " para comprar un código de activación.");
        }
    }

    private void Cerrar_Click(object sender, RoutedEventArgs e) => Close();

    private static Brush Res(string clave) => (Brush)Application.Current.Resources[clave];
}
