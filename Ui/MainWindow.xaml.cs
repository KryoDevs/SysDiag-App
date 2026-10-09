using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SysDiag.Core;
using SysDiag.Core.Diagnostics;
using SysDiag.Core.Drivers;
using SysDiag.Core.Hardware;
using SysDiag.Core.Network;
using SysDiag.Core.Performance;
using SysDiag.Core.Security;
using SysDiag.Core.Storage;
using SysDiag.Core.Windows;
using SysDiag.Models;
using SysDiag.Services;

namespace SysDiag.Ui;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    private readonly DispatcherTimer _reloj;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        // El ícono se carga acá, no como atributo XAML: un .ico mal formado
        // ahí rompe la construcción del BAML entero y tumba la app antes de
        // que exista siquiera una ventana con la que mostrar el error. Acá,
        // si falla, la app sigue sin ícono en vez de no arrancar.
        CargarIcono();

        // Reloj de la cabecera: se actualiza cada segundo, sin depender de
        // que corra ningún diagnóstico.
        _reloj = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _reloj.Tick += (_, _) => _vm.HoraSistema = DateTime.Now.ToString("HH:mm:ss");
        _reloj.Start();
        Closed += (_, _) => { _reloj.Stop(); _vm.Dispose(); };
        Closing += (_, e) =>
        {
            if (!_vm.Ocupado) return;
            e.Cancel = true;
            if (_vm.PuedeCancelar)
            {
                if (Dialog.Confirm("Hay una operación en curso", "Cancélala y espera a que termine antes de cerrar.", "Cancelar operación"))
                    _vm.Cancelar();
            }
            else Dialog.Info("Operación no interrumpible", "Espera a que termine. Cerrar durante una instalación o restauración puede dejar el sistema en un estado parcial.");
        };

        StateChanged += (_, _) =>
        {
            // Con chrome propio, una ventana maximizada se sale de la pantalla
            // por el grosor del borde de redimensión: se compensa con margen.
            Root.Margin = WindowState == System.Windows.WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            BtnMax.Content = WindowState == System.Windows.WindowState.Maximized ? "\uE923" : "\uE922";
            BtnMax.ToolTip = WindowState == System.Windows.WindowState.Maximized ? "Restaurar" : "Maximizar";
        };

        // El registro se sigue en vivo. Se engancha al volcado por lotes y no
        // a cada línea: así se desplaza una vez por ráfaga, no una por línea.
        _vm.LineaAgregada += () =>
        {
            if (LogList.Items.Count > 0)
                LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _vm.Ocupado) _vm.Cancelar();
        };
    }

    /// <summary>
    /// Carga el ícono desde el recurso empaquetado. Si el archivo no decodifica
    /// bien —algo que puede pasar con .ico generados por herramientas de
    /// terceros, por ejemplo con cuadros comprimidos en PNG donde el cargador
    /// de íconos de WPF puede fallar— la ventana igual arranca, solo que sin
    /// ícono propio. Preferible a que la app entera no abra por un detalle
    /// puramente cosmético.
    /// </summary>
    private void CargarIcono()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Resources/Icons/AppIcon.ico");
            var recurso = Application.GetResourceStream(uri);
            if (recurso == null)
            {
                AppLog.Write("No se encontró el recurso del ícono.", "WARN");
                return;
            }

            using (recurso.Stream)
            {
                Icon = System.Windows.Media.Imaging.BitmapFrame.Create(
                    recurso.Stream,
                    System.Windows.Media.Imaging.BitmapCreateOptions.None,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            }
        }
        catch (Exception ex)
        {
            AppLog.Write($"No se pudo cargar el ícono de la ventana: {ex.Message}", "WARN");
            // Se sigue sin ícono: Icon queda en null, WPF usa el genérico.
        }
    }

    // ---- Barra de título --------------------------------------------------

    private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Max_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == System.Windows.WindowState.Maximized
            ? System.Windows.WindowState.Normal
            : System.Windows.WindowState.Maximized;

    private void Cerrar_Click(object sender, RoutedEventArgs e) => Close();

    // ---- Navegación -------------------------------------------------------

    private async void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is not ListBoxItem item || item.Tag is not string clave) return;
        if (_vm.Ocupado)
        {
            // Si el ítem quedara marcado, volver a pulsarlo al terminar la operación no dispararía este evento.
            AppLog.Write("Hay una operación en curso. Espera a que termine para elegir otro módulo.", "WARN");
            Nav.SelectedIndex = -1;
            return;
        }
        try
        {
            // La selección marca el módulo activo; se limpia al terminar para que
            // volver a pulsar el mismo vuelva a ejecutarlo.
            switch (clave)
            {
                case "completo":
                    _vm.BusquedaDriversHecha = false;
                    await _vm.RunAsync("Diagnóstico completo",
                        ("red", PasoRed),
                        ("rendimiento", PasoRendimiento),
                        ("termicas", PasoTermicas),
                        ("almacenamiento", PasoAlmacenamiento),
                        ("seguridad", PasoSeguridad),
                        ("estabilidad", PasoEstabilidad),
                        ("drivers", PasoDrivers),
                        ("actualizaciones", PasoActualizaciones),
                        ("arranque", PasoArranque),
                        ("limpieza", PasoAnalisisLimpieza));
                    break;

                case "red":
                    await _vm.RunAsync("Red y latencia", ("red", PasoRed));
                    break;

                case "rendimiento":
                    await _vm.RunAsync("Rendimiento", ("rendimiento", PasoRendimiento));
                    break;

                case "termicas":
                    await _vm.RunAsync("Térmicas y energía", ("termicas", PasoTermicas));
                    break;

                case "estabilidad":
                    await _vm.RunAsync("Estabilidad", ("estabilidad", PasoEstabilidad));
                    break;

                case "limpieza":
                    await Limpieza();
                    break;

                case "drivers":
                    _vm.BusquedaDriversHecha = false;
                    await _vm.RunAsync("Drivers", ("drivers", PasoDrivers));
                    break;

                case "actualizaciones":
                    await _vm.RunAsync("Actualizaciones", ("actualizaciones", PasoActualizaciones));
                    break;

                case "almacenamiento":
                    await _vm.RunAsync("Almacenamiento", ("almacenamiento", PasoAlmacenamiento));
                    break;

                case "seguridad":
                    await _vm.RunAsync("Seguridad", ("seguridad", PasoSeguridad));
                    break;

                case "arranque":
                    await _vm.RunAsync("Arranque y software", ("arranque", PasoArranque));
                    break;

                case "optimizar":
                    await Optimizar();
                    break;

                case "perfiles":
                    new ProfilesWindow { Owner = this }.ShowDialog();
                    break;

                case "restaurar":
                    if (RequiereAdmin())
                    {
                        string result = null;
                        if (await _vm.RunActionAsync("Restaurando estado", (r, t) => Task.Run(() => result = OptimizeModule.Restore(), t)))
                            Dialog.Info("Restaurar estado previo", result);
                    }
                    break;

                case "punto-restauracion":
                    if (RequiereAdmin())
                    {
                        RestorePointModule.Resultado punto = null;
                        if (!await _vm.RunActionAsync("Creando punto de restauración", (r, t) => Task.Run(() => punto = RestorePointModule.Crear("Punto manual desde SysDiag"), t))) break;
                        if (punto.Exito)
                            Dialog.Info("Punto de restauración creado", punto.Mensaje);
                        else
                            Dialog.Error("No se pudo crear el punto de restauración", punto.Mensaje);
                    }
                    break;

                case "historial":
                    new HistoryWindow { Owner = this }.ShowDialog();
                    break;

                case "ping-monitor":
                    new PingMonitorWindow { Owner = this }.ShowDialog();
                    break;

                case "ajustes":
                    new SettingsWindow { Owner = this }.ShowDialog();
                    break;
            }
        }
        finally { Nav.SelectedIndex = -1; }
    }

    // Cada paso delega en su servicio de dominio (Services/), no en el
    // módulo estático directamente: es la capa que hace testeable el motor
    // y la que pide tu arquitectura. Los módulos de Core/ siguen siendo
    // donde vive la lógica real; los servicios son el contrato hacia la UI.
    private static Task PasoRed(DiagnosticReport r, CancellationToken t) =>
        new NetworkService().EjecutarAsync(r, t);

    private static Task PasoRendimiento(DiagnosticReport r, CancellationToken t) =>
        new PerformanceService().EjecutarAsync(r, t);

    private static Task PasoTermicas(DiagnosticReport r, CancellationToken t) =>
        new HardwareService().EjecutarAsync(r, t);

    private static Task PasoEstabilidad(DiagnosticReport r, CancellationToken t) =>
        new StabilityService().EjecutarAsync(r, t);

    private static Task PasoDrivers(DiagnosticReport r, CancellationToken t) =>
        new DriverService().EjecutarAsync(r, t);

    private static Task PasoAlmacenamiento(DiagnosticReport r, CancellationToken t) =>
        new StorageService().EjecutarAsync(r, t);

    private static Task PasoArranque(DiagnosticReport r, CancellationToken t) =>
        new StartupService().EjecutarAsync(r, t);

    private static Task PasoSeguridad(DiagnosticReport r, CancellationToken t) =>
        new SecurityService().EjecutarAsync(r, t);

    private static Task PasoActualizaciones(DiagnosticReport r, CancellationToken t)
        => new UpdateService().EjecutarAsync(r, t);
    private static Task PasoAnalisisLimpieza(DiagnosticReport r, CancellationToken t)
        => new CleanupService().EjecutarAsync(r, t);

    // Solo abren el navegador o los ajustes del sistema en los canales
    // oficiales. La app nunca descarga ni ejecuta un instalador de driver.
    private void AbrirWindowsUpdate_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:windowsupdate-optionalupdates") { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) { Dialog.Error("No se pudo abrir Windows Update", ex.Message); }
    }

    private async void BuscarDrivers_Click(object sender, RoutedEventArgs e) => await BuscarDrivers();

    private async Task BuscarDrivers()
    {
        if (_vm.Ocupado) return;
        _vm.BusquedaDriversHecha = true;
        // La llamada COM síncrona no puede abortarse con seguridad. No anunciar una cancelación ficticia.
        bool success = await _vm.RunActionAsync("Drivers disponibles", (r, t) => Task.Run(() =>
        {
            SystemModule.Run(r, token: t);
            DriverModule.Run(r);
            DriverUpdateModule.Buscar(r);
        }, t), modulo: "drivers");
        if (success) _vm.TablaSeleccionada = "Drivers disponibles";
    }

    private async void InstalarDriver_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not DriverUpdateRow fila)
        {
            Dialog.Info("Nada seleccionado", "Elige primero una fila en la tabla.");
            return;
        }
        await InstalarDrivers(new List<DriverUpdateRow> { fila }, fila.Titulo);
    }

    private async void InstalarTodosDrivers_Click(object sender, RoutedEventArgs e)
    {
        var todos = _vm.Report.DriversDisponibles;
        if (todos.Count == 0)
        {
            Dialog.Info("Nada que instalar", "Busca drivers primero.");
            return;
        }
        await InstalarDrivers(todos, $"{todos.Count} drivers");
    }

    private async Task InstalarDrivers(List<DriverUpdateRow> seleccion, string descripcion)
    {
        if (!RequiereAdmin()) return;

        bool ok = Dialog.Confirm($"Instalar {descripcion}",
            "Los paquetes vienen firmados por Microsoft y validados contra el hardware de este equipo.\n\n" +
            "Si algún paquete pide aceptar su licencia, SysDiag la acepta al confirmar: revisa la lista antes de continuar.\n\n" +
            "Aun así, un cambio de driver puede requerir reiniciar y, en casos raros, dejar un dispositivo " +
            "sin funcionar. Windows guarda la versión anterior: se revierte desde Propiedades del " +
            "dispositivo ▸ Controlador ▸ Revertir.",
            "Descargar e instalar");

        if (!ok) return;

        string resultado = null;
        if (await _vm.RunActionAsync("Instalando drivers", (r, t) => Task.Run(() =>
            resultado = DriverUpdateModule.Instalar(seleccion), t)))
        {
            Dialog.Info("Instalación de drivers", resultado ?? "Sin resultado.");
            await BuscarDrivers();
        }
    }

    private async void VerificarDriver_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Elige el archivo de driver que descargaste",
            Filter = "Archivos de driver (*.inf;*.cab;*.exe;*.msi;*.zip)|*.inf;*.cab;*.exe;*.msi;*.zip|Todos|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        string ruta = dlg.FileName;
        DriverVerifier.Resultado res = null;

        await _vm.RunActionAsync("Verificando driver", (r, t) => Task.Run(() => res = DriverVerifier.Verificar(ruta), t));

        if (res == null) return;

        string informe =
            $"Archivo: {res.Archivo}  ({res.Tamano})\n" +
            $"Firma: {res.EstadoFirma}\n" +
            (res.Firmado ? $"Emisor: {res.Emisor}\nCertificado válido hasta: {res.ValidoHasta}\n" : "") +
            $"Antivirus: {res.Antivirus}\n" +
            $"SHA-256: {res.Sha256}\n\n" +
            string.Join("\n\n", res.Notas);

        if (DriverVerifier.VerificacionSatisfactoria(res))
            Dialog.Info("Verificación informativa completada", informe);
        else
            Dialog.Error("Verificación incompleta o no válida", informe);
    }

    private void PropiedadesDispositivo_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not DriverRow fila)
        {
            Dialog.Info("Nada seleccionado", "Elige primero un dispositivo en la tabla.");
            return;
        }
        DriverUpdateModule.AbrirPropiedades(fila.DeviceId);
    }

    private void AbrirDeviceManager_Click(object sender, RoutedEventArgs e)
    {
        try { var info = ProcessRunner.CreateStartInfo(AppEnv.SystemTool("mmc"), new[] { System.IO.Path.Combine(Environment.SystemDirectory, "devmgmt.msc") });
            info.UseShellExecute = true;
            using var process = Process.Start(info); }
        catch (Exception ex) { Dialog.Error("No se pudo abrir el Administrador de dispositivos", ex.Message); }
    }

    private async void EscanearHardware_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Ocupado || !RequiereAdmin()) return;
        if (await _vm.RunActionAsync("Buscando cambios de hardware", (r, t) => Task.Run(() =>
            AppEnv.RunRequired("pnputil", new[] { "/scan-devices" }, 120000, t), t)))
            Dialog.Info("Búsqueda completada", "Windows volvió a revisar los dispositivos conectados. Repite el inventario de drivers para comprobar el resultado.");
    }

    private void ActualizarTodo_Click(object sender, RoutedEventArgs e)
    {
        bool ok = Dialog.Confirm("Actualizar todos los programas",
            "Se abrirá una consola con winget donde verás cada instalación y podrás cortarla en cualquier momento. " +
            "Cierra los programas que estén en uso antes de continuar.",
            "Abrir winget");

        if (ok && !UpdateModule.LanzarActualizacion()) Dialog.Error("No se pudo abrir winget", "Revisa Registro y la instalación de winget.");
    }

    private void ActualizarUno_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not UpdateRow fila)
        {
            Dialog.Info("Nada seleccionado", "Elige primero una fila en la tabla.");
            return;
        }

        bool ok = Dialog.Confirm($"Actualizar {fila.Nombre}",
            $"Se actualizará de {fila.Actual} a {fila.Disponible} mediante winget, en una consola visible.",
            "Actualizar");

        if (ok && !UpdateModule.LanzarActualizacion(fila.Id)) Dialog.Error("No se pudo abrir winget", "El identificador o la instalación de winget no son válidos. Revisa Registro.");
    }

    private void AbrirSoporteAsus_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Antes iba fijo a tu ASUS TUF F15: para cualquier otra marca era
            // directamente el enlace equivocado. Ahora se detecta el
            // fabricante real del equipo (ya lo recolectó SystemModule) y se
            // manda al portal genérico de soporte que le corresponde.
            string equipo = _vm.Report?.Equipo ?? "";
            string url = DriverVerifier.SitioOficial(equipo);
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) { Dialog.Error("No se pudo abrir el navegador", ex.Message); }
    }

    private async Task Limpieza()
    {
        var dlg = new CleanupWindow { Owner = this };
        if (dlg.ShowDialog() != true) return;

        // Los destinos del sistema no se pueden vaciar sin permisos elevados.
        if ((CleanupModule.Opts.CacheWindowsUpdate || CleanupModule.Opts.Prefetch
             || CleanupModule.Opts.DeliveryOptimization) && !AppEnv.IsAdmin)
        {
            Dialog.Info("Permisos insuficientes",
                "Los destinos del sistema que marcaste necesitan administrador. Se analizarán igual, pero es probable que no se puedan borrar.");
        }

        if (!await _vm.RunAsync("Limpieza",
            ("limpieza", (r, t) => Task.Run(() => CleanupModule.Analyze(r, t), t)))) return;

        if (_vm.Report.Limpieza.Count == 0 && !CleanupModule.Opts.Papelera)
        {
            // Sin esto el usuario pulsa «Limpiar» y no ve ningún resultado.
            Dialog.Info("Nada que analizar", "Ninguna de las categorías seleccionadas existe en este equipo. Marca otras categorías o revisa los permisos.");
            return;
        }

        var filas = _vm.Report.Limpieza;
        long total = filas.Sum(x => x.Bytes);

        string aviso = CleanupModule.Opts.Papelera
            ? "\n\nAdemás se vaciará la papelera de reciclaje: eso NO se puede deshacer."
            : "";

        bool borrar = Dialog.Confirm("Borrar archivos temporales",
            $"Se pueden liberar {AppEnv.FormatBytes(total)}.\n\n" +
            "Solo se borrarán los archivos analizados de las categorías seleccionadas. El borrado no se puede deshacer; los temporales recientes, protegidos o en uso se omiten." + aviso,
            "Borrar ahora");

        if (borrar)
            await _vm.RunAsync("Limpieza",
                ("limpieza", (r, t) => Task.Run(() => CleanupModule.Clean(r, filas, t), t)));
    }

    private async Task Optimizar()
    {
        if (!RequiereAdmin()) return;

        var dlg = new OptimizeWindow { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var opciones = dlg.Options;
        await _vm.RunActionAsync("Optimización", (r, t) => Task.Run(() => OptimizeModule.Run(r, opciones, t), t));
    }

    private bool RequiereAdmin()
    {
        if (AppEnv.IsAdmin) return true;

        bool elevar = Dialog.Confirm("Se necesitan permisos de administrador",
            "Este módulo cambia configuración del sistema.",
            "Reiniciar como administrador");

        if (elevar && AppEnv.RelaunchElevated()) Application.Current.Shutdown();
        return false;
    }

    private void Elevar_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Ocupado) return;
        if (AppEnv.RelaunchElevated()) Application.Current.Shutdown();
    }

    // ---- Pie --------------------------------------------------------------

    private async void Reparar_Click(object sender, RoutedEventArgs e)
    {
        var hallazgo = _vm.HallazgoSeleccionado;
        var accion = Remediation.Obtener(hallazgo?.AccionId);
        if (accion == null) return;

        // Los casos que abren su propio flujo con opciones no se ejecutan a
        // ciegas: llevan al usuario a la pantalla donde decide el detalle.
        switch (accion.Id)
        {
            case "limpiar-temp":
                await Limpieza();
                return;
            case "buscar-drivers":
                await BuscarDrivers();
                return;
        }

        if (accion.RequiereAdmin && !RequiereAdmin()) return;

        if (!Dialog.Confirm(accion.Titulo, accion.Descripcion, "Aplicar")) return;

        string resultado = null;
        if (await _vm.RunActionAsync(accion.Titulo, (r, t) => Task.Run(() => resultado = Remediation.Ejecutar(accion.Id, r), t)))
            Dialog.Info(accion.Titulo, resultado ?? "Sin resultado.");
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e) => _vm.Cancelar();

    private void Informe_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.PuedeExportar) return;
        try
        {
            string archivo = ReportBuilder.Build(_vm.Report);
            Process.Start(new ProcessStartInfo(archivo) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Dialog.Error("No se pudo generar el informe", ex.Message);
        }
    }

    private void ExportarCsv_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.PuedeExportar) return;
        try
        {
            if (_vm.FilasTabla == null || _vm.FilasTabla.Count == 0)
            {
                Dialog.Info("Nada que exportar",
                    "Elige primero una tabla en la vista Datos.");
                return;
            }

            string archivo = Exporter.ToCsv(_vm.TablaSeleccionada, _vm.FilasTabla);
            Dialog.Info("Tabla exportada", archivo);
        }
        catch (Exception ex)
        {
            Dialog.Error("No se pudo exportar", ex.Message);
        }
    }

    private void ExportarJson_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.PuedeExportar) return;
        try
        {
            string archivo = Exporter.ToJson(_vm.Report);
            Dialog.Info("Diagnóstico exportado", archivo);
        }
        catch (Exception ex)
        {
            Dialog.Error("No se pudo exportar", ex.Message);
        }
    }

    private void Carpeta_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppEnv.OutputPath) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Dialog.Error("No se pudo abrir la carpeta", ex.Message);
        }
    }

    // ---- Tabla ------------------------------------------------------------

    /// <summary>
    /// Usa el DisplayName de cada propiedad como encabezado y descarta las
    /// marcadas como no visibles, para no duplicar los nombres en la vista.
    /// </summary>
    private void Grid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (e.PropertyDescriptor is not PropertyDescriptor pd) return;

        if (!pd.IsBrowsable)
        {
            e.Cancel = true;
            return;
        }
        e.Column.Header = pd.DisplayName;
    }
}
