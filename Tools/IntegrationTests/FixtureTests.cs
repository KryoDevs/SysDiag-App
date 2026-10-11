using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SysDiag.Core;
using SysDiag.Core.Diagnostics;
using SysDiag.Core.Recommendations;
using SysDiag.Models;
using Xunit;

namespace IntegrationTests;

public class FixtureTests
{
    [Fact]
    public void FixtureContainsCriticalFindings()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "diagnostico_fixture.json");
        Assert.True(File.Exists(path), $"Fixture not found: {path}");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("Hallazgos", out var hallazgos), "No hallazgos array");

        bool anyBad = false;
        foreach (var h in hallazgos.EnumerateArray())
        {
            if (h.TryGetProperty("Severity", out var s) && s.GetString() == "Bad")
            {
                anyBad = true;
                break;
            }
        }

        Assert.True(anyBad, "Fixture must contain at least one finding with Severity == Bad");
    }

    [Fact]
    public void HealthScore_CalculaPuntajeEsperado()
    {
        var report = new DiagnosticReport();
        report.Add(Severity.Bad, "Red", "latencia alta");
        report.Add(Severity.Bad, "Estabilidad", "reinicios");
        report.Add(Severity.Warn, "Térmicas", "temperatura elevada");

        var score = HealthScore.Calcular(report);

        Assert.Equal(65, score);
        Assert.Equal("Con reparos", HealthScore.Etiqueta(score));
        Assert.Equal(Severity.Warn, HealthScore.Nivel(score));
    }

    [Fact]
    public void ReplaceModuleFrom_ConservaLosDemasModulosYCombinaHallazgos()
    {
        var previo = new DiagnosticReport();
        previo.Arranque = new List<StartupRow> { new() { Nombre = "App previa", Origen = "Registro" } };
        previo.Hallazgos.Add(new Finding { Area = "Arranque", Severity = Severity.Warn, Message = "Aviso previo", Modulo = "arranque" });

        var parcial = new DiagnosticReport();
        parcial.Red = new List<LatencyResult>
        {
            new() { Destino = "Router", Media = 12, Estado = Severity.Ok }
        };
        parcial.Hallazgos.Add(new Finding { Area = "Red", Severity = Severity.Bad, Message = "Latencia alta" });

        previo.ReplaceModuleFrom(parcial, "red");

        Assert.Equal(2, previo.Hallazgos.Count);
        Assert.Equal("Router", previo.Red[0].Destino);
        Assert.Equal("App previa", previo.Arranque[0].Nombre);
        Assert.Contains(previo.Hallazgos, h => h.Area == "Red" && h.Severity == Severity.Bad);
    }

    [Fact]
    public void RecommendationEngine_GeneraRecomendacionesDesdeHallazgos()
    {
        var report = new DiagnosticReport();
        report.Add(Severity.Bad, "Red", "latencia alta");
        report.Arranque = new List<StartupRow>();
        for (int i = 0; i < 25; i++)
            report.Arranque.Add(new StartupRow { Nombre = $"App {i}", Origen = "Registro" });
        report.RendimientoResumen = new List<KeyValueRow>
        {
            new("RAM en uso", "86 % (7 GB de 8 GB)"),
            new("CPU total", "78 %")
        };

        var recomendaciones = RecommendationEngine.Generate(report);

        Assert.NotEmpty(recomendaciones);
        Assert.Contains(recomendaciones, r => r.Titulo.Contains("red", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(recomendaciones, r => r.Titulo.Contains("Windows", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(recomendaciones, r => r.Titulo.Contains("RAM", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HealthScore_RespetaUmbralesYSinDatos()
    {
        Assert.Equal(-1, HealthScore.Calcular(new DiagnosticReport()));
        Assert.Equal("sin datos", HealthScore.Etiqueta(-1));
        Assert.Equal(Severity.Ok, HealthScore.Nivel(-1));

        var puntajeBajo = new DiagnosticReport();
        puntajeBajo.Add(Severity.Bad, "Red", "latencia alta");
        puntajeBajo.Add(Severity.Bad, "Sistema", "reinicio frecuente");
        puntajeBajo.Add(Severity.Bad, "Estabilidad", "fallos");

        var score = HealthScore.Calcular(puntajeBajo);
        Assert.Equal(55, score);
        Assert.Equal("Con reparos", HealthScore.Etiqueta(score));
        Assert.Equal(Severity.Warn, HealthScore.Nivel(score));

        var puntajeMedio = new DiagnosticReport();
        puntajeMedio.Add(Severity.Warn, "Térmicas", "temperatura elevada");
        puntajeMedio.Add(Severity.Bad, "Software", "actualizaciones pendientes");

        var scoreMedio = HealthScore.Calcular(puntajeMedio);
        Assert.Equal(80, scoreMedio);
        Assert.Equal("Aceptable", HealthScore.Etiqueta(scoreMedio));
        Assert.Equal(Severity.Warn, HealthScore.Nivel(scoreMedio));
    }

    [Fact]
    public void ReplaceModuleFrom_ModuloVacioBorraSoloSuTabla()
    {
        var previo = new DiagnosticReport();
        previo.WiFi = new List<KeyValueRow> { new() { Clave = "SSID", Valor = "Casa" } };
        previo.Arranque = new List<StartupRow> { new() { Nombre = "App previa", Origen = "Registro" } };
        previo.Hallazgos.Add(new Finding { Area = "Arranque", Severity = Severity.Warn, Message = "Aviso previo", Modulo = "arranque" });

        // Un «Red» que terminó sin SSID sustituye la tabla de red: no deja la del diagnóstico anterior.
        previo.ReplaceModuleFrom(new DiagnosticReport(), "red");

        Assert.Empty(previo.WiFi);
        Assert.Single(previo.Hallazgos);
        Assert.Equal("App previa", previo.Arranque[0].Nombre);
    }

    [Fact]
    public void ReplaceModuleFrom_NoDuplicaHallazgosRepetidos()
    {
        var previo = new DiagnosticReport();
        previo.Add(Severity.Warn, "Sistema", "Aviso previo", modulo: "arranque");

        var parcial = new DiagnosticReport();
        parcial.Add(Severity.Bad, "Red", "Latencia alta");
        parcial.Add(Severity.Bad, "Red", "Latencia alta");

        previo.ReplaceModuleFrom(parcial, "red");

        Assert.Equal(2, previo.Hallazgos.Count);
        Assert.Contains(previo.Hallazgos, h => h.Area == "Sistema" && h.Message == "Aviso previo");
        Assert.Contains(previo.Hallazgos, h => h.Area == "Red" && h.Message == "Latencia alta");
    }

    [Fact]
    public void ReporteVacio_ExplicaQueFaltaDatos()
    {
        var report = new DiagnosticReport();

        Assert.False(report.TieneDatosRelevantes());
        Assert.Contains("admin", report.ResumenEstado(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReporteConDatosDeUnModulo_SeConsideraConDatos()
    {
        var report = new DiagnosticReport();
        report.Red = new List<LatencyResult>
        {
            new() { Destino = "Router", Media = 12, Estado = Severity.Ok }
        };

        Assert.True(report.TieneDatosRelevantes());
        Assert.Contains("completó", report.ResumenEstado(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReporteParcial_ListaModulosFaltantesBajoLosReales()
    {
        var report = new DiagnosticReport();
        report.Red = new List<LatencyResult>
        {
            new() { Destino = "Router", Media = 12, Estado = Severity.Ok }
        };

        var faltantes = report.ModulosFaltantes();

        Assert.Contains("Rendimiento", faltantes);
        Assert.DoesNotContain("Red y latencia", faltantes);
        Assert.Contains("Térmicas y energía", faltantes);
    }

    [Fact]
    public void ReporteSinDatos_MuestraQuéFaltaRealmente()
    {
        var report = new DiagnosticReport();

        var resumen = report.ResumenEstado();

        Assert.Contains("Rendimiento", resumen, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Térmicas", resumen, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Wmi_Bloqueado_PoneMensajeEspecificoDePermisos()
    {
        Wmi.ResetAccessState();
        Wmi.MarcarAccesoDenegado();

        var report = new DiagnosticReport();
        var resumen = report.ResumenEstado();

        Assert.Contains("acceso denegado", resumen, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WMI", resumen, StringComparison.OrdinalIgnoreCase);

        Wmi.ResetAccessState();
    }

    [Fact]
    public void ReportBuilder_CreaHtml_ConReporteVacio()
    {
        var report = new DiagnosticReport();

        string path = ReportBuilder.Build(report);

        Assert.True(File.Exists(path), $"No se creó el HTML del informe: {path}");
        Assert.Contains("incompleto", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Exporter_GeneraJsonYArchivaHistorial_ConReporteParcial()
    {
        var report = new DiagnosticReport();
        report.Red = new List<LatencyResult>
        {
            new() { Destino = "Router", Media = 12, Estado = Severity.Ok }
        };

        string jsonPath = Exporter.ToJson(report);
        Exporter.Archivar(report);

        Assert.True(File.Exists(jsonPath));
        Assert.NotEmpty(Exporter.Historial());
    }
}
