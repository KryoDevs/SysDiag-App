using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using SysDiag.Core;
using SysDiag.Core.Diagnostics;
using SysDiag.Core.Drivers;
using SysDiag.Core.Storage;
using SysDiag.Diagnostics;
using SysDiag.Models;
using SysDiag.Services;
using Xunit;

namespace IntegrationTests;

public class SafetyRegressionTests
{
    [Fact]
    public void Settings_NormalizesValuesLoadedFromUntrustedJson()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("""
            {"SampleSeconds":0,"PingCount":-1,"EventDays":2147483647,"WheaDays":0,"HistorialMaximo":-2,"LogsMaximo":0}
            """)!.Normalizar();
        Assert.Equal(2, settings.SampleSeconds);
        Assert.Equal(5, settings.PingCount);
        Assert.Equal(90, settings.EventDays);
        Assert.Equal(1, settings.WheaDays);
        Assert.Equal(5, settings.HistorialMaximo);
        Assert.Equal(5, settings.LogsMaximo);
    }

    [Theory]
    [InlineData("thumbcache_32.db", true)]
    [InlineData("THUMBCACHE_256.DB", true)]
    [InlineData("settings.dat", false)]
    [InlineData("iconcache_32.db", false)]
    [InlineData("thumbcache_32.db.exe", false)]
    public void Cleanup_OnlyMatchesThumbnailDatabases(string name, bool expected) =>
        Assert.Equal(expected, CleanupSafety.IsThumbnail(name));

    [Fact]
    public void Cleanup_ContainmentDoesNotAcceptSiblingPrefixOrTraversal()
    {
        using var folder = new TemporaryDirectory();
        string root = Path.Combine(folder.Path, "cache");
        Assert.True(CleanupSafety.IsContainedFile(root, Path.Combine(root, "child", "a.tmp")));
        Assert.False(CleanupSafety.IsContainedFile(root, Path.Combine(folder.Path, "cache-other", "a.tmp")));
        Assert.False(CleanupSafety.IsContainedFile(root, Path.Combine(root, "..", "important.txt")));
        Assert.False(CleanupSafety.IsContainedFile(root, root));
    }

    [Fact]
    public void Cleanup_RejectsArbitraryRowsAndFilesOutsideTheirRoot()
    {
        using var folder = new TemporaryDirectory();
        string root = Directory.CreateDirectory(Path.Combine(folder.Path, "cache")).FullName;
        string outside = Path.Combine(folder.Path, "important.txt");
        File.WriteAllText(outside, "conservar");
        var row = new CleanupRow { Ruta = root, Ubicacion = "Caché falsificada", Items = new() { new(outside) } };
        CleanupModule.Clean(new DiagnosticReport(), new() { row }, CancellationToken.None);
        Assert.True(File.Exists(outside));
        Assert.False(CleanupSafety.TryDelete(root, outside, out _));
    }

    [Fact]
    public void Cleanup_DeletesApprovedFileByHandleButPreservesReadOnlyFiles()
    {
        using var folder = new TemporaryDirectory();
        string file = Path.Combine(folder.Path, "safe.tmp");
        File.WriteAllText(file, "1234");
        Assert.True(CleanupSafety.TryDelete(folder.Path, file, out long freed));
        Assert.Equal(4, freed);
        Assert.False(File.Exists(file));

        File.WriteAllText(file, "keep");
        File.SetAttributes(file, FileAttributes.ReadOnly);
        try
        {
            Assert.False(CleanupSafety.TryDelete(folder.Path, file, out _));
            Assert.True(File.Exists(file));
            Assert.True((File.GetAttributes(file) & FileAttributes.ReadOnly) != 0);
        }
        finally { File.SetAttributes(file, FileAttributes.Normal); }
    }

    [Fact]
    public void Cleanup_RejectsJunctionRootAndLinkedDescendant()
    {
        using var folder = new TemporaryDirectory();
        string root = Directory.CreateDirectory(Path.Combine(folder.Path, "cache")).FullName;
        string outside = Directory.CreateDirectory(Path.Combine(folder.Path, "outside")).FullName;
        File.WriteAllText(Path.Combine(outside, "important.txt"), "conservar");
        string link = Path.Combine(root, "link");
        var result = ProcessRunner.Run(ProcessRunner.CreateStartInfo(AppEnv.SystemTool("cmd"),
            new[] { "/c", "mklink", "/J", link, outside }));
        Assert.True(result.Success, result.Describe("mklink"));
        try
        {
            Assert.False(CleanupSafety.TryDelete(root, Path.Combine(link, "important.txt"), out _));
            Assert.False(CleanupSafety.TryDelete(link, Path.Combine(link, "important.txt"), out _));
            Assert.True(File.Exists(Path.Combine(outside, "important.txt")));
        }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public async Task ConsoleRunner_DrainsLargeStderrAndReportsNonZeroExit()
    {
        var result = await ProcessRunner.RunAsync(PowerShell("[Console]::Error.Write(('e' * 200000)); [Console]::Out.Write('ok'); exit 7"),
            TimeSpan.FromSeconds(20));
        Assert.False(result.Success);
        Assert.Equal(7, result.ExitCode);
        Assert.Equal("ok", result.StandardOutput);
        Assert.Equal(200000, result.StandardError.Length);
    }

    [Fact]
    public async Task ConsoleRunner_EnforcesTimeoutBeforeReadingToEnd()
    {
        var watch = Stopwatch.StartNew();
        var result = await ProcessRunner.RunAsync(PowerShell("Start-Sleep -Seconds 30"), TimeSpan.FromMilliseconds(300));
        Assert.True(result.TimedOut);
        Assert.False(result.Success);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"Timeout no respetado: {watch.Elapsed}");
    }

    [Fact]
    public async Task ConsoleRunner_CancellationIsNotReportedAsSuccess()
    {
        using var source = new CancellationTokenSource(300);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProcessRunner.RunAsync(
            PowerShell("Start-Sleep -Seconds 30"), TimeSpan.FromSeconds(40), source.Token));
    }

    [Fact]
    public void ConsoleRunner_CapturesStartupFailureWithoutInventingSuccess()
    {
        var result = ProcessRunner.Run(new ProcessStartInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe")));
        Assert.False(result.Success);
        Assert.Null(result.ExitCode);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public void DriverVerifier_NoAntivirusOrMissingIntegrityMustNeverPass()
    {
        var result = new DriverVerifier.Resultado
        {
            Firmado = true, CadenaValida = true, IntegridadValida = true,
            Sha256 = new string('A', 64), Antivirus = "no analizado (Defender no disponible)"
        };
        Assert.False(DriverVerifier.VerificacionSatisfactoria(result));
        result.Antivirus = "sin amenazas pendientes";
        result.IntegridadValida = false;
        Assert.False(DriverVerifier.VerificacionSatisfactoria(result));
        Assert.Contains("no ejecuta", DriverVerifier.Instalar("untrusted.exe"));
    }

    [Fact]
    public void Authenticode_RejectsUnsignedFile()
    {
        using var folder = new TemporaryDirectory();
        string path = Path.Combine(folder.Path, "unsigned.exe");
        File.WriteAllText(path, "No es un ejecutable firmado");
        Assert.NotEqual(0, AuthenticodeVerifier.VerifyFile(path));
    }

    [Fact]
    public void AtomicFile_ReplacesWholeFileAndLeavesNoTemporaryArtifacts()
    {
        using var folder = new TemporaryDirectory();
        string path = Path.Combine(folder.Path, "backup.json");
        AtomicFile.WriteAllText(path, "old");
        AtomicFile.WriteAllText(path, "new");
        Assert.Equal("new", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(folder.Path));
    }

    [Fact]
    public void ReportJson_RoundTripPreservesFindings()
    {
        var report = new DiagnosticReport();
        report.Add(Severity.Bad, "Seguridad", "Problema real", "Revisar");
        var loaded = JsonSerializer.Deserialize<DiagnosticReport>(JsonSerializer.Serialize(report))!;
        Assert.Single(loaded.Hallazgos);
        Assert.Equal("Problema real", loaded.Hallazgos[0].Message);
    }

    [Theory]
    [InlineData("en-US", "84.5 %", false)]
    [InlineData("es-CL", "84,5 %", false)]
    [InlineData("en-US", "86,5 %", true)]
    [InlineData("es-CL", "86.5 %", true)]
    public void MemoryRule_IsIndependentOfMachineCulture(string culture, string value, bool warning)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var report = new DiagnosticReport { RendimientoResumen = new() { new("RAM en uso", value) } };
            new MemoryRules().Evaluar(report);
            Assert.Equal(warning, report.Hallazgos.Count > 0);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void HealthScore_AcceptsMeasuredDataWithoutInventoryOrFindings()
    {
        var report = new DiagnosticReport { Red = new() { new() { Destino = "Router", Media = 4 } } };
        Assert.Equal(100, HealthScore.Calcular(report));
    }

    [Fact]
    public void Merge_RefreshesRecommendationsAfterEveryNewModule()
    {
        var accumulated = new DiagnosticReport();
        accumulated.MergeFrom(new DiagnosticReport { WiFi = new() { new("SSID", "Prueba") } });
        var network = new DiagnosticReport();
        network.Add(Severity.Bad, "Red", "Problema de red");
        accumulated.MergeFrom(network);
        Assert.Contains(accumulated.Recomendaciones, r => r.Titulo == "Revisar la red");
    }

    [Fact]
    public async Task Scan_RepeatedModuleReplacesOldFindingsAndClearsEmptyTables()
    {
        var previous = new DiagnosticReport
        {
            Red = new() { new() { Destino = "Viejo" } },
            WiFi = new() { new("SSID", "Vieja red") }
        };
        previous.Hallazgos.Add(new() { Area = "Red", Message = "Problema viejo", Severity = Severity.Bad, Modulo = "red" });
        var result = await new ScanService().EjecutarAsync(previous, new[] { new EmptyNetworkService() }, CancellationToken.None);
        Assert.Empty(result.Red);
        Assert.Empty(result.WiFi);
        Assert.DoesNotContain(result.Hallazgos, f => f.Message == "Problema viejo");
    }

    private sealed class EmptyNetworkService : IDiagnosticService
    {
        public string Clave => "red";
        public Task EjecutarAsync(DiagnosticReport report, CancellationToken token) => Task.CompletedTask;
    }

    private static ProcessStartInfo PowerShell(string script) => ProcessRunner.CreateStartInfo(
        Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
        new[] { "-NoProfile", "-NonInteractive", "-Command", script });
}

internal sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SysDiag-tests-" + Guid.NewGuid().ToString("N"));
    public TemporaryDirectory() => Directory.CreateDirectory(Path);
    public void Dispose() => Directory.Delete(Path, recursive: true);
}
