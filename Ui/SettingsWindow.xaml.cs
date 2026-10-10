using System;
using System.Windows;
using SysDiag.Core.Licensing;
using SysDiag.Core.Windows;
using SysDiag.Models;

namespace SysDiag.Ui;

public partial class SettingsWindow : Window
{
    private AppSettings _actual;

    public SettingsWindow()
    {
        InitializeComponent();
        MouseLeftButtonDown += (_, _) => DragMove();

        _actual = SettingsService.Cargar();
        Volcar(_actual);
        ActualizarLicencia();
    }

    private void ActualizarLicencia()
    {
        TxtLicencia.Text = LicenseService.EsPro
            ? "SysDiag Pro activado en este equipo."
            : LicenseService.Estado == EstadoLicencia.Prueba
                ? $"Prueba gratuita: {LicenseService.DiasPruebaRestantes} día(s) restantes."
                : "La prueba terminó: el diagnóstico sigue libre; las acciones que modifican el equipo piden un código.";
    }

    private void Licencia_Click(object sender, RoutedEventArgs e)
    {
        new ActivationWindow { Owner = this }.ShowDialog();
        ActualizarLicencia();
    }

    private void Volcar(AppSettings s)
    {
        TxtSample.Text = s.SampleSeconds.ToString();
        TxtPing.Text = s.PingCount.ToString();
        TxtEventDays.Text = s.EventDays.ToString();
        TxtWheaDays.Text = s.WheaDays.ToString();
        TxtHistorial.Text = s.HistorialMaximo.ToString();
        TxtLogs.Text = s.LogsMaximo.ToString();
    }

    private void Restablecer_Click(object sender, RoutedEventArgs e) => Volcar(AppSettings.PorDefecto());

    private void Cerrar_Click(object sender, RoutedEventArgs e) => Close();

    private void Guardar_Click(object sender, RoutedEventArgs e)
    {
        // Cada campo se acota a un rango razonable: nadie gana nada con un
        // muestreo de 0 segundos o una ventana de eventos de 9999 días, y
        // esto evita que un typo deje un módulo tardando para siempre.
        // La lógica vive en AppSettings.LeerCampo, no acá, para poder
        // probarla con tests sin abrir esta ventana.
        var nuevo = new AppSettings
        {
            SampleSeconds = AppSettings.LeerCampo(TxtSample.Text, 2, 30, 5),
            PingCount = AppSettings.LeerCampo(TxtPing.Text, 5, 100, 20),
            EventDays = AppSettings.LeerCampo(TxtEventDays.Text, 1, 90, 30),
            WheaDays = AppSettings.LeerCampo(TxtWheaDays.Text, 1, 90, 15),
            HistorialMaximo = AppSettings.LeerCampo(TxtHistorial.Text, 5, 500, 60),
            LogsMaximo = AppSettings.LeerCampo(TxtLogs.Text, 5, 200, 30),
        };

        if (!SettingsService.Guardar(nuevo))
        {
            Dialog.Error("No se guardaron los ajustes", "Revisa los permisos de la carpeta de salida. La configuración anterior sigue activa.");
            return;
        }
        SettingsService.Aplicar(nuevo);
        _actual = nuevo;

        Dialog.Info("Ajustes guardados", "Los cambios ya están activos. La retención de registros también se ha aplicado.");
        Close();
    }
}
