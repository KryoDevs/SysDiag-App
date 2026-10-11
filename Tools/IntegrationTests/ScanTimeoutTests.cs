using System.Threading;
using System.Threading.Tasks;
using SysDiag.Models;
using SysDiag.Services;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// El límite por módulo de <see cref="ScanService"/> no tenía ninguna prueba: el código existía
/// y el aviso de «no se pudo medir» era visible para el usuario, pero nada comprobaba que un
/// módulo colgado se omitiera sin detener a los demás. Estas pruebas usan límites de milisegundos
/// inyectados por el constructor; con los de producción tardarían minutos.
/// </summary>
public class ScanTimeoutTests
{
    [Fact]
    public async Task ModuloQueNoResponde_SeOmiteYLosDemasSiguenMidiendose()
    {
        var limites = new Dictionary<string, TimeSpan> { ["red"] = TimeSpan.FromMilliseconds(200) };
        var servicio = new ScanService(limites, TimeSpan.FromSeconds(30));

        var result = await servicio.EjecutarAsync(null!,
            new IDiagnosticService[] { new Colgado("red"), new SeguridadOk() }, CancellationToken.None);

        Assert.Contains("red", result.ModulosColgados);
        Assert.DoesNotContain("seguridad", result.ModulosColgados);
        // El módulo que sí respondió conserva sus hallazgos.
        Assert.Contains(result.Hallazgos, f => f.Message == "Seguridad OK" && f.Modulo == "seguridad");
        Assert.Equal("Completado", result.EstadoEjecucion);
    }

    [Fact]
    public async Task ModuloOmitido_DejaUnAvisoVisibleYNoUnModuloVacioSinExplicacion()
    {
        var limites = new Dictionary<string, TimeSpan> { ["red"] = TimeSpan.FromMilliseconds(200) };
        var servicio = new ScanService(limites, TimeSpan.FromSeconds(30));

        var result = await servicio.EjecutarAsync(null!,
            new IDiagnosticService[] { new Colgado("red") }, CancellationToken.None);

        // Un módulo que falta sin avisar se lee como «no hay problemas de red».
        Assert.Contains(result.Hallazgos, f =>
            f.Severity == Severity.Warn && f.AccionId == DiagnosticReport.MarcaColgado && f.Message.Contains("Red y latencia"));
        Assert.Contains("Red y latencia", result.ResumenColgados());
    }

    [Fact]
    public async Task AvisoDeUnaCorridaAnterior_SeRetiraCuandoEseModuloResponde()
    {
        var limites = new Dictionary<string, TimeSpan> { ["red"] = TimeSpan.FromMilliseconds(200) };
        var servicio = new ScanService(limites, TimeSpan.FromSeconds(30));

        var primera = await servicio.EjecutarAsync(null!,
            new IDiagnosticService[] { new Colgado("red") }, CancellationToken.None);
        Assert.Contains(primera.Hallazgos, f => f.AccionId == DiagnosticReport.MarcaColgado);

        var segunda = await servicio.EjecutarAsync(primera,
            new IDiagnosticService[] { new SeguridadOk() }, CancellationToken.None);

        // Un aviso que ya no es cierto es peor que no avisar.
        Assert.DoesNotContain(segunda.Hallazgos, f => f.AccionId == DiagnosticReport.MarcaColgado);
        Assert.Empty(segunda.ModulosColgados);
    }

    [Fact]
    public void LimitePara_UsaElValorInyectadoYElDefectoParaLosDemas()
    {
        var servicio = new ScanService(
            new Dictionary<string, TimeSpan> { ["red"] = TimeSpan.FromSeconds(7) },
            TimeSpan.FromSeconds(9));

        Assert.Equal(TimeSpan.FromSeconds(7), servicio.LimitePara("red"));
        Assert.Equal(TimeSpan.FromSeconds(9), servicio.LimitePara("otro"));
        Assert.Equal(TimeSpan.FromSeconds(9), servicio.LimitePara(null!));
    }

    [Fact]
    public void LimitesDeProduccion_CubrenLosModulosConocidos()
    {
        // Si se añade un módulo sin límite, cae en el defecto de 2 minutos sin que nadie lo note.
        foreach (var clave in DiagnosticReport.NombresModulos.Keys)
        {
            Assert.True(ScanService.LimitesPorModulo.ContainsKey(clave), $"Falta el límite de «{clave}».");
        }
    }

    /// <summary>Simula un recolector que nunca termina, salvo que se le cancele.</summary>
    private sealed class Colgado : IDiagnosticService
    {
        public Colgado(string clave) => Clave = clave;
        public string Clave { get; }
        public Task EjecutarAsync(DiagnosticReport report, CancellationToken token) => Task.Delay(Timeout.Infinite, token);
    }

    private sealed class SeguridadOk : IDiagnosticService
    {
        public string Clave => "seguridad";
        public Task EjecutarAsync(DiagnosticReport report, CancellationToken token)
        {
            report.Add(Severity.Ok, "Seguridad", "Seguridad OK");
            return Task.CompletedTask;
        }
    }
}
