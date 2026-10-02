using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SysDiag.Core.Storage;

namespace SysDiag.Ui;

public partial class CleanupWindow : Window
{
    private readonly Dictionary<string, CheckBox> _casillas = new();

    public CleanupWindow()
    {
        InitializeComponent();
        MouseLeftButtonDown += (_, _) => DragMove();

        var o = CleanupModule.Opts;

        Add("TempUsuario", "Temporales del usuario", "Solo archivos con más de 24 horas; se conservan los temporales recientes y los de solo lectura.", o.TempUsuario, "BOk");
        Add("TempWindows", "Temporales de Windows", "Equivalente del sistema. Requiere administrador para vaciarlo entero.", o.TempWindows, "BOk");
        Add("CacheInternet", "Caché de Internet", "Archivos temporales de navegación del sistema.", o.CacheInternet, "BOk");
        Add("VolcadosApp", "Volcados de aplicaciones", "Restos de programas que se cerraron con error.", o.VolcadosApp, "BOk");
        Add("LogsJuegos", "Registros de League of Legends", "El cliente los regenera en la siguiente partida.", o.LogsJuegos, "BOk");
        Add("Miniaturas", "Caché de miniaturas", "Se reconstruye solo al abrir carpetas con imágenes.", o.Miniaturas, "BOk");
        Add("ShaderCache", "Caché de sombreadores DirectX", "Se regenera al jugar. La primera partida puede tener algún tirón extra.", o.ShaderCache, "BWarn");
        Add("ErroresWindows", "Informes de errores de Windows", "Reportes de fallos ya enviados o caducados.", o.ErroresWindows, "BOk");
        Add("CacheWindowsUpdate", "Caché de Windows Update", "Libera bastante espacio, pero obliga a volver a descargar actualizaciones pendientes. Requiere administrador.", o.CacheWindowsUpdate, "BWarn");
        Add("DeliveryOptimization", "Archivos de Delivery Optimization", "Trozos de actualizaciones compartidos con otros equipos de la red.", o.DeliveryOptimization, "BWarn");
        Add("Prefetch", "Prefetch", "Windows lo usa para acelerar el arranque de programas. Al borrarlo, los primeros inicios serán más lentos hasta que se reconstruya.", o.Prefetch, "BWarn");
        Add("Papelera", "Vaciar la papelera de reciclaje", "IRREVERSIBLE. Lo que haya dentro se pierde definitivamente, incluido lo que hayas borrado por error.", o.Papelera, "BBad");
    }

    private void Add(string clave, string titulo, string detalle, bool marcado, string colorKey)
    {
        var box = new CheckBox
        {
            IsChecked = marcado,
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = titulo,
                        Style = (Style)FindResource("Body"),
                        FontWeight = FontWeights.SemiBold
                    },
                    new TextBlock
                    {
                        Text = detalle,
                        Style = (Style)FindResource("Dim"),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 3, 0, 0)
                    }
                }
            }
        };

        box.Style = (Style)FindResource("Opcion");
        box.Tag = (Brush)FindResource(colorKey);

        _casillas[clave] = box;
        Lista.Children.Add(box);
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

    private void Aceptar_Click(object sender, RoutedEventArgs e)
    {
        var o = CleanupModule.Opts;
        bool V(string k) => _casillas[k].IsChecked == true;

        o.TempUsuario = V("TempUsuario");
        o.TempWindows = V("TempWindows");
        o.CacheInternet = V("CacheInternet");
        o.VolcadosApp = V("VolcadosApp");
        o.LogsJuegos = V("LogsJuegos");
        o.Miniaturas = V("Miniaturas");
        o.ShaderCache = V("ShaderCache");
        o.ErroresWindows = V("ErroresWindows");
        o.CacheWindowsUpdate = V("CacheWindowsUpdate");
        o.DeliveryOptimization = V("DeliveryOptimization");
        o.Prefetch = V("Prefetch");
        o.Papelera = V("Papelera");

        DialogResult = true;
        Close();
    }
}
