using System;
using System.Globalization;
using System.Linq;
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
        // Se ajusta antes de cualquier otra cosa: si la pantalla es más chica
        // que el alto declarado en el XAML, el pie de la ventana quedaría fuera
        // del área de trabajo y no habría cómo arrastrarla de vuelta.
        Ventana.AjustarAPantalla(this);
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

        // `LeerCampo` no falla: corrige. Escribir «3,5» guarda 5, y escribir
        // 9999 guarda 90, sin más señal que esa. Como la ventana se cerraba
        // justo después con un «guardado», el aviso describía otra cosa: un
        // dato presentado como guardado que no era el que se tecleó. Los
        // campos se vuelcan con lo que realmente quedó y se dice cuáles se
        // movieron. El rango sigue siendo decisión del programa; deja de ser
        // invisible.
        var revisados = new[]
        {
            ("muestreo", TxtSample.Text, nuevo.SampleSeconds),
            ("pings", TxtPing.Text, nuevo.PingCount),
            ("ventana de eventos", TxtEventDays.Text, nuevo.EventDays),
            ("ventana de WHEA", TxtWheaDays.Text, nuevo.WheaDays),
            ("historial", TxtHistorial.Text, nuevo.HistorialMaximo),
            ("retención de logs", TxtLogs.Text, nuevo.LogsMaximo),
        };
        var movidos = revisados
            .Where(x => (x.Item2 ?? "").Trim() != x.Item3.ToString(CultureInfo.InvariantCulture))
            .Select(x => $"{x.Item1}: quedó en {x.Item3}")
            .ToList();

        Volcar(nuevo);

        Dialog.Info("Ajustes guardados", movidos.Count == 0
            ? "Los cambios ya están activos. La retención de registros también se ha aplicado."
            : "Los cambios ya están activos.\n\nAlgunos campos no eran un número entero dentro del rango "
              + "admisible y se ajustaron:\n· " + string.Join("\n· ", movidos));
        Close();
    }
}
