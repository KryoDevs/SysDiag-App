using System;
using System.Collections.Generic;
using System.Linq;
using SysDiag.Core.Diagnostics;
using SysDiag.Models;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// Pruebas del motor de diferencias entre dos diagnósticos.
///
/// Es la pieza que responde «¿mejoró o empeoró?», así que sus dos decisiones
/// no obvias son las dos cosas que más vale probar: emparejar hallazgos por
/// área y mensaje en vez de por severidad (para que un hallazgo que empeoró no
/// aparezca como uno resuelto más uno nuevo) y comparar solo mediciones que son
/// número en el modelo.
/// </summary>
public class DiffTests
{
    private static DiagnosticReport Reporte(int puntaje, params string[] modulos)
    {
        var r = new DiagnosticReport { Puntaje = puntaje };
        foreach (var m in modulos) r.ModulosCompletados[m] = DateTime.Now;
        return r;
    }

    private static Finding Hallazgo(Severity s, string area, string mensaje) => new()
    {
        Severity = s, Area = area, Message = mensaje
    };

    // ---- Emparejamiento de hallazgos ---------------------------------------

    [Fact]
    public void UnHallazgoQueEmpeora_seCuentaComoEmpeorado()
    {
        // Si se emparejara por severidad también, esto daría «uno resuelto y uno
        // nuevo», que es la lectura contraria a lo que pasó.
        var antes = Reporte(80, "red");
        antes.Hallazgos.Add(Hallazgo(Severity.Warn, "Red", "Jitter alto"));

        var despues = Reporte(70, "red");
        despues.Hallazgos.Add(Hallazgo(Severity.Bad, "Red", "Jitter alto"));

        var diff = DiffEngine.Comparar(antes, despues);

        Assert.Single(diff.Empeoraron);
        Assert.Empty(diff.Nuevos);
        Assert.Empty(diff.Resueltos);
    }

    [Fact]
    public void UnHallazgoQueMejora_seCuentaComoMejorado()
    {
        var antes = Reporte(60, "red");
        antes.Hallazgos.Add(Hallazgo(Severity.Bad, "Disco", "Salud en fallo"));

        var despues = Reporte(90, "red");
        despues.Hallazgos.Add(Hallazgo(Severity.Ok, "Disco", "Salud en fallo"));

        var diff = DiffEngine.Comparar(antes, despues);

        Assert.Single(diff.Mejoraron);
        Assert.Empty(diff.Nuevos);
        Assert.Empty(diff.Resueltos);
    }

    [Fact]
    public void UnHallazgoNuevo_seCuentaComoNuevo()
    {
        var antes = Reporte(90, "red");
        var despues = Reporte(80, "red");
        despues.Hallazgos.Add(Hallazgo(Severity.Bad, "Memoria", "Paginación sostenida"));

        var diff = DiffEngine.Comparar(antes, despues);

        Assert.Single(diff.Nuevos);
        Assert.Equal("Memoria", diff.Nuevos[0].Area);
        Assert.Null(diff.Nuevos[0].SeveridadAnterior);
    }

    [Fact]
    public void UnHallazgoQueDesaparece_seCuentaComoResuelto()
    {
        var antes = Reporte(70, "red");
        antes.Hallazgos.Add(Hallazgo(Severity.Warn, "Red", "DNS lento"));

        var despues = Reporte(90, "red");

        var diff = DiffEngine.Comparar(antes, despues);

        Assert.Single(diff.Resueltos);
        Assert.Equal("Red", diff.Resueltos[0].Area);
    }

    [Fact]
    public void ElModuloQueLoGenero_noCuentaParaEmparejar()
    {
        // Un mismo hallazgo lo puede emitir otro módulo entre corridas; eso no
        // es un cambio del equipo.
        var antes = Reporte(80, "red");
        antes.Hallazgos.Add(new Finding { Severity = Severity.Warn, Area = "Red", Message = "Latencia alta", Modulo = "red" });

        var despues = Reporte(80, "red");
        despues.Hallazgos.Add(new Finding { Severity = Severity.Warn, Area = "Red", Message = "Latencia alta", Modulo = "rendimiento" });

        var diff = DiffEngine.Comparar(antes, despues);

        Assert.Empty(diff.Nuevos);
        Assert.Empty(diff.Resueltos);
        Assert.Single(diff.Iguales);
    }

    [Fact]
    public void SinCambios_elResumenLoDiceYNoInventaNada()
    {
        var antes = Reporte(85, "red");
        antes.Hallazgos.Add(Hallazgo(Severity.Warn, "Red", "Jitter alto"));
        var despues = Reporte(85, "red");
        despues.Hallazgos.Add(Hallazgo(Severity.Warn, "Red", "Jitter alto"));

        var diff = DiffEngine.Comparar(antes, despues);

        Assert.True(diff.Vacio);
        Assert.Contains("Sin cambios", diff.Resumen());
        Assert.Equal(0, diff.DeltaPuntaje);
    }

    // ---- Cobertura ----------------------------------------------------------

    [Fact]
    public void CoberturasDistintas_seDeclaran()
    {
        var parcial = Reporte(95, "red");
        var completo = Reporte(70, "red", "rendimiento", "termicas", "almacenamiento",
            "seguridad", "estabilidad", "drivers", "actualizaciones", "arranque");

        var diff = DiffEngine.Comparar(parcial, completo);

        Assert.False(diff.MismaCobertura);
        Assert.Contains("Red", diff.CoberturaAnterior);
        Assert.NotEqual(diff.CoberturaAnterior, diff.CoberturaActual);
    }

    [Fact]
    public void MismaCobertura_noAvisa()
    {
        var a = Reporte(80, "red", "rendimiento");
        var b = Reporte(80, "red", "rendimiento");

        Assert.True(DiffEngine.Comparar(a, b).MismaCobertura);
    }

    // ---- Mediciones ---------------------------------------------------------

    [Fact]
    public void ElEspacioLibreQueBaja_seMarcaComoPeor()
    {
        var antes = Reporte(80);
        antes.Discos.Add(new DiskRow { Unidad = "C:", LibrePct = 40 });

        var despues = Reporte(70);
        despues.Discos.Add(new DiskRow { Unidad = "C:", LibrePct = 12 });

        var diff = DiffEngine.Comparar(antes, despues);
        var medicion = Assert.Single(diff.Mediciones);

        Assert.True(medicion.Peor);
        Assert.Contains("C:", medicion.Concepto);
    }

    [Fact]
    public void UnaLatenciaQueSubePoco_noSeReporta()
    {
        // Menos de 3 ms es ruido de medición. Anunciarlo haría que el diff
        // pareciera moverse siempre, y un diff que siempre se mueve no se lee.
        var antes = Reporte(80);
        antes.Red.Add(new LatencyResult { Destino = "Router", Media = 25 });

        var despues = Reporte(80);
        despues.Red.Add(new LatencyResult { Destino = "Router", Media = 27 });

        Assert.Empty(DiffEngine.Comparar(antes, despues).Mediciones);
    }

    [Fact]
    public void UnaLatenciaQueSubeMucho_seReportaComoPeor()
    {
        var antes = Reporte(80);
        antes.Red.Add(new LatencyResult { Destino = "Router", Media = 25 });

        var despues = Reporte(60);
        despues.Red.Add(new LatencyResult { Destino = "Router", Media = 180 });

        var medicion = Assert.Single(DiffEngine.Comparar(antes, despues).Mediciones);

        Assert.True(medicion.Peor);
        Assert.Contains("155 ms", medicion.Nota);
    }

    [Fact]
    public void UnEventoQueSeRepiteMas_seReporta()
    {
        var antes = Reporte(80);
        antes.EventosResumen.Add(new EventSummaryRow { Id = 41, Origen = "Kernel-Power", Ocurrencias = 2 });

        var despues = Reporte(60);
        despues.EventosResumen.Add(new EventSummaryRow { Id = 41, Origen = "Kernel-Power", Ocurrencias = 5 });

        var medicion = Assert.Single(DiffEngine.Comparar(antes, despues).Mediciones);

        Assert.True(medicion.Peor);
        Assert.Contains("3", medicion.Nota);
    }

    [Fact]
    public void VolcadosNuevos_seReportan()
    {
        var antes = Reporte(80);
        var despues = Reporte(60);
        despues.Minidumps.Add(new DumpRow { Archivo = "101025-01.dmp" });

        var medicion = Assert.Single(DiffEngine.Comparar(antes, despues).Mediciones);

        Assert.True(medicion.Peor);
        Assert.Equal("0", medicion.Antes);
        Assert.Equal("1", medicion.Despues);
    }

    [Fact]
    public void ElResumenJuntaLasCuentasEnUnaFrase()
    {
        var antes = Reporte(80, "red");
        antes.Hallazgos.Add(Hallazgo(Severity.Warn, "Red", "Jitter alto"));

        var despues = Reporte(60, "red");
        despues.Hallazgos.Add(Hallazgo(Severity.Bad, "Red", "Jitter alto"));
        despues.Hallazgos.Add(Hallazgo(Severity.Bad, "Disco", "Salud en fallo"));

        var diff = DiffEngine.Comparar(antes, despues);

        Assert.Contains("1 empeoró", diff.Resumen());
        Assert.Contains("1 hallazgo nuevo", diff.Resumen());
        Assert.Equal(-20, diff.DeltaPuntaje);
    }
}
