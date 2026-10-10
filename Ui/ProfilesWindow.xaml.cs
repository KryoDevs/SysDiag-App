using System;
using System.Windows;
using SysDiag.Core;
using SysDiag.Core.Windows;
using SysDiag.Models;

namespace SysDiag.Ui;

public partial class ProfilesWindow : Window
{
    private bool _applying;
    public ProfilesWindow()
    {
        InitializeComponent();
        // Se ajusta antes de cualquier otra cosa: si la pantalla es más chica
        // que el alto declarado en el XAML, el pie de la ventana quedaría fuera
        // del área de trabajo y no habría cómo arrastrarla de vuelta.
        Ventana.AjustarAPantalla(this);
        MouseLeftButtonDown += (_, _) => DragMove();
        Closing += (_, e) => { if (_applying) e.Cancel = true; };
    }

    private void Cerrar_Click(object sender, RoutedEventArgs e) => Close();

    private async void Universidad_Click(object sender, RoutedEventArgs e) => await Aplicar("Universidad — Silencioso",
        new OptimizeModule.Options
        {
            FlushDns = true,
            FlushArp = true,
            FixWlanAutoconfig = true,
            WifiPowerSave = true,
            WifiMaxPerformance = false,
            HighPerformancePlan = false,
            VisualEffects = true,
            GameMode = false,
            CpuMaxPercent = 60
        });

    private async void Trabajo_Click(object sender, RoutedEventArgs e) => await Aplicar("Trabajo — Equilibrado",
        new OptimizeModule.Options
        {
            FlushDns = true,
            FlushArp = true,
            FixWlanAutoconfig = true,
            WifiPowerSave = false,
            WifiMaxPerformance = false,
            HighPerformancePlan = false,
            VisualEffects = false,
            PublicDns = true,
            GameMode = false,
            CpuMaxPercent = 85
        });

    private async void Juego_Click(object sender, RoutedEventArgs e) => await Aplicar("Juego — Rendimiento",
        new OptimizeModule.Options
        {
            FlushDns = true,
            FlushArp = true,
            FixWlanAutoconfig = true,
            WifiMaxPerformance = true,
            WifiPowerSave = false,
            HighPerformancePlan = true,
            VisualEffects = false,
            GameMode = true,
            CpuMaxPercent = 100
        });

    private async Task Aplicar(string nombre, OptimizeModule.Options opciones)
    {
        if (_applying) return;
        if (!AppEnv.IsAdmin)
        {
            bool elevar = Dialog.Confirm("Se necesitan permisos de administrador",
                $"Aplicar el perfil «{nombre}» cambia configuración del sistema.",
                "Reiniciar como administrador");

            if (elevar && AppEnv.RelaunchElevated()) Application.Current.Shutdown();
            return;
        }

        bool ok = Dialog.Confirm($"Aplicar perfil «{nombre}»",
            "Se conserva el primer estado respaldado, no el perfil intermedio. «Restaurar estado» revierte solo los ajustes respaldados; vaciar cachés no es reversible.",
            "Aplicar");
        if (!ok) return;

        _applying = true;
        IsEnabled = false;
        try
        {
            await Task.Run(() => OptimizeModule.Run(new DiagnosticReport(), opciones));
            _applying = false;
            IsEnabled = true;
            Dialog.Info("Perfil aplicado", $"«{nombre}» está activo.");
            Close();
        }
        catch (Exception ex)
        {
            Dialog.Error("No se pudo aplicar el perfil", ex.Message);
        }
        finally { _applying = false; IsEnabled = true; }
    }
}
