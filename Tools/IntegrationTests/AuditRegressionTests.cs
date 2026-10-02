using System.Security.AccessControl;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text.Json;
using System.Xml.Linq;
using SysDiag.Core;
using SysDiag.Core.Diagnostics;
using SysDiag.Core.Drivers;
using SysDiag.Core.Network;
using SysDiag.Core.Security;
using SysDiag.Core.Storage;
using SysDiag.Core.Windows;
using SysDiag.Models;
using SysDiag.Services;
using Xunit;

namespace IntegrationTests;

public class AuditRegressionTests
{
    private const string User = "test-user";
    private const string Machine = "test-machine";

    [Fact]
    public void Backup_PreservesFirstValuesAndOriginalPlanAcrossProfiles()
    {
        using var folder = new TemporaryDirectory();
        string file = Path.Combine(folder.Path, "state.json");
        var store = new OptimizationBackupStore(file, Machine, User);
        Guid original = Guid.NewGuid(), other = Guid.NewGuid();
        var state = store.ReadOrCreate(original);
        state.CapturePowerPlan(original, true, true, () => 2, () => 95);
        state.EfectosVisuales = new() { Existia = false };
        store.Write(state);
        state = store.ReadOrCreate(other);
        state.CapturePowerPlan(original, true, true, () => throw new Exception("No volver a capturar"), () => throw new Exception("No volver a capturar"));
        state.CapturePowerPlan(other, true, false, () => 1, () => throw new Exception("CPU no seleccionado"));
        store.Write(state);
        var copy = store.Read();
        Assert.Equal(original, copy.PlanEnergia);
        Assert.Equal(2u, copy.Planes[0].WifiPowerIndex);
        Assert.Equal(95u, copy.Planes[0].CpuMaxPercent);
        Assert.False(copy.EfectosVisuales.Existia);
        Assert.Equal(2, copy.Planes.Count);
        store.CompleteRestore();
        Assert.False(File.Exists(file));
        Assert.True(File.Exists(file + ".restaurado.json"));
        Assert.Equal(other, store.ReadOrCreate(other).PlanEnergia);
    }

    [Theory]
    [InlineData(0, 100u, "1.1.1.1", Machine, User)]
    [InlineData(2, 0u, "1.1.1.1", Machine, User)]
    [InlineData(2, 101u, "1.1.1.1", Machine, User)]
    [InlineData(2, 100u, "1.1.1.1 & echo exploit", Machine, User)]
    [InlineData(2, 100u, "::1", Machine, User)]
    [InlineData(2, 100u, "1.1.1.1", "another-machine", User)]
    [InlineData(2, 100u, "1.1.1.1", Machine, "another-user")]
    public void Backup_RejectsMalformedOrForeignState(int schema, uint cpu, string dns, string machine, string user)
    {
        using var folder = new TemporaryDirectory();
        var store = new OptimizationBackupStore(Path.Combine(folder.Path, "state.json"), Machine, User);
        var state = store.ReadOrCreate(Guid.NewGuid());
        state.SchemaVersion = schema;
        state.Planes.Add(new() { Plan = state.PlanEnergia, CpuMaxPercent = cpu });
        state.Dns = new() { Interfaz = Guid.NewGuid(), Servidores = new() { dns } };
        state.Equipo = machine; state.UsuarioSid = user;
        Assert.Throws<InvalidDataException>(() => store.Write(state));
        Assert.Empty(Directory.GetFiles(folder.Path));
    }

    [Fact]
    public void Backup_RejectsDuplicatePlansAndContradictoryDhcp()
    {
        using var folder = new TemporaryDirectory();
        var store = new OptimizationBackupStore(Path.Combine(folder.Path, "state.json"), Machine, User);
        var state = store.ReadOrCreate(Guid.NewGuid());
        state.Planes.Add(new() { Plan = state.PlanEnergia }); state.Planes.Add(new() { Plan = state.PlanEnergia });
        Assert.Throws<InvalidDataException>(() => store.Write(state));
        state.Planes.Clear();
        state.Dns = new() { Interfaz = Guid.NewGuid(), Automatico = true, Servidores = new() { "1.1.1.1" } };
        Assert.Throws<InvalidDataException>(() => store.Write(state));
    }

    [Fact]
    public void Options_AreOptInAndRejectConflictsBeforeExecution()
    {
        var options = new OptimizeModule.Options();
        Assert.False(options.FlushDns || options.FlushArp || options.PublicDns || options.HighPerformancePlan);
        options.WifiPowerSave = options.WifiMaxPerformance = true;
        Assert.Throws<ArgumentException>(options.Validate);
        options.WifiPowerSave = false; options.CpuMaxPercent = 0;
        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }

    [Fact]
    public void AtomicFile_AbortsIfProtectionFailsWithoutReplacingOriginal()
    {
        using var folder = new TemporaryDirectory();
        string file = Path.Combine(folder.Path, "important.json");
        File.WriteAllText(file, "original");
        Assert.Throws<UnauthorizedAccessException>(() => AtomicFile.WriteAllText(file, "new",
            beforeReplace: _ => throw new UnauthorizedAccessException("No se pudo proteger")));
        Assert.Equal("original", File.ReadAllText(file));
        Assert.Single(Directory.GetFiles(folder.Path));
    }

    [Fact]
    public void ProtectedBackup_UsesAdminOwnershipAndAclBeforeAtomicRename()
    {
        using var folder = new TemporaryDirectory();
        string root = Path.Combine(folder.Path, "private");
        SecureBackupDirectory.ProtectDirectory(root);
        string file = Path.Combine(root, "state.json");
        AtomicFile.WriteAllText(file, "{}", beforeReplace: SecureBackupDirectory.ProtectTemporaryFile);
        SecureBackupDirectory.VerifyFile(file);
        var security = new FileInfo(file).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
        Assert.Equal(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), security.GetOwner(typeof(SecurityIdentifier)));
        Assert.True(security.AreAccessRulesProtected);
        var user = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        Assert.DoesNotContain(security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>(),
            rule => rule.IdentityReference.Equals(user) && rule.AccessControlType == AccessControlType.Allow);
    }

    [Fact]
    public void NativePower_ReadsExistingPlanWithoutChangingSettings()
    {
        Guid active = PowerSettings.ActivePlan();
        Assert.NotEqual(Guid.Empty, active);
        Assert.Contains(active, PowerSettings.Plans());
        uint cpu = PowerSettings.ReadAc(active, PowerSettings.ProcessorSubgroup, PowerSettings.ProcessorMaximum);
        Assert.InRange(cpu, 1u, 100u);
    }

    [Fact]
    public void Authenticode_RejectsModifiedSignedPeEvenThoughItsCertificateIsStillReadable()
    {
        using var folder = new TemporaryDirectory();
        string root = Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
        string original = Path.Combine(root, "dotnet.exe");
        Assert.True(File.Exists(original), "No se encontró el host firmado de .NET instalado por CI.");
        Assert.Equal(0, AuthenticodeVerifier.VerifyFile(original));
        byte[] bytes = File.ReadAllBytes(original);
        int pe = BitConverter.ToInt32(bytes, 0x3c);
        int section = pe + 24 + BitConverter.ToUInt16(bytes, pe + 20);
        int raw = BitConverter.ToInt32(bytes, section + 20);
        bytes[raw + 32] ^= 1; // cambia código, NO la tabla del certificado
        string modified = Path.Combine(folder.Path, "modified.exe");
        File.WriteAllBytes(modified, bytes);
        using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(modified));
        Assert.NotEmpty(certificate.Subject);
        Assert.NotEqual(0, AuthenticodeVerifier.VerifyFile(modified));
    }

    [Theory]
    [InlineData(2d, 50d, 40d, 0d, Severity.Bad)]
    [InlineData(2d, 0d, 75d, 0d, Severity.Bad)]
    [InlineData(0d, 0d, 75d, 0d, Severity.Warn)]
    [InlineData(0d, 90d, 40d, 0d, Severity.Bad)]
    [InlineData(0d, 0d, 40d, 1d, Severity.Bad)]
    [InlineData(0d, 0d, 40d, 0d, Severity.Ok)]
    public void Storage_NeverDowngradesCriticalHealth(double health, double wear, double temperature, double errors, Severity expected) =>
        Assert.Equal(expected, StorageModule.AssessHealth(health, wear, temperature, errors));

    [Fact]
    public void MissingWmiPropertiesAndStorageHealthAreUnknownNotHealthy()
    {
        var row = new WmiRow(new Dictionary<string, object>());
        Assert.False(Wmi.TryNum(row, "HealthStatus", out _));
        Assert.False(Wmi.TryBool(row, "AntivirusEnabled", out _));
        Assert.Equal(Severity.Warn, StorageModule.AssessHealth(null, null, null, null));
    }

    [Theory]
    [InlineData("Domain Profile Settings\nState ON\nPrivate Profile Settings\nState ON\nPublic Profile Settings\nState ON", Severity.Ok)]
    [InlineData("Perfil de dominio\nEstado ACTIVADO\nPerfil privado\nEstado ACTIVADO\nPerfil público\nEstado ACTIVADO", Severity.Ok)]
    [InlineData("Domain Profile Settings\nState ON", Severity.Warn)]
    [InlineData("Domain Profile Settings\nState OFF", Severity.Bad)]
    [InlineData("error: access denied", Severity.Warn)]
    public void Firewall_RequiresAllProfilesBeforeClaimingCompleteProtection(string output, Severity expected) =>
        Assert.Equal(expected, SecurityModule.ParseFirewall(output).Nivel);

    [Theory]
    [InlineData(true, true, null, Severity.Ok)]
    [InlineData(false, false, 0u, Severity.Ok)]
    [InlineData(false, false, 2u, Severity.Bad)]
    [InlineData(false, false, null, Severity.Warn)]
    [InlineData(true, false, null, Severity.Warn)]
    public void Defender_ConsultsSecurityCenterBeforeInferringMissingProtection(bool realtime, bool engine, uint? health, Severity expected) =>
        Assert.Equal(expected, SecurityModule.AssessDefender(realtime, engine, health).Nivel);

    [Theory]
    [InlineData(1001, "Microsoft-Windows-WER-SystemErrorReporting", true)]
    [InlineData(1001, "Unrelated-Service", false)]
    [InlineData(41, "Microsoft-Windows-Kernel-Power", true)]
    [InlineData(41, "Another-Provider", false)]
    [InlineData(18, "Microsoft-Windows-WHEA-Logger", true)]
    [InlineData(18, "Service Control Manager", false)]
    [InlineData(129, "storahci", true)]
    [InlineData(129, "Unknown-Provider", false)]
    public void Events_IdsAreScopedToProviders(int id, string provider, bool expected) =>
        Assert.Equal(expected, SystemEventCatalog.IsKnownEvent(id, provider));

    [Fact]
    public void EventQueries_UseStructuredXmlAndBoundedDateWindow()
    {
        var query = XElement.Parse(SystemEventCatalog.QueryXml(int.MaxValue));
        Assert.Equal("QueryList", query.Name.LocalName);
        var selects = query.Descendants("Select").ToList();
        Assert.Equal(9, selects.Count);
        Assert.All(selects, select => { Assert.Contains("Provider[@Name=", select.Value); Assert.Contains("7776000000", select.Value); });
    }

    [Theory]
    [InlineData("=HYPERLINK(\"https://example.invalid\")")]
    [InlineData("+cmd")]
    [InlineData("-1+2")]
    [InlineData("@SUM(1)")]
    [InlineData("  =formula")]
    [InlineData("\ttext")]
    [InlineData("\ntext")]
    public void Csv_NeutralizesSpreadsheetFormulaInjection(string field) =>
        Assert.StartsWith("'", Exporter.EscapeCsvField(field).Trim('"'));

    [Fact]
    public void Csv_EscapesCarriageReturnQuotesAndSeparator() =>
        Assert.Equal("\"a;\"\"b\"\"\rc\"", Exporter.EscapeCsvField("a;\"b\"\rc"));

    [Fact]
    public void Exports_DoNotOverwriteAndHtmlUsesRecordedDurationAndEscaping()
    {
        using var folder = new TemporaryDirectory();
        var report = Report(DateTime.UtcNow.AddDays(-2), "red");
        report.Fin = report.Inicio.AddSeconds(7);
        report.EstadoEjecucion = "Cancelado";
        report.Equipo = "<script>alert(1)</script>";
        string first = Exporter.ToJson(report, folder.Path), second = Exporter.ToJson(report, folder.Path);
        Assert.NotEqual(first, second);
        string html = File.ReadAllText(ReportBuilder.Build(report, folder.Path));
        Assert.Contains("7 s", html);
        Assert.Contains("Cancelado", html);
        Assert.Contains("Diagnóstico parcial", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Equal(report.Hallazgos.Count, Exporter.Cargar(first).Hallazgos.Count);
    }

    [Fact]
    public void History_UsesMeasurementDateSkipsCorruptionAndComparesSameCoverage()
    {
        using var folder = new TemporaryDirectory();
        var date = DateTime.UtcNow.AddDays(-5);
        var red = Report(date, "red"); red.Add(Severity.Warn, "Red", "old warning", modulo: "red");
        var full = Report(date.AddDays(1), "red", "seguridad"); full.Add(Severity.Bad, "Seguridad", "different coverage", modulo: "seguridad");
        Assert.True(Exporter.Archivar(red, folder.Path)); Assert.True(Exporter.Archivar(full, folder.Path));
        File.SetLastWriteTimeUtc(Exporter.Listar(historialDirectory: folder.Path).Single(e => e.Fecha == date).Archivo, DateTime.UtcNow.AddDays(10));
        File.WriteAllText(Path.Combine(folder.Path, "newest-corrupt.json"), "{invalid");
        var history = Exporter.Listar(historialDirectory: folder.Path);
        Assert.Equal(2, history.Count);
        Assert.Equal(full.Inicio, history[0].Fecha);
        Assert.Equal(95, Exporter.PuntajeAnterior(date.AddDays(3), new[] { "red" }, folder.Path));
        Assert.Equal(85, Exporter.PuntajeAnterior(date.AddDays(3), new[] { "red", "seguridad" }, folder.Path));
    }

    [Fact]
    public void Archive_TwoRunsInSameInstantHaveDifferentFiles()
    {
        using var folder = new TemporaryDirectory();
        var date = DateTime.UtcNow;
        Assert.True(Exporter.Archivar(Report(date, "red"), folder.Path));
        Assert.True(Exporter.Archivar(Report(date, "red"), folder.Path));
        Assert.Equal(2, Directory.GetFiles(folder.Path, "*.json").Length);
    }

    [Fact]
    public async Task Scan_CancellationPreservesPreviousDataAndDoesNotCommitUnfinishedModule()
    {
        var report = Report(DateTime.UtcNow, "red"); report.Red.Add(new() { Destino = "previous", Media = 25 });
        using var cancellation = new CancellationTokenSource();
        var service = new DelegateDiagnosticService("red", (scratch, token) =>
        {
            scratch.Red.Add(new() { Destino = "unfinished" }); cancellation.Cancel(); return Task.CompletedTask;
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ScanService().EjecutarAsync(report, new[] { service }, cancellation.Token));
        Assert.Equal("previous", Assert.Single(report.Red).Destino);
        Assert.Equal("Cancelado", report.EstadoEjecucion);
    }

    [Fact]
    public async Task Network_PreCanceledOperationsDoNotSendPackets()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => NetworkModule.PingUnaVez("127.0.0.1", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => NetworkModule.MeasureAsync("local", "127.0.0.1", 1, cancellation.Token));
    }

    [Fact]
    public void ComWorker_ReentrantCallsExceptionsAndDisposalDoNotDeadlock()
    {
        var worker = new ComWorker("SysDiag.Test.COM");
        Assert.Equal(42, worker.Run(() => worker.Run(() => 42)));
        Assert.Throws<InvalidOperationException>(() => worker.Run<int>(() => throw new InvalidOperationException("expected")));
        worker.Dispose();
        Assert.Throws<ObjectDisposedException>(() => worker.Run(() => 1));
    }

    [Fact]
    public void DriverSelection_UsesIdentityNotJustAnIndex()
    {
        Guid current = Guid.NewGuid();
        var row = new DriverUpdateRow { Indice = 0, UpdateId = current.ToString() };
        Assert.True(DriverUpdateModule.MatchesUpdate(row, current.ToString(), 1));
        Assert.False(DriverUpdateModule.MatchesUpdate(row, Guid.NewGuid().ToString(), 1));
        row.Indice = 1;
        Assert.False(DriverUpdateModule.MatchesUpdate(row, current.ToString(), 1));
    }

    [Fact]
    public void LoadedJson_NormalizesNullRowsAndInvalidSeverity()
    {
        var report = JsonSerializer.Deserialize<DiagnosticReport>("""
            {"Sistema":[null],"RendimientoResumen":[null],"Red":null,"Hallazgos":[null,{"Severity":999,"Area":null,"Message":null}]}
            """)!;
        Assert.Empty(report.Sistema); Assert.Empty(report.RendimientoResumen); Assert.Empty(report.Red);
        Assert.Equal(Severity.Warn, Assert.Single(report.Hallazgos).Severity);
    }

    [Theory]
    [InlineData("Vendor.Package", true)]
    [InlineData("Vendor.Package & calc", false)]
    [InlineData("--all", false)]
    [InlineData("Bad\"Id", false)]
    public void Winget_RejectsUnsafeIdentifiers(string id, bool expected) => Assert.Equal(expected, UpdateModule.IsSafePackageId(id));

    [Fact]
    public void Winget_ParsesKnownTableAndRejectsUnknownOutput()
    {
        string header = $"{ "Name",-20}{ "Id",-25}{ "Version",-14}{ "Available",-14}Source";
        string row = $"{ "Test app",-20}{ "Vendor.Package",-25}{ "1.0",-14}{ "2.0",-14}winget";
        Assert.True(UpdateModule.TryParseTable(header + "\n" + new string('-', 90) + "\n" + row, out var rows));
        Assert.Equal("Vendor.Package", Assert.Single(rows).Id);
        Assert.False(UpdateModule.TryParseTable("network error", out _));
        Assert.False(UpdateModule.TryParseTable("One Two Three Four\n---------------------\n", out _));
    }

    [Fact]
    public void Coverage_InventoryDisksAreNotACompletedSmartScan()
    {
        var report = Report(DateTime.UtcNow, "red");
        report.Discos.Add(new());
        Assert.Contains("Almacenamiento", report.ModulosFaltantes());
    }

    private static DiagnosticReport Report(DateTime date, params string[] modules)
    {
        var report = new DiagnosticReport { Inicio = date };
        foreach (string module in modules) report.ModulosCompletados[module] = date;
        report.Sistema.Add(new("Equipo", "Equipo sintético"));
        return report;
    }
}
