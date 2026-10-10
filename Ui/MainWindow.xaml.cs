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
        // Se ajusta antes de cualquier otra cosa: si la pantalla es más chica
        // que el alto declarado en el XAML, el pie de la ventana quedaría fuera
        // del área de trabajo y no habría cómo arrastrarla de vuelta.
        Ventana.AjustarAPantalla(this);
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
        // Los rótulos de grupo viajan dentro de la lista para que el desplazamiento
        // los arrastre con su sección. No son un destino: si por la precedencia de
        // estilos alguno llegara a ser seleccionable, se desmarca en el acto para
        // que no quede pintado como módulo activo.
        if (Nav.SelectedItem is not ListBoxItem item || item.Tag is not string clave)
        {
            Nav.SelectedIndex = -1;
            return;
        }
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
            _vm.SetContexto(clave);
            await EjecutarModuloAsync(clave);
        }
        finally { Nav.SelectedIndex = -1; }
    }

    /// <summary>
    /// Un solo lugar donde vive «qué hace cada módulo». La paleta de comandos
    /// y la lista del rail llaman acá: dos despachos distintos para la misma
    /// lista de veinte destinos es una lista que va a divergir en cuanto
    /// alguien agregue un módulo.
    /// </summary>
    private async Task EjecutarModuloAsync(string clave)
    {
        switch (clave)
        {
            case "red":
                await _vm.RunAsync("Red y latencia", ("red", PasoRed));
                break;

            case "rendimiento":
                await _vm.RunAsync("Rendimiento", ("rendimiento", PasoRendimiento));
                break;

            case "consumo":
                new ConsumoWindow { Owner = this }.ShowDialog();
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

            case "tweaks":
                new TweaksWindow { Owner = this }.ShowDialog();
                break;

            case "perfiles":
                new ProfilesWindow { Owner = this }.ShowDialog();
                break;

            case "restaurar":
                await RestaurarEstado();
                break;

            case "punto-restauracion":
                await CrearPunto();
                break;

            case "smart":
                new SmartWindow { Owner = this }.ShowDialog();
                break;

            case "comparar":
                new CompararWindow { Owner = this }.ShowDialog();
                break;

            case "historial":
                new HistoryWindow { Owner = this }.ShowDialog();
                break;

            case "traza":
                new TrazaWindow { Owner = this }.ShowDialog();
                break;

            case "ping-monitor":
                new PingMonitorWindow { Owner = this }.ShowDialog();
                break;

            case "cambios":
                new CambiosWindow { Owner = this }.ShowDialog();
                break;

            case "activacion-windows":
                new ActivacionWindowsWindow { Owner = this }.ShowDialog();
                break;

            case "ajustes":
                new SettingsWindow { Owner = this }.ShowDialog();
                break;
        }
    }

    /// <summary>
    /// Botón principal del rail. El diagnóstico completo dejó de ser un ítem
    /// más de la lista: es la acción que se hace al abrir la aplicación, y
    /// como tal se dibuja arriba y separada del resto de los módulos.
    /// </summary>
    private async void Completo_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Ocupado) return;
        await EjecutarCompleto();
    }

    private async Task EjecutarCompleto()
    {
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

    private void AbrirWindowsUpdateSistema_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:windowsupdate") { UseShellExecute = true })?.Dispose(); }
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
        if (!ExigeLicenciaPro("instalar drivers")) return;
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
        if (!ExigeLicenciaPro("actualizar programas")) return;
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
        if (!ExigeLicenciaPro("actualizar programas")) return;

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
        {
            if (!ExigeLicenciaPro("borrar temporales")) return;
            if (!await _vm.RunAsync("Limpieza",
                ("limpieza", (r, t) => Task.Run(() => CleanupModule.Clean(r, filas, t), t)))) return;

            // Se registra igual que los cambios reversibles, y marcado. Un
            // historial que solo anotara lo que se puede deshacer mentiría por
            // omisión justo en el caso que más importa: borrar no tiene vuelta
            // atrás, y conviene que quede escrito en algún sitio, con la fecha
            // y la cantidad, por si después hay que explicarlo.
            ActionLog.Registrar(OrigenCambio.Limpieza,
                $"Archivos temporales borrados ({AppEnv.FormatBytes(total)})",
                $"Categorías: {string.Join(", ", filas.Select(f => f.Ubicacion).Distinct())}." +
                (CleanupModule.Opts.Papelera ? " Se vació la papelera de reciclaje." : ""),
                reversible: false,
                nota: "El borrado de archivos no se puede deshacer. El punto de restauración del sistema tampoco recupera archivos temporales.");
        }
    }

    private async Task Optimizar()
    {
        if (!ExigeLicenciaPro("optimizar el equipo")) return;
        if (!RequiereAdmin()) return;

        var dlg = new OptimizeWindow { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var opciones = dlg.Options;
        if (!await _vm.RunActionAsync("Optimización", (r, t) => Task.Run(() => OptimizeModule.Run(r, opciones, t), t))) return;

        // La reversión de este paso restaura el conjunto completo, no solo la
        // opción que se tocó: decirlo ahora evita que el botón «Deshacer» del
        // historial sorprenda revirtiendo más de lo que se esperaba.
        ActionLog.Registrar(OrigenCambio.Optimizacion, "Optimización de red y energía",
            OpcionesTexto(opciones), referencia: AppEnv.BackupFile, reversible: true,
            nota: "Deshacer este paso restaura todas las optimizaciones pendientes, no solo esta corrida.");
    }

    /// <summary>
    /// Las opciones aplicadas, en texto. Existe porque el registro de cambios
    /// se lee semanas después: «Optimización» sin más no dice qué se tocó, y
    /// esa es justo la información que hace falta para decidir deshacerlo.
    /// </summary>
    private static string OpcionesTexto(OptimizeModule.Options o)
    {
        var partes = new List<string>();
        if (o.FlushDns) partes.Add("vaciar caché DNS");
        if (o.FlushArp) partes.Add("vaciar caché ARP");
        if (o.FixWlanAutoconfig) partes.Add("WLAN automático");
        if (o.WifiMaxPerformance) partes.Add("Wi-Fi a máximo rendimiento");
        if (o.WifiPowerSave) partes.Add("Wi-Fi en ahorro");
        if (o.PublicDns) partes.Add("DNS públicos");
        if (o.VisualEffects) partes.Add("efectos visuales");
        if (o.HighPerformancePlan) partes.Add("plan de alto rendimiento");
        if (o.ResetTcpStack) partes.Add("reinicio de la pila TCP/IP (no reversible)");
        if (o.GameMode) partes.Add("modo juego");
        if (o.CpuMaxPercent.HasValue) partes.Add($"CPU máxima al {o.CpuMaxPercent} %");
        return partes.Count == 0 ? "Sin opciones reconocidas." : string.Join(" · ", partes);
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

    // ---- Licencia y acciones rápidas --------------------------------------

    /// <summary>
    /// Las acciones que modifican el equipo requieren licencia vigente
    /// (prueba o código Pro). El diagnóstico y la lectura nunca se bloquean.
    /// </summary>
    private bool ExigeLicenciaPro(string accion)
    {
        if (Core.Licensing.LicenseService.PuedeModificar) return true;
        Dialog.Info("Se requiere activación",
            $"La prueba terminó y {accion} modifica el equipo. Activa SysDiag con un código para usarla; el diagnóstico y la lectura siguen libres.");
        new ActivationWindow { Owner = this }.ShowDialog();
        return Core.Licensing.LicenseService.PuedeModificar;
    }

    private void Licencia_Click(object sender, RoutedEventArgs e) =>
        new ActivationWindow { Owner = this }.ShowDialog();

    /// <summary>
    /// Destino de cada botón de la banda de sección. Muchos reutilizan los
    /// mismos handlers del pie y de la vista Datos: una sola implementación
    /// por acción, se invoque desde donde se invoque.
    /// </summary>
    private async void AccionRapida_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Ocupado) return;
        if (sender is not Button { Tag: string id }) return;
        await EjecutarAccionAsync(id);
    }

    /// <summary>
    /// Igual que EjecutarModuloAsync pero para la banda de acciones. Un solo
    /// despacho: la paleta de comandos ejecuta exactamente lo mismo que el
    /// botón, porque llama a lo mismo.
    /// </summary>
    internal async Task EjecutarAccionAsync(string id)
    {
        switch (id)
        {
            case "ping-monitor": new PingMonitorWindow { Owner = this }.ShowDialog(); break;
            case "historial": new HistoryWindow { Owner = this }.ShowDialog(); break;
            case "informe": Informe_Click(this, new RoutedEventArgs()); break;
            case "export-json": ExportarJson_Click(this, new RoutedEventArgs()); break;
            case "ajustes": new SettingsWindow { Owner = this }.ShowDialog(); break;
            case "licencia": new ActivationWindow { Owner = this }.ShowDialog(); break;
            case "tweaks": new TweaksWindow { Owner = this }.ShowDialog(); break;
            case "perfiles": new ProfilesWindow { Owner = this }.ShowDialog(); break;
            case "optimizar": await Optimizar(); break;
            case "limpieza": await Limpieza(); break;
            case "buscar-drivers": await BuscarDrivers(); break;
            case "actualizar-todo": ActualizarTodo_Click(this, new RoutedEventArgs()); break;
            case "abrir-wu": AbrirWindowsUpdateSistema_Click(this, new RoutedEventArgs()); break;
            case "abrir-devmgmt": AbrirDeviceManager_Click(this, new RoutedEventArgs()); break;
            case "verificar-driver": VerificarDriver_Click(this, new RoutedEventArgs()); break;
            case "ver-procesos": SeleccionarTabla("Procesos por CPU"); break;
            case "ver-eventos": SeleccionarTabla("Eventos (detalle)"); break;
            case "ver-hallazgos": VHallazgos.IsChecked = true; break;
            case "ir-red": await _vm.RunAsync("Red y latencia", ("red", PasoRed)); break;
            case "limpiar-dns": await LimpiarDns(); break;
            case "crear-punto": await CrearPunto(); break;
            case "restaurar": await RestaurarEstado(); break;
            case "energia": AbrirHerramienta("powercfg.cpl"); break;
            case "diskmgmt": AbrirHerramienta("diskmgmt.msc"); break;
            case "defender": AbrirHerramienta("windowsdefender:"); break;
            case "taskmgr": AbrirHerramienta("taskmgr.exe"); break;
            case "servicios": AbrirHerramienta("services.msc"); break;
            case "rstrui": AbrirHerramienta("rstrui.exe"); break;
            case "activacion-windows": new ActivacionWindowsWindow { Owner = this }.ShowDialog(); break;
            case "cambios": new CambiosWindow { Owner = this }.ShowDialog(); break;
            case "abrir-activacion-os": AbrirHerramienta("ms-settings:activation"); break;
        }
    }

    private void SeleccionarTabla(string nombre)
    {
        VDatos.IsChecked = true;
        if (_vm.Tablas.Contains(nombre)) _vm.TablaSeleccionada = nombre;
    }

    // ---- Paleta de comandos (Ctrl+K) --------------------------------------

    /// <summary>
    /// El atajo se atiende en la ventana y no en el campo de búsqueda de la
    /// paleta porque la paleta está cerrada cuando se pulsa: quien la dispara
    /// es la ventana que la contiene.
    /// </summary>
    private void Ventana_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.K && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            AbrirPaleta();
            e.Handled = true;
        }
    }

    private void Paleta_Click(object sender, RoutedEventArgs e) => AbrirPaleta();

    private void AbrirPaleta()
    {
        if (_vm.Ocupado)
        {
            AppLog.Write("Hay una operación en curso. Espera a que termine para abrir la paleta.", "WARN");
            return;
        }
        new PaletteWindow(ConstruirComandos()) { Owner = this }.ShowDialog();
    }

    /// <summary>
    /// La lista de comandos se arma desde las mismas estructuras que ya pintan
    /// la interfaz, no desde una segunda lista mantenida a mano: los módulos
    /// salen de los ítems del rail y las acciones salen de la sección activa.
    /// Dos listas paralelas de lo mismo divergen en cuanto alguien agrega un
    /// módulo, y ese desajuste se ve solo en la paleta.
    /// </summary>
    private List<Comando> ConstruirComandos()
    {
        var comandos = new List<Comando>();

        comandos.Add(new Comando
        {
            Titulo = "Diagnóstico completo",
            Grupo = "Medir",
            Detalle = "Recorre todos los módulos y archiva el resultado.",
            Sinonimos = "todo completo analisis escanear empezar",
            Ejecutar = EjecutarCompleto
        });

        foreach (var item in Nav.Items.OfType<ListBoxItem>())
        {
            if (item.Tag is not string clave || clave.Length == 0) continue;
            string etiqueta = EtiquetaNav(item);
            if (etiqueta.Length == 0) continue;

            comandos.Add(new Comando
            {
                Titulo = etiqueta,
                Grupo = "Módulo",
                Detalle = item.ToolTip?.ToString() ?? "",
                Ejecutar = async () =>
                {
                    _vm.SetContexto(clave);
                    await EjecutarModuloAsync(clave);
                }
            });
        }

        // Las acciones de la sección activa van primero en su propio grupo:
        // quien abre la paleta desde la vista de Drivers casi siempre quiere
        // una de esas, y encontrarlas al final de veinte módulos es perder el
        // sentido del atajo.
        foreach (var accion in _vm.Contexto?.Acciones ?? new List<AccionRapida>())
        {
            if (string.IsNullOrWhiteSpace(accion.Id)) continue;
            string id = accion.Id;
            comandos.Add(new Comando
            {
                Titulo = accion.Texto,
                Grupo = _vm.Contexto.Nombre,
                Detalle = "Acción de la sección actual.",
                Ejecutar = () => EjecutarAccionAsync(id)
            });
        }

        comandos.AddRange(new[]
        {
            new Comando { Titulo = "Exportar informe", Grupo = "Datos",
                Detalle = "HTML o Markdown, completo o redactado para compartir.",
                Sinonimos = "exportar informe html markdown soporte foro redactar",
                Ejecutar = () => { Informe_Click(this, new RoutedEventArgs()); return Task.CompletedTask; } },
            new Comando { Titulo = "Exportar todo (JSON)", Grupo = "Datos",
                Detalle = "Todos los datos medidos, para procesarlos con otro programa.",
                Sinonimos = "json exportar todo datos",
                Ejecutar = () => { ExportarJson_Click(this, new RoutedEventArgs()); return Task.CompletedTask; } },
            new Comando { Titulo = "Exportar tabla (CSV)", Grupo = "Datos",
                Detalle = "La tabla que estás viendo en Datos.",
                Sinonimos = "csv tabla exportar",
                Ejecutar = () => { ExportarCsv_Click(this, new RoutedEventArgs()); return Task.CompletedTask; } },
            new Comando { Titulo = "Abrir carpeta de salida", Grupo = "Datos",
                Detalle = "Donde SysDiag guarda informes, historial y exportaciones.",
                Sinonimos = "carpeta archivos salida ruta",
                Ejecutar = () => { Carpeta_Click(this, new RoutedEventArgs()); return Task.CompletedTask; } },
            new Comando { Titulo = "Copiar registro", Grupo = "Datos",
                Detalle = "Copia al portapapeles lo que se está viendo, no todo el registro.",
                Sinonimos = "copiar log registro portapapeles",
                Ejecutar = () => { CopiarRegistro_Click(this, new RoutedEventArgs()); return Task.CompletedTask; } },
            new Comando { Titulo = "Ver Resumen", Grupo = "Vista", Ejecutar = () => { VResumen.IsChecked = true; return Task.CompletedTask; } },
            new Comando { Titulo = "Ver Hallazgos", Grupo = "Vista", Ejecutar = () => { VHallazgos.IsChecked = true; return Task.CompletedTask; } },
            new Comando { Titulo = "Ver Datos", Grupo = "Vista", Ejecutar = () => { VDatos.IsChecked = true; return Task.CompletedTask; } },
            new Comando { Titulo = "Ver Registro", Grupo = "Vista", Ejecutar = () => { VRegistro.IsChecked = true; return Task.CompletedTask; } },
            new Comando { Titulo = "Ajustes", Grupo = "Ventana",
                Detalle = "Segundos de muestreo, cantidad de pings, ventanas de días.",
                Sinonimos = "ajustes configuracion opciones preferencias",
                Ejecutar = () => { new SettingsWindow { Owner = this }.ShowDialog(); return Task.CompletedTask; } },
            new Comando { Titulo = "Historial", Grupo = "Ventana",
                Detalle = "Diagnósticos guardados.", Sinonimos = "historial guardados anteriores",
                Ejecutar = () => { new HistoryWindow { Owner = this }.ShowDialog(); return Task.CompletedTask; } },
            new Comando { Titulo = "Cambios aplicados", Grupo = "Ventana",
                Detalle = "Lo que SysDiag cambió en el equipo, con deshacer paso por paso.",
                Sinonimos = "cambios deshacer historial revertir",
                Ejecutar = () => { new CambiosWindow { Owner = this }.ShowDialog(); return Task.CompletedTask; } },
            new Comando { Titulo = "Ajustes de Windows", Grupo = "Ventana",
                Detalle = "Catálogo de ajustes de Windows 10 y 11, con ensayo y reversión.",
                Sinonimos = "tweaks ajustes windows registro",
                Ejecutar = () => { new TweaksWindow { Owner = this }.ShowDialog(); return Task.CompletedTask; } },
            new Comando { Titulo = "Activación de SysDiag", Grupo = "Ventana",
                Detalle = "Estado de la licencia de la aplicación.",
                Sinonimos = "licencia activacion pro codigo",
                Ejecutar = () => { new ActivationWindow { Owner = this }.ShowDialog(); return Task.CompletedTask; } }
        });

        if (PuedeCancelarAhora()) comandos.Add(new Comando
        {
            Titulo = "Cancelar la operación en curso",
            Grupo = "Ventana",
            Ejecutar = () => { Cancelar_Click(this, new RoutedEventArgs()); return Task.CompletedTask; }
        });

        if (!AppEnv.IsAdmin) comandos.Add(new Comando
        {
            Titulo = "Reiniciar como administrador",
            Grupo = "Ventana",
            Detalle = "Algunos módulos solo se completan elevados.",
            Sinonimos = "admin elevar permisos administrador",
            Ejecutar = () => { Elevar_Click(this, new RoutedEventArgs()); return Task.CompletedTask; }
        });

        return comandos;
    }

    private bool PuedeCancelarAhora() => _vm.PuedeCancelar;

    /// <summary>
    /// El texto del ítem del rail. El contenido es un StackPanel con el icono y
    /// la etiqueta, así que la etiqueta es el último TextBlock con texto.
    /// </summary>
    private static string EtiquetaNav(ListBoxItem item)
    {
        if (item.Content is not StackPanel panel) return "";
        return panel.Children.OfType<TextBlock>()
            .Select(t => t.Text)
            .LastOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? "";
    }

    // ---- Filtros de las vistas --------------------------------------------

    /// <summary>
    /// El valor del filtro viaja en <c>Tag</c> y no en el nombre del control:
    /// cuatro radios con el mismo handler y un <c>switch</c> sobre el texto
    /// sería un cuarto duplicado de la misma lista, y es el tipo de duplicado
    /// que queda desincronizado en cuanto se agrega una severidad.
    /// </summary>
    private void FiltroHallazgos_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string valor } && int.TryParse(valor, out int filtro))
            _vm.FiltroHallazgos = filtro;
    }

    private void FiltroRegistro_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string valor })
            _vm.FiltroRegistro = valor;
    }

    /// <summary>
    /// Copia lo que se está viendo, no todo el registro: quien filtra y luego
    /// copia espera llevarse lo filtrado. Con el registro completo en el
    /// portapapeles, pegarlo en un informe obliga a limpiarlo a mano.
    /// </summary>
    private void CopiarRegistro_Click(object sender, RoutedEventArgs e)
    {
        string texto = _vm.TextoRegistroFiltrado;
        if (string.IsNullOrWhiteSpace(texto))
        {
            Dialog.Info("Nada que copiar", "No hay líneas que coincidan con el filtro y la búsqueda actuales.");
            return;
        }

        try
        {
            Clipboard.SetText(texto);
            AppLog.Write($"Registro: {texto.Split('\n').Length} líneas copiadas al portapapeles.", "OK");
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            // El portapapeles puede estar tomado por otro proceso; no es un
            // error de SysDiag y no merece sonar como tal.
            Dialog.Error("No se pudo copiar", "Otro programa está usando el portapapeles. " + ex.Message);
        }
    }

    private static void AbrirHerramienta(string destino)
    {
        try
        {
            Process.Start(new ProcessStartInfo(destino) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Dialog.Error("No se pudo abrir la herramienta", ex.Message);
        }
    }

    private async Task LimpiarDns()
    {
        bool ok = false;
        if (await _vm.RunActionAsync("Vaciar caché DNS", (r, t) => Task.Run(() =>
        {
            var res = AppEnv.RunCommand("ipconfig", new[] { "/flushdns" }, token: t);
            ok = res.ExitCode == 0;
        }, t)))
            Dialog.Info("Caché DNS", ok
                ? "La caché DNS quedó vacía: Windows resolverá los nombres de nuevo."
                : "Windows no permitió vaciar la caché DNS. Revisa Registro para el detalle.");
    }

    private async Task CrearPunto()
    {
        if (!RequiereAdmin()) return;
        RestorePointModule.Resultado punto = null;
        if (!await _vm.RunActionAsync("Creando punto de restauración", (r, t) => Task.Run(() => punto = RestorePointModule.Crear("Punto manual desde SysDiag"), t))) return;
        if (punto.Exito)
        {
            // No reversible en el sentido de este registro: un punto de
            // restauración no se «deshace», se usa. Se anota igual para que el
            // historial sea la cronología completa de lo que se hizo, sin
            // huecos justo en el paso que existe para proteger a los demás.
            ActionLog.Registrar(OrigenCambio.PuntoRestauracion, "Punto de restauración de Windows",
                punto.Mensaje, reversible: false,
                nota: "Un punto de restauración no se deshace: se aplica desde la configuración de Windows cuando hace falta.");
            Dialog.Info("Punto de restauración creado", punto.Mensaje);
        }
        else
        {
            Dialog.Error("No se pudo crear el punto de restauración", punto.Mensaje);
        }
    }

    private async Task RestaurarEstado()
    {
        if (!RequiereAdmin()) return;
        string result = null;
        if (await _vm.RunActionAsync("Restaurando estado", (r, t) => Task.Run(() => result = OptimizeModule.Restore(), t)))
        {
            // Restaurar desde fuera del historial tiene que dejarlo reflejado
            // ahí: si no, el historial seguiría ofreciendo «Deshacer» sobre
            // optimizaciones que ya se revirtieron por otro camino.
            ActionLog.MarcarDeshechos(OrigenCambio.Optimizacion, result ?? "Restaurado desde «Restaurar estado».");
            Dialog.Info("Restaurar estado previo", result);
        }
    }

    // ---- Pie --------------------------------------------------------------

    private async void Reparar_Click(object sender, RoutedEventArgs e)
    {
        var hallazgo = _vm.HallazgoSeleccionado;
        var accion = Remediation.Obtener(hallazgo?.AccionId);
        if (accion == null) return;
        if (!ExigeLicenciaPro("reparar el hallazgo")) return;

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

    /// <summary>
    /// El informe ya no se genera a ciegas en un solo formato: se elige para
    /// quién es. Compartir un HTML con el nombre del equipo, el del usuario y
    /// el SSID de la red de la casa es lo que pasaba siempre, porque el README
    /// se limitaba a aconsejar editarlo a mano.
    /// </summary>
    private void Informe_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.PuedeExportar) return;
        try
        {
            new ExportarWindow(_vm.Report) { Owner = this }.ShowDialog();
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
