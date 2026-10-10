using System;
using System.Windows;
using SysDiag.Core.Windows;

namespace SysDiag.Ui;

public partial class OptimizeWindow : Window
{
    public OptimizeModule.Options Options { get; } = new();

    public OptimizeWindow()
    {
        InitializeComponent();
        // Se ajusta antes de cualquier otra cosa: si la pantalla es más chica
        // que el alto declarado en el XAML, el pie de la ventana quedaría fuera
        // del área de trabajo y no habría cómo arrastrarla de vuelta.
        Ventana.AjustarAPantalla(this);
        MouseLeftButtonDown += (_, _) => DragMove();
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

    private void Aplicar_Click(object sender, RoutedEventArgs e)
    {
        Options.FlushDns = ChkDns.IsChecked == true;
        Options.FlushArp = ChkArp.IsChecked == true;
        Options.WifiMaxPerformance = ChkWifi.IsChecked == true;
        Options.WifiPowerSave = ChkWifiPowerSave.IsChecked == true;
        Options.CpuMaxPercent = ChkCpuMax.IsChecked == true ? 70 : null;
        Options.PublicDns = ChkDnsPublico.IsChecked == true;
        Options.VisualEffects = ChkEfectos.IsChecked == true;
        Options.FixWlanAutoconfig = ChkWlan.IsChecked == true;
        Options.HighPerformancePlan = ChkPlan.IsChecked == true;
        Options.ResetTcpStack = ChkReset.IsChecked == true;

        try { Options.Validate(); }
        catch (ArgumentException ex) { Dialog.Error("Opciones incompatibles", ex.Message); return; }

        // El reinicio TCP/IP no es restaurable por el respaldo de SysDiag: se confirma aparte.
        if (Options.ResetTcpStack)
        {
            bool ok = Dialog.Confirm("Confirmar reinicio de la pila de red",
                "Se borrará la configuración manual de IP, DNS y VPN. Los cambios no surten " +
                "efecto hasta reiniciar el equipo. SysDiag NO los revierte: tendrás que reconfigurar IP fija, rutas y VPN manualmente. Se exige un punto de restauración antes de ejecutar.",
                "Reiniciar la pila");

            if (!ok) return;
        }

        DialogResult = true;
        Close();
    }
}
