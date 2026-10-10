using SysDiag.Core.Diagnostics;
using SysDiag.Core.Hardware;
using SysDiag.Core.Network;
using SysDiag.Core.Windows;
using SysDiag.Models;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// Regresiones de la segunda pasada de auditoría. Cubren lógica pura (sin WMI, COM ni red) para que
/// corran en CI sin depender del hardware del runner.
/// </summary>
public class AuditRound2RegressionTests
{
    [Theory]
    [InlineData(0d)]          // zona sin lectura: antes aparecía como «0 °C»
    [InlineData(-5d)]
    [InlineData(double.NaN)]
    [InlineData(10_000_000d)] // lectura físicamente imposible
    public void ThermalZone_IgnoresMissingOrImpossibleReadings(double raw) =>
        Assert.Null(ThermalModule.ConvertirTemperaturaAcpi(raw));

    [Fact]
    public void ThermalZone_ConvertsDecikelvinToCelsius() =>
        Assert.Equal(50.0, ThermalModule.ConvertirTemperaturaAcpi(3231.5)!.Value, 1);

    [Theory]
    [InlineData("No applicable upgrade found.", true)]
    [InlineData("No se encontraron actualizaciones aplicables.", true)]
    [InlineData("Name   Id   Version   Available   Source\n-----\nApp  Vendor.App  1.0  2.0  winget", false)]
    [InlineData("", false)]
    public void Winget_RecognisesEmptyCatalogRegardlessOfExitCode(string output, bool expected) =>
        Assert.Equal(expected, UpdateModule.EsSinActualizaciones(output));

    [Fact]
    public void WifiScan_KeepsSignalAndChannelOfEachAccessPointWhateverTheOrder()
    {
        // Windows imprime «Señal» antes o después de «Canal» según el adaptador y el idioma.
        const string raw =
            "Interface name : Wi-Fi\r\n" +
            "There are 3 networks currently visible.\r\n\r\n" +
            "SSID 1 : CasaNet\r\n" +
            "    Network type            : Infrastructure\r\n\r\n" +
            "    BSSID 1                 : aa:bb:cc:00:00:01\r\n" +
            "         Signal             : 88%\r\n" +
            "         Radio type         : 802.11ax\r\n" +
            "         Channel            : 36\r\n\r\n" +
            "    BSSID 2                 : aa:bb:cc:00:00:02\r\n" +
            "         Channel            : 6\r\n" +
            "         Signal             : 40%\r\n\r\n" +
            "SSID 2 : Vecina\r\n" +
            "    BSSID 1                 : cc:dd:ee:00:00:03\r\n" +
            "         Señal              : 70%\r\n" +
            "         Canal              : 36\r\n";

        var redes = NetworkModule.ParseRedesCercanas(raw);

        Assert.Equal(3, redes.Count);
        Assert.Equal(("CasaNet", 36, 88), (redes[0].Ssid, redes[0].Canal, redes[0].SenalPct));
        Assert.Equal(("CasaNet", 6, 40), (redes[1].Ssid, redes[1].Canal, redes[1].SenalPct));
        Assert.Equal(("Vecina", 36, 70), (redes[2].Ssid, redes[2].Canal, redes[2].SenalPct));
        Assert.Equal("5 GHz", redes[0].Banda);
        Assert.Equal("2.4 GHz", redes[1].Banda);
    }

    [Fact]
    public void WifiScan_DropsAccessPointsWithoutChannel()
    {
        const string raw =
            "SSID 1 : SinCanal\n" +
            "    BSSID 1                 : aa:bb:cc:00:00:09\n" +
            "         Signal             : 55%\n";

        Assert.Empty(NetworkModule.ParseRedesCercanas(raw));
        Assert.Empty(NetworkModule.ParseRedesCercanas(""));
    }

    [Fact]
    public void History_TrendOnlyUsesRunsWithTheSameCoverage()
    {
        using var folder = new TemporaryDirectory();
        var date = DateTime.UtcNow.AddDays(-5);
        var parcial = ReportFor(date, "red");
        var completo = ReportFor(date.AddDays(1), "red", "seguridad");
        completo.Add(Severity.Bad, "Seguridad", "Firewall apagado", modulo: "seguridad");
        Assert.True(Exporter.Archivar(parcial, folder.Path));
        Assert.True(Exporter.Archivar(completo, folder.Path));

        var mismaCobertura = Exporter.Historial(30, folder.Path, new[] { "red" });
        var todas = Exporter.Historial(30, folder.Path);

        Assert.Single(mismaCobertura);
        Assert.Equal(100, mismaCobertura[0].Puntaje);
        Assert.Equal(2, todas.Count);
    }

    [Fact]
    public void ArchiveCopy_RecordsOnlyTheModulesMeasuredInThatRun()
    {
        using var folder = new TemporaryDirectory();
        var date = DateTime.UtcNow.AddDays(-1);
        // El reporte fusionado conserva módulos de corridas anteriores: esa corrida solo midió «red».
        var merged = ReportFor(date, "red", "seguridad", "termicas");

        var copia = merged.ParaArchivo(new[] { "red" });

        Assert.Single(copia.ModulosCompletados);
        Assert.Equal(3, merged.ModulosCompletados.Count);
        Assert.True(Exporter.Archivar(copia, folder.Path));
        var entrada = Assert.Single(Exporter.Listar(historialDirectory: folder.Path));
        Assert.Equal(new[] { "red" }, entrada.Modulos);
    }

    private static DiagnosticReport ReportFor(DateTime date, params string[] modules)
    {
        var report = new DiagnosticReport { Inicio = date };
        foreach (string module in modules) report.ModulosCompletados[module] = date;
        report.Sistema.Add(new("Equipo", "Equipo sintético"));
        return report;
    }
}
