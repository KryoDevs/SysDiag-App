using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using SysDiag.Core;
using SysDiag.Core.Diagnostics;
using SysDiag.Diagnostics;
using SysDiag.Models;

namespace SysDiag;

public partial class App : Application
{
    private Mutex _instancia;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--self-test", StringComparer.Ordinal))
        {
            // Gate del ejecutable PUBLICADO: carga WPF/recursos/JSON/reglas, no toca hardware ni ajustes.
            try
            {
                var report = new DiagnosticReport { RendimientoResumen = new() { new("RAM en uso", "86.5 %") } };
                new DiagnosticEngine().Evaluar(report);
                var copy = JsonSerializer.Deserialize<DiagnosticReport>(JsonSerializer.Serialize(report));
                if (copy?.Hallazgos.Count != 1 || HealthScore.Calcular(copy) != 95 || !Resources.Contains("BOk"))
                    throw new InvalidOperationException("Falló el autotest de reglas, JSON o recursos WPF.");
                // Cada ventana se construye sin mostrarla: valida su XAML, sus recursos y su code-behind dentro del EXE
                // publicado. Antes ninguna ventana se construía en CI, y un fallo solo aparecía al abrirla.
                _ = new Ui.MainWindow();
                _ = new Ui.HistoryWindow();
                _ = new Ui.SettingsWindow();
                _ = new Ui.ProfilesWindow();
                _ = new Ui.OptimizeWindow();
                _ = new Ui.CleanupWindow();
                _ = new Ui.PingMonitorWindow();
                _ = new Ui.DialogWindow();
                _ = new Ui.ActivationWindow();
                _ = new Ui.TweaksWindow();
                Console.WriteLine($"SYSDIAG_SELF_TEST_OK {AppEnv.Version}");
                Shutdown(0);
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); Shutdown(1); }
            return;
        }

        if (!WaitForParent(e.Args)) { Shutdown(1); return; }
        _instancia = new Mutex(false, @"Local\SysDiag.SingleInstance");
        try { _ownsMutex = _instancia.WaitOne(0); }
        catch (AbandonedMutexException) { _ownsMutex = true; }
        if (!_ownsMutex)
        {
            MessageBox.Show("SysDiag ya está abierto.", "SysDiag", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var culture = CultureInfo.GetCultureInfo("es-CL");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));
        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Write($"Excepción no controlada: {args.Exception}", "ERROR");
            Ui.Dialog.Error("Error inesperado", args.Exception.Message + "\n\nEl detalle quedó guardado en el registro.");
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => AppLog.Write($"Error fatal: {args.ExceptionObject}", "ERROR");
        Core.Windows.SettingsService.Aplicar(Core.Windows.SettingsService.Cargar());
        Core.Licensing.LicenseService.Inicializar();
        base.OnStartup(e);
    }

    private static bool WaitForParent(string[] args)
    {
        int index = Array.IndexOf(args, "--wait-for-parent");
        if (index < 0) return true;
        if (index + 1 >= args.Length || !int.TryParse(args[index + 1], out int id) || id <= 0 || id == Environment.ProcessId)
            return false;
        try
        {
            using var parent = Process.GetProcessById(id);
            // Solo esperar a otra copia de nuestro ejecutable, no a un PID arbitrario.
            if (!string.Equals(parent.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) return false;
            if (parent.WaitForExit(15000)) return true;
            MessageBox.Show("La instancia anterior todavía no terminó. Cierra SysDiag antes de volver a elevarlo.", "SysDiag");
            return false;
        }
        catch (ArgumentException) { return true; } // el padre ya terminó y liberó el mutex
        catch (InvalidOperationException) { return true; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex) _instancia?.ReleaseMutex();
        _instancia?.Dispose();
        base.OnExit(e);
    }
}
