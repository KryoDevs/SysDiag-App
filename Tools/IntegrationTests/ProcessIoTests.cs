using System.Collections.Generic;
using System.Linq;
using SysDiag.Core.Performance;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// Pruebas de las partes puras de «consumo por proceso»: la escala de unidades,
/// la diferencia entre dos muestras y el resumen de destinos.
///
/// Todo lo que se puede equivocar sin un disco real está acá. Lo que queda
/// afuera es el P/Invoke, que no se puede probar en integración continua, y por
/// eso el módulo está partido así y no en un solo método.
/// </summary>
public class ProcessIoTests
{
    private static Dictionary<int, MuestraIo> Muestra(params (int Pid, ulong Leido, ulong Escrito, string Nombre)[] filas)
    {
        var mapa = new Dictionary<int, MuestraIo>();
        foreach (var f in filas)
            mapa[f.Pid] = new MuestraIo { Leido = f.Leido, Escrito = f.Escrito, Nombre = f.Nombre };
        return mapa;
    }

    // ---- Escala de unidades ------------------------------------------------

    [Fact]
    public void LaVelocidadSeEscala_aLaUnidadQueCorresponde()
    {
        Assert.Equal((0, "B/s"), ProcesoIoMath.Velocidad(0));
        Assert.Equal((512, "B/s"), ProcesoIoMath.Velocidad(512));
        Assert.Equal((2, "KB/s"), ProcesoIoMath.Velocidad(2048));
        Assert.Equal((3, "MB/s"), ProcesoIoMath.Velocidad(3 * 1024 * 1024));
        Assert.Equal((2, "GB/s"), ProcesoIoMath.Velocidad(2L * 1024 * 1024 * 1024));
    }

    [Fact]
    public void UnaVelocidadImposible_noSeMuestra()
    {
        // Un NaN llegaría a la vista y se imprimiría tal cual; acá se corta.
        var (valor, unidad) = ProcesoIoMath.Velocidad(double.NaN);
        Assert.Equal(0, valor);
        Assert.Equal("B/s", unidad);
    }

    // ---- Diferencia de muestras --------------------------------------------

    [Fact]
    public void UnProcesoQueAparecioDespues_noSeMide()
    {
        // Sin referencia inicial, su acumulado total pasaría por movimiento de
        // estos segundos y sería siempre el primero de la lista.
        var antes = Muestra((1, 0, 0, "viejo"));
        var despues = Muestra((1, 0, 0, "viejo"), (2, 900_000_000, 0, "recien-llegado"));

        var filas = ProcesoIoMath.Calcular(antes, despues, null, 5);

        Assert.DoesNotContain(filas, f => f.Pid == 2);
    }

    [Fact]
    public void UnContadorQueBajo_seDescarta()
    {
        // El PID se reutilizó o el proceso se reinició. Restar igual da un
        // número envuelto enorme, y en esta pantalla eso manda a matar el
        // proceso equivocado.
        var antes = Muestra((7, 5000, 5000, "servicio"));
        var despues = Muestra((7, 1000, 1000, "servicio"));

        Assert.Empty(ProcesoIoMath.Calcular(antes, despues, null, 5));
    }

    [Fact]
    public void LaDiferencia_seDividePorLosSegundosMedidos()
    {
        var antes = Muestra((3, 1000, 0, "copia"));
        var despues = Muestra((3, 1000 + 4096, 2048, "copia"));

        var fila = Assert.Single(ProcesoIoMath.Calcular(antes, despues, null, 2));

        Assert.Equal(4096UL, fila.LeidoBytes);
        Assert.Equal(2048UL, fila.EscritoBytes);
        Assert.Equal(2048, fila.LecturaBytesS);
        Assert.Equal(1024, fila.EscrituraBytesS);
        Assert.Equal(3072, fila.TotalBytesS);
    }

    [Fact]
    public void SinTiempoTranscurrido_noHayMedicion()
    {
        var antes = Muestra((3, 0, 0, "copia"));
        var despues = Muestra((3, 9999, 0, "copia"));

        Assert.Empty(ProcesoIoMath.Calcular(antes, despues, null, 0));
    }

    [Fact]
    public void LoQueNoSeMovioNiTieneConexiones_noAparece()
    {
        var antes = Muestra((1, 0, 0, "inactivo"));
        var despues = Muestra((1, 0, 0, "inactivo"));

        Assert.Empty(ProcesoIoMath.Calcular(antes, despues, null, 5));
    }

    [Fact]
    public void QuienNoTocaElDiscoPeroTieneLaRed_aparece()
    {
        // La pregunta de la pantalla es «quién está usando el disco y la red»:
        // dejar afuera al que solo usa la red la respondería a medias.
        var conexiones = new Dictionary<int, List<string>> { [9] = new() { "1.1.1.1:443" } };
        var antes = Muestra((9, 0, 0, "navegador"));
        var despues = Muestra((9, 0, 0, "navegador"));

        var fila = Assert.Single(ProcesoIoMath.Calcular(antes, despues, conexiones, 5));

        Assert.Equal(1, fila.Conexiones);
        Assert.Equal("1.1.1.1:443", fila.Destinos);
    }

    [Fact]
    public void ElOrden_esPorMovimientoTotalYNoPorOperaciones()
    {
        // Mil lecturas chicas no son mil lecturas grandes: ordenar por
        // operaciones pondría primero al proceso más inofensivo.
        var antes = Muestra((1, 0, 0, "muchas-lecturas"), (2, 0, 0, "una-lectura-grande"));
        var despues = Muestra((1, 1024, 0, "muchas-lecturas"), (2, 8_000_000, 0, "una-lectura-grande"));

        var filas = ProcesoIoMath.Calcular(antes, despues, null, 5);

        Assert.Equal(new[] { 2, 1 }, filas.Select(f => f.Pid).ToArray());
    }

    [Fact]
    public void ElNombreSeTomaDeLaMuestraFinal()
    {
        var antes = Muestra((5, 0, 0, ""));
        var despues = Muestra((5, 2048, 0, "actualizado"));

        Assert.Equal("actualizado", Assert.Single(ProcesoIoMath.Calcular(antes, despues, null, 5)).Nombre);
    }

    // ---- Destinos ----------------------------------------------------------

    [Fact]
    public void SinConexiones_seDiceYSinMas()
    {
        Assert.Equal("sin conexiones", ProcesoIoMath.ResumirDestinos(null));
        Assert.Equal("sin conexiones", ProcesoIoMath.ResumirDestinos(new List<string>()));
    }

    [Fact]
    public void LosDestinos_seCuentanUnaVezYNombranHastaTres()
    {
        // Un navegador tiene decenas de extremos abiertos; listarlos todos no
        // ayuda a saber con quién habla.
        var remotos = new List<string> { "b.com:443", "a.com:443", "b.com:443", "c.com:80", "d.com:80", "e.com:80" };

        var resumen = ProcesoIoMath.ResumirDestinos(remotos);

        Assert.StartsWith("a.com:443, b.com:443, c.com:80", resumen);
        Assert.Contains("y 2 más", resumen);
    }

    // ---- Recorte -----------------------------------------------------------

    [Fact]
    public void ElRecorte_noDejaAfueraLoQueSeEstaBuscando()
    {
        var conexiones = new Dictionary<int, List<string>>
        {
            [1] = new() { "a:1" }, [2] = new() { "b:2" }, [3] = new() { "c:3" }
        };
        var antes = Muestra((1, 0, 0, "activo-uno"), (2, 0, 0, "activo-dos"), (3, 0, 0, "solo-red"));
        var despues = Muestra((1, 5000, 0, "activo-uno"), (2, 4000, 0, "activo-dos"), (3, 0, 0, "solo-red"));
        var filas = ProcesoIoMath.Calcular(antes, despues, conexiones, 5);

        // Con más procesos activos que el tope, gana el que más mueve.
        Assert.Equal(new[] { 1, 2 }, ProcesoIoMath.Top(filas, 2).Select(f => f.Pid).ToArray());

        // Con menos activos que el tope, no se recorta: los que solo tienen red
        // se conservan en vez de desaparecer por una regla pensada para el caso
        // de arriba.
        Assert.Equal(3, ProcesoIoMath.Top(filas, 10).Count);
    }
}
