using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SysDiag.Core;
using SysDiag.Core.Windows;
using SysDiag.Models;

namespace SysDiag.Ui;

/// <summary>
/// Ventana de activación de Windows. Lee el estado de la licencia y ofrece
/// los caminos oficiales para activarla.
///
/// Dos decisiones de diseño atraviesan toda la ventana. La primera: la
/// advertencia de qué hace y qué no va a la vista, arriba y completa, en vez
/// de escondida en un tooltip. Una herramienta que toca licencias se presta a
/// malentendidos, y el malentendido se desarma antes, no después.
///
/// La segunda: nada se ejecuta en silencio. Cada acción pasa por una
/// confirmación que dice exactamente qué va a cambiar, y la respuesta de la
/// utilidad de Windows se muestra completa en el panel de salida. Si Windows
/// rechaza la clave, el usuario tiene que poder leer por qué.
/// </summary>
public partial class ActivacionWindowsWindow : Window
{
    private bool _ocupado;

    public ActivacionWindowsWindow()
    {
        InitializeComponent();
        // Se ajusta antes de cualquier otra cosa: si la pantalla es más chica
        // que el alto declarado en el XAML, el pie de la ventana quedaría fuera
        // del área de trabajo y no habría cómo arrastrarla de vuelta.
        Ventana.AjustarAPantalla(this);

        // El módulo vive en Core y no conoce la UI: le prestamos la forma de
        // avisar para que un fallo al abrir un canal oficial no se pierda.
        ActivationModule.Notificar = (titulo, cuerpo) => Dialog.Error(titulo, cuerpo);

        Closing += (_, e) =>
        {
            if (!_ocupado) return;
            e.Cancel = true;
            Dialog.Info("Operación en curso",
                "Windows todavía está procesando la solicitud. Esperá a que termine.");
        };
        Closed += (_, _) => ActivationModule.Notificar = null;

        Refrescar();
    }

    // ---- Estado -----------------------------------------------------------

    /// <summary>
    /// Consulta y pinta. La consulta es rápida (una llamada WMI) y por eso va
    /// directo: las acciones sí se van a segundo plano, porque slmgr tarda.
    /// </summary>
    private void Refrescar()
    {
        var estado = ActivationModule.Consultar();

        Brush color = Res(estado.Nivel == Severity.Ok ? "BOk"
            : estado.Nivel == Severity.Bad ? "BBad" : "BWarn");

        TxtEstado.Text = estado.Titulo;
        TxtEstado.Foreground = color;
        TxtEdicion.Text = string.IsNullOrWhiteSpace(estado.Edicion) ? "Edición no reportada" : estado.Edicion;
        TxtExplicacion.Text = estado.Explicacion;

        IconoEstado.Text = estado.Nivel == Severity.Ok ? "\uE73E"
            : estado.Nivel == Severity.Bad ? "\uEA39" : "\uE7BA";
        IconoEstado.Foreground = color;
        SelloEstado.Background = Lavado(color);

        PanelDatos.Children.Clear();
        foreach (var fila in estado.Filas)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 7) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var clave = new TextBlock { Text = fila.Clave, Style = (Style)FindResource("Dim") };
            var valor = new TextBlock
            {
                Text = fila.Valor,
                Style = (Style)FindResource("Body"),
                TextWrapping = TextWrapping.Wrap
            };

            grid.Children.Add(clave);
            grid.Children.Add(valor);
            Grid.SetColumn(clave, 0);
            Grid.SetColumn(valor, 1);
            PanelDatos.Children.Add(grid);
        }

        if (!string.IsNullOrWhiteSpace(estado.Aviso))
        {
            var aviso = new TextBlock
            {
                Text = estado.Aviso,
                Style = (Style)FindResource("Dim"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0),
                Foreground = Res("BTextMuted")
            };
            PanelDatos.Children.Add(aviso);
        }

        PanelAdmin.Visibility = AppEnv.IsAdmin ? Visibility.Collapsed : Visibility.Visible;
        TxtPuerto.Text = string.IsNullOrWhiteSpace(TxtPuerto.Text) ? "1688" : TxtPuerto.Text;

        AppLog.Write($"Activación de Windows: {estado.Titulo}",
            estado.Nivel == Severity.Ok ? "OK" : estado.Nivel == Severity.Bad ? "ERROR" : "WARN");
    }

    // ---- Acciones ---------------------------------------------------------

    private async void Instalar_Click(object sender, RoutedEventArgs e)
    {
        string clave = TxtClave.Text ?? "";
        string normalizada = ActivationModule.NormalizarClave(clave);
        string problema = ActivationModule.ValidarClave(normalizada);
        if (problema != null)
        {
            Dialog.Error("Clave incompleta", problema);
            return;
        }

        // La confirmación dice qué se va a hacer y con qué: el usuario tiene
        // que poder comprobar que la clave que escribió es la que se instala.
        if (!Dialog.Confirm("Instalar y activar",
                $"Se instalará la clave terminada en {ActivationModule.Enmascarar(normalizada)} y se " +
                "pedirá la activación a los servidores de Microsoft.\n\n" +
                "Usala solo con una licencia legítima de la que seas titular.",
                "Instalar y activar"))
            return;

        var resultado = await EjecutarAsync("Instalando clave de producto",
            () => ActivationModule.InstalarClave(clave));

        if (resultado == null) return;
        if (resultado.Exito) TxtClave.Clear();

        Mostrar(resultado, "Clave de producto");
        Refrescar();
    }

    private async void ActivarEnLinea_Click(object sender, RoutedEventArgs e)
    {
        if (!Dialog.Confirm("Activar en línea",
                "Se le pedirá a Microsoft que valide la licencia instalada en este equipo. " +
                "Necesitás conexión a internet.",
                "Activar"))
            return;

        var resultado = await EjecutarAsync("Activando Windows", () => ActivationModule.ActivarEnLinea());
        if (resultado == null) return;

        Mostrar(resultado, "Activación en línea");
        Refrescar();
    }

    private async void ConfigurarKms_Click(object sender, RoutedEventArgs e)
    {
        string host = (TxtKms.Text ?? "").Trim();
        string puerto = (TxtPuerto.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            Dialog.Error("Falta el host", "Escribí el nombre o la dirección IP de tu servidor KMS.");
            return;
        }

        if (!Dialog.Confirm("Configurar activación por volumen",
                $"La activación de este equipo pasará a depender del host KMS «{host}».\n\n" +
                "Usalo solo con un servidor KMS propio de tu organización. Apuntarlo a un host " +
                "ajeno activaría Windows sin licencia, y eso no es algo que SysDiag haga.",
                "Configurar"))
            return;

        var resultado = await EjecutarAsync("Configurando activación por volumen",
            () => ActivationModule.ConfigurarKms(host, puerto));
        if (resultado == null) return;

        Mostrar(resultado, "Activación por volumen");
        Refrescar();
    }

    private void VerDetalle_Click(object sender, RoutedEventArgs e)
    {
        string detalle = ActivationModule.LicenciaDetallada();
        string vencimiento = ActivationModule.VencimientoDetallado();
        if (!string.IsNullOrWhiteSpace(vencimiento)) detalle += Environment.NewLine + Environment.NewLine + vencimiento;

        TxtDetalle.Text = detalle;
        PanelDetalle.Visibility = Visibility.Visible;
    }

    private void AbrirConfiguracion_Click(object sender, RoutedEventArgs e) => ActivationModule.AbrirConfiguracion();
    private void AbrirTienda_Click(object sender, RoutedEventArgs e) => ActivationModule.AbrirTienda();
    private void Solucionador_Click(object sender, RoutedEventArgs e) => ActivationModule.AbrirSolucionador();

    private void Elevar_Click(object sender, RoutedEventArgs e)
    {
        if (AppEnv.RelaunchElevated()) Close();
    }

    private void Cerrar_Click(object sender, RoutedEventArgs e) => Close();

    // ---- Infraestructura --------------------------------------------------

    /// <summary>
    /// Corre la acción fuera del hilo de la interfaz y bloquea los botones
    /// mientras tanto. slmgr abre una consola y tarda varios segundos: en el
    /// hilo de la UI la ventana se congela y se ve como si se hubiera caído.
    /// </summary>
    private async Task<ActivationModule.Resultado> EjecutarAsync(string titulo,
        Func<ActivationModule.Resultado> trabajo)
    {
        if (_ocupado) return null;

        _ocupado = true;
        Cursor = System.Windows.Input.Cursors.Wait;
        foreach (var boton in new[] { BtnInstalar, BtnActivar, BtnKms, BtnDetalle })
            boton.IsEnabled = false;

        ActivationModule.Resultado resultado = null;
        try
        {
            resultado = await Task.Run(trabajo);
        }
        catch (Exception ex)
        {
            AppLog.Write($"{titulo}: {ex}", "ERROR");
            resultado = ActivationModule.Resultado.Falla("No se pudo completar la operación: " + ex.Message);
        }
        finally
        {
            _ocupado = false;
            Cursor = System.Windows.Input.Cursors.Arrow;
            foreach (var boton in new[] { BtnInstalar, BtnActivar, BtnKms, BtnDetalle })
                boton.IsEnabled = true;
        }

        return resultado;
    }

    private void Mostrar(ActivationModule.Resultado resultado, string titulo)
    {
        if (!string.IsNullOrWhiteSpace(resultado.Salida))
        {
            TxtDetalle.Text = resultado.Salida.Trim();
            PanelDetalle.Visibility = Visibility.Visible;
        }

        AppLog.Write($"{titulo}: {(resultado.Exito ? "completado" : "falló")} — {resultado.Mensaje}",
            resultado.Exito ? "OK" : "WARN");

        if (resultado.Exito) Dialog.Info(titulo, resultado.Mensaje);
        else Dialog.Error(titulo, resultado.Mensaje);
    }

    private static Brush Res(string clave) => (Brush)Application.Current.Resources[clave];

    private static Brush Lavado(Brush color)
    {
        var c = ((SolidColorBrush)color).Color;
        var lavado = new SolidColorBrush(Color.FromArgb(38, c.R, c.G, c.B));
        lavado.Freeze();
        return lavado;
    }
}
