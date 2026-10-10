using System.Collections.Generic;
using System.Linq;
using SysDiag.Core.Network;
using SysDiag.Models;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// Pruebas de la estadística y la lectura de la ruta: las dos partes puras de
/// la medición por salto, y las únicas que se pueden probar sin una red real.
///
/// La que más vale es la de la pérdida aislada. Un router intermedio con
/// pérdida y los saltos siguientes limpios no está perdiendo paquetes: está
/// limitando los ICMP que contesta. Leer eso al revés es la forma más común
/// —y más cómoda— de echarle la culpa al proveedor, y acá queda probado que no
/// se hace.
/// </summary>
public class TraceRouteTests
{
    private static HopLossRow Fila(int salto, string ip, int enviados, int respondidos,
        double media, double jitter = 0)
    {
        double perdida = enviados > 0 ? (double)(enviados - respondidos) / enviados * 100 : 0;
        return new HopLossRow
        {
            Salto = salto,
            Direccion = ip,
            Enviados = enviados,
            Respondidos = respondidos,
            PerdidaPct = System.Math.Round(perdida, 1),
            Media = media,
            Jitter = jitter
        };
    }

    private static List<string> Textos(IReadOnlyList<HopLossRow> ruta) =>
        TraceMath.Interpretar(ruta).Select(c => c.Texto).ToList();

    // ---- Estadística de un salto -------------------------------------------

    [Fact]
    public void UnSaltoSinRespuesta_noSeAcusaDePerderPaquetes()
    {
        // La mayoría de los routers descartan el ICMP en silencio. Ponerle
        // «100 % de pérdida» sería la conclusión más injusta y la más
        // frecuente de todas.
        var fila = TraceMath.Calcular(3, "10.0.0.1", new List<double>(), 10);

        Assert.Equal(0, fila.Respondidos);
        Assert.Equal(Severity.Warn, fila.Estado);
        Assert.Contains("sin respuesta", fila.Nota);
        Assert.DoesNotContain("100", fila.Nota);
    }

    [Fact]
    public void LaPerdida_seCalculaSobreLoEnviado()
    {
        var tiempos = new List<double> { 12, 14, 13, 15, 11, 12, 14, 13 };
        var fila = TraceMath.Calcular(2, "10.0.0.1", tiempos, 10);

        Assert.Equal(10, fila.Enviados);
        Assert.Equal(8, fila.Respondidos);
        Assert.Equal(20, fila.PerdidaPct);
        Assert.Equal(11, fila.Minimo);
        Assert.Equal(15, fila.Maximo);
        Assert.Equal(13, fila.Media);
    }

    [Fact]
    public void PocaPerdida_esAtencionYMuchaEsCritico()
    {
        // El corte no es arbitrario: por debajo de un cuarto de los paquetes,
        // la causa más probable sigue siendo el router contestando de mala
        // gana, no un enlace roto.
        var ocho = new List<double> { 10, 10, 10, 10, 10, 10, 10, 10 };
        var cinco = new List<double> { 10, 10, 10, 10, 10 };

        Assert.Equal(Severity.Warn, TraceMath.Calcular(2, "a", ocho, 10).Estado);
        Assert.Equal(Severity.Bad, TraceMath.Calcular(2, "b", cinco, 10).Estado);
    }

    [Fact]
    public void LaLatencia_seCalificaPorTramos()
    {
        var rapido = new List<double> { 20, 20, 20 };
        var medio = new List<double> { 90, 90, 90 };
        var lento = new List<double> { 200, 200, 200 };

        Assert.Equal(Severity.Ok, TraceMath.Calcular(2, "a", rapido, 3).Estado);
        Assert.Equal(Severity.Warn, TraceMath.Calcular(2, "b", medio, 3).Estado);
        Assert.Equal(Severity.Bad, TraceMath.Calcular(2, "c", lento, 3).Estado);
    }

    [Fact]
    public void ConUnaSolaMuestra_noSeReportaJitter()
    {
        // Un jitter de 0 con una muestra dice «enlace estable» cuando lo que
        // pasa es que no alcanzó la medición para saberlo.
        Assert.Equal(0, TraceMath.Calcular(2, "a", new List<double> { 15 }, 3).Jitter);
    }

    [Fact]
    public void UnJitterAlto_seMarcaComoColaYNoComoDistancia()
    {
        var fila = TraceMath.Calcular(2, "a", new List<double> { 10, 100, 10, 100 }, 4);

        Assert.True(fila.Jitter > TraceMath.JitterConGestion);
        Assert.Contains("cola", fila.Nota);
    }

    // ---- Lectura de la ruta ------------------------------------------------

    [Fact]
    public void SinSaltos_noHayConclusiones()
    {
        Assert.Empty(TraceMath.Interpretar(new List<HopLossRow>()));
    }

    [Fact]
    public void SiNadieResponde_seDiceQueNoSePudoMedir()
    {
        var ruta = new List<HopLossRow> { Fila(1, "a", 10, 0, 0), Fila(2, "b", 10, 0, 0) };

        var textos = Textos(ruta);

        Assert.Single(textos);
        Assert.Contains("Ningún salto respondió", textos[0]);
    }

    [Fact]
    public void UnaRutaLimpia_seDiceConCuantosSaltosSeMidio()
    {
        var ruta = new List<HopLossRow> { Fila(1, "a", 10, 10, 5), Fila(2, "b", 10, 10, 12) };

        Assert.Contains(Textos(ruta), t => t.Contains("Sin pérdida en ninguno de los 2 saltos"));
    }

    [Fact]
    public void LaPerdidaAislada_noSeLeEchaAlEnlace()
    {
        // El caso que define la herramienta: pérdida en un salto intermedio y
        // los siguientes limpios. Es el router limitando ICMP, no un enlace
        // que pierde paquetes; si se perdieran de verdad, no llegarían más
        // lejos y la pérdida se vería también en los saltos siguientes.
        var ruta = new List<HopLossRow>
        {
            Fila(1, "192.168.1.1", 10, 10, 2),
            Fila(2, "10.10.0.1", 10, 8, 20),
            Fila(3, "8.8.8.8", 10, 10, 25)
        };

        var textos = Textos(ruta);

        Assert.Contains(textos, t => t.Contains("limitando los ICMP"));
        // Y no se acusa a nadie: sin pérdida real no hay tramo que culpar.
        Assert.DoesNotContain(textos, t => t.Contains("tu router") || t.Contains("tu proveedor"));
    }

    [Fact]
    public void LaPerdidaQueSeArrastra_senalaElTramoYSuResponsable()
    {
        var ruta = new List<HopLossRow>
        {
            Fila(1, "192.168.1.1", 10, 10, 2),
            Fila(2, "10.10.0.1", 10, 7, 20),
            Fila(3, "8.8.8.8", 10, 7, 25)
        };

        var textos = Textos(ruta);

        Assert.Contains(textos, t => t.Contains("empieza en el salto 2"));
        // Con la pérdida confirmada sí se dice de quién es el tramo: el
        // segundo salto es el enlace con el proveedor.
        Assert.Contains(textos, t => t.Contains("proveedor"));
    }

    [Fact]
    public void ElResponsable_distingueElRouterDelRestoDelCamino()
    {
        Assert.Contains("tu router", TraceMath.Responsable(1).Texto);
        Assert.Contains("proveedor", TraceMath.Responsable(2).Texto);

        // Más allá del segundo salto, el proveedor no controla nada: decirlo
        // evita un reclamo que no va a ninguna parte.
        Assert.Contains("más allá", TraceMath.Responsable(5).Texto);
    }

    [Fact]
    public void ElSaltoMasLento_esElQueMasSumaRespectoDelAnterior()
    {
        var ruta = new List<HopLossRow>
        {
            Fila(1, "a", 10, 10, 10),
            Fila(2, "b", 10, 10, 30),
            Fila(3, "c", 10, 10, 90)
        };

        var salto = TraceMath.SaltoMasLento(ruta);

        Assert.NotNull(salto);
        Assert.Equal(3, salto.Value.Hacia);
        Assert.Equal(60, salto.Value.Suma);
    }

    [Fact]
    public void SinDosSaltosMedibles_noHaySaltoMasLento()
    {
        Assert.Null(TraceMath.SaltoMasLento(new List<HopLossRow> { Fila(1, "a", 10, 10, 10) }));
        Assert.Null(TraceMath.SaltoMasLento(new List<HopLossRow> { Fila(1, "a", 10, 0, 0), Fila(2, "b", 10, 0, 0) }));
    }

    [Fact]
    public void UnSaltoQueNoSumaNada_noSeNombraComoElMasCaro()
    {
        Assert.Null(TraceMath.SaltoMasLento(new List<HopLossRow>
        {
            Fila(1, "a", 10, 10, 20),
            Fila(2, "b", 10, 10, 20)
        }));
    }

    [Fact]
    public void ElTironesSeExplicanPorColaYNoPorPromedio()
    {
        var ruta = new List<HopLossRow> { Fila(1, "a", 10, 10, 20), Fila(2, "b", 10, 10, 25, 45) };

        Assert.Contains(Textos(ruta), t => t.Contains("jitter") && t.Contains("tirones"));
    }
}
