using System.IO;
using SysDiag.Core.Diagnostics;
using SysDiag.Core.Windows;
using SysDiag.Models;
using SysDiag.Services;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        try
        {
            if (args.Contains("--self-test")) return await SelfTest();
            string? output = null;
            for (int index = 0; index < args.Length; index++)
            {
                if (args[index] != "--output" || index + 1 >= args.Length)
                    throw new ArgumentException("Uso: HeadlessRunner [--output carpeta] o --self-test");
                output = Path.GetFullPath(args[++index]);
            }
            SettingsService.Aplicar(SettingsService.Cargar());
            Console.WriteLine("Diagnóstico de solo lectura: no se limpiarán archivos ni se aplicarán ajustes.");
            IDiagnosticService[] services =
            {
                new NetworkService(), new PerformanceService(), new HardwareService(), new StorageService(),
                new SecurityService(), new StabilityService(), new DriverService(), new UpdateService(),
                new StartupService(), new CleanupService()
            };
            var report = await new ScanService().EjecutarAsync(null!, services, cancellation.Token);
            string file = Exporter.ToJson(report, output!);
            Console.WriteLine($"Diagnóstico guardado: {file}");
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Diagnóstico cancelado o timeout global de 10 minutos. No se anunció como completado.");
            return 1;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 2; }
    }

    private static async Task<int> SelfTest()
    {
        string root = Path.Combine(Path.GetTempPath(), "SysDiag-headless-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var report = await new ScanService().EjecutarAsync(null!, new IDiagnosticService[] { new FakePerformance() }, CancellationToken.None);
            var loaded = Exporter.Cargar(Exporter.ToJson(report, root));
            if (loaded.Hallazgos.Count != 2 || loaded.Sistema.Count != 1
                || loaded.Hallazgos.Any(f => f.Modulo != "rendimiento"))
                throw new InvalidOperationException("El runner perdió inventario, reglas o hallazgos durante el round-trip.");
            Console.WriteLine("SYSDIAG_HEADLESS_SELF_TEST_OK");
            return 0;
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    // Solo para --self-test: nunca se usa como fuente de datos de un diagnóstico normal.
    private sealed class FakePerformance : IDiagnosticService
    {
        public string Clave => "rendimiento";
        public Task EjecutarAsync(DiagnosticReport report, CancellationToken token)
        {
            report.Equipo = "Equipo sintético de autotest";
            report.Sistema.Add(new("Equipo", report.Equipo));
            report.RendimientoResumen.Add(new("RAM en uso", "90.5 %"));
            report.TopCpu.Add(new() { Proceso = "Proceso sintético", CpuPct = 60 });
            return Task.CompletedTask;
        }
    }
}
