using System.Collections.Generic;
using System.Linq;
using SysDiag.Core.Storage;
using SysDiag.Models;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// Pruebas del parseo y la interpretación de atributos SMART.
///
/// Son las dos partes puras del módulo: el bloque de bytes que entrega WMI y la
/// traducción de cada atributo a una conclusión. Todo lo que puede fallar sin
/// un disco real está acá, porque justamente no hay forma de probar esto en la
/// máquina de integración continua.
///
/// Las aserciones evitan a propósito cualquier número formateado: la app fija
/// `es-CL` al arrancar y las pruebas corren sin ese arranque, así que el
/// separador de miles depende de la máquina y no de este código.
/// </summary>
public class SmartTests
{
    // ---- Parseo del bloque -------------------------------------------------

    private static byte[] Bloque(params (int Id, byte Valor, byte Peor, long Crudo)[] atributos)
    {
        var datos = new byte[2 + 30 * 12];
        datos[0] = 1;   // revisión de la estructura

        int pos = 2;
        foreach (var a in atributos)
        {
            datos[pos] = (byte)a.Id;
            datos[pos + 3] = a.Valor;
            datos[pos + 4] = a.Peor;
            for (int i = 0; i < 6; i++) datos[pos + 5 + i] = (byte)(a.Crudo >> (8 * i));
            pos += 12;
        }
        return datos;
    }

    private static byte[] BloqueUmbrales(params (int Id, byte Umbral)[] pares)
    {
        var datos = new byte[2 + 30 * 12];
        datos[0] = 1;

        int pos = 2;
        foreach (var p in pares)
        {
            datos[pos] = (byte)p.Id;
            datos[pos + 1] = p.Umbral;
            pos += 12;
        }
        return datos;
    }

    [Fact]
    public void UnBloqueNulo_oDemasiadoCorto_noProduceAtributos()
    {
        Assert.Empty(SmartModule.ParsearAtributos(null));
        Assert.Empty(SmartModule.ParsearAtributos(new byte[0]));
        Assert.Empty(SmartModule.ParsearAtributos(new byte[13]));   // menos de una entrada completa
    }

    [Fact]
    public void UnaEntrada_seLeeEntera()
    {
        var atributos = SmartModule.ParsearAtributos(Bloque((5, 100, 100, 0)));

        var unico = Assert.Single(atributos);
        Assert.Equal(5, unico.Id);
        Assert.Equal(100, unico.Valor);
        Assert.Equal(100, unico.Peor);
        Assert.Equal(0, unico.Crudo);
    }

    [Fact]
    public void ElValorCrudo_seLeeEn48Bits()
    {
        // Seis bytes little-endian: los atributos de volumen (TBW, LBA
        // escritos) usan el ancho completo y se truncarían a 32 bits.
        var atributos = SmartModule.ParsearAtributos(Bloque((241, 100, 100, 0x010203040506)));

        Assert.Equal(0x010203040506, Assert.Single(atributos).Crudo);
    }

    [Fact]
    public void LasEntradasVacias_seDescartan()
    {
        // Los discos rellenan con ceros las entradas que no usan; sin este
        // filtro aparecerían treinta filas «Atributo 0» por disco.
        var atributos = SmartModule.ParsearAtributos(Bloque((5, 100, 100, 0), (0, 0, 0, 0), (9, 90, 90, 500)));

        Assert.Equal(2, atributos.Count);
        Assert.Equal(new[] { 5, 9 }, atributos.Select(a => a.Id).ToArray());
    }

    [Fact]
    public void LosUmbrales_seEmparejanPorIdentificador()
    {
        var umbrales = SmartModule.ParsearUmbrales(BloqueUmbrales((5, 10), (194, 45)));

        Assert.Equal(10, umbrales[5]);
        Assert.Equal(45, umbrales[194]);
        Assert.False(umbrales.ContainsKey(9));
    }

    // ---- Interpretación ----------------------------------------------------

    private static SmartLectura Leer(int id, byte valor, long crudo, byte? umbral = null) =>
        SmartModule.Interpretar(new SmartAtributoCrudo { Id = id, Valor = valor, Peor = valor, Crudo = crudo }, umbral);

    [Fact]
    public void ElUmbralDelFabricante_vaAntesQueNuestroCriterio()
    {
        // Es el propio disco el que dice que el atributo falló: no depende de
        // nuestra tabla y por eso se mira primero.
        var lectura = Leer(5, 5, 0, umbral: 10);

        Assert.Equal(Severity.Bad, lectura.Estado);
        Assert.Contains("fabricante", lectura.Texto);
    }

    [Fact]
    public void UnValorNormalizadoEnCero_noSeComparaConElUmbral()
    {
        // Cero no es una medición, es una entrada que el disco no llenó. Si se
        // comparara igual contra el umbral, cualquier atributo sin datos
        // saldría como fallido y el disco entero parecería muerto.
        var lectura = Leer(5, 0, 0, umbral: 10);

        Assert.Equal(Severity.Ok, lectura.Estado);
        Assert.DoesNotContain("fabricante", lectura.Texto);
    }

    [Fact]
    public void SinSectoresReasignados_esCorrecto()
    {
        var lectura = Leer(5, 100, 0);

        Assert.Equal(Severity.Ok, lectura.Estado);
        Assert.Contains("Sin sectores reasignados", lectura.Texto);
    }

    [Fact]
    public void PocosSectoresReasignados_esAtencionYNoSentencia()
    {
        var lectura = Leer(5, 90, 3);

        Assert.Equal(Severity.Warn, lectura.Estado);
        // Un número aislado no dice si el daño avanza. La lectura lo dice en
        // lugar de insinuar que está bien: el atributo no se puede interpretar
        // sin una segunda medición.
        Assert.Contains("volviendo a medir", lectura.Texto);
    }

    [Fact]
    public void MuchosSectoresReasignados_esCritico()
    {
        Assert.Equal(Severity.Bad, Leer(5, 60, 40).Estado);
    }

    [Fact]
    public void SectoresPendientes_subenDeAtencionACritico()
    {
        Assert.Equal(Severity.Warn, Leer(197, 90, 2).Estado);
        Assert.Equal(Severity.Bad, Leer(197, 60, 9).Estado);
        Assert.Equal(Severity.Ok, Leer(197, 100, 0).Estado);
    }

    [Fact]
    public void SectoresIncorregibles_sonCriticosDesdeElPrimero()
    {
        Assert.Equal(Severity.Bad, Leer(198, 80, 1).Estado);
        Assert.Equal(Severity.Ok, Leer(198, 100, 0).Estado);
    }

    [Fact]
    public void LosErroresDeCRC_seAtribuyenAlCableYNoAlDisco()
    {
        // Es la conclusión accionable del atributo: cambiar un cable de dos
        // dólares antes de dar por muerto un disco sano.
        var lectura = Leer(199, 100, 4);

        Assert.Equal(Severity.Warn, lectura.Estado);
        Assert.Contains("cable", lectura.Texto);
    }

    [Theory]
    [InlineData(8, Severity.Bad)]
    [InlineData(20, Severity.Warn)]
    [InlineData(60, Severity.Ok)]
    public void LaVidaUtilRestante_seCalificaPorTramos(int resto, Severity esperado)
    {
        // Los tres identificadores que las marcas usan para lo mismo; el valor
        // normalizado es el porcentaje restante por convención.
        foreach (int id in new[] { 177, 202, 231 })
        {
            var lectura = Leer(id, (byte)resto, 0);
            Assert.Equal(esperado, lectura.Estado);
            Assert.Contains("vida útil de escritura", lectura.Texto);
        }
    }

    [Fact]
    public void LaTemperatura_seCalificaYSeReconoceElRangoImposible()
    {
        Assert.Equal(Severity.Warn, Leer(194, 60, 72).Estado);
        Assert.Equal(Severity.Ok, Leer(194, 90, 45).Estado);

        // Algunos discos codifican mínimos y máximos en los bytes altos.
        // Tomar el byte bajo daría una temperatura ridícula; se dice que no se
        // pudo interpretar en lugar de mostrarla.
        var imposible = Leer(194, 90, 200);
        Assert.False(imposible.Interpretable);
    }

    [Fact]
    public void LasHorasEncendido_seInformanSinEncenderNingunaLuz()
    {
        var lectura = Leer(9, 95, 8760);

        Assert.Equal(Severity.Ok, lectura.Estado);
        Assert.Contains("horas encendido", lectura.Texto);
    }

    [Fact]
    public void LaTasaDeErroresDeLectura_noSeInterpreta()
    {
        // El crudo depende de cada fabricante: compararlo contra un número
        // nuestro inventaría fallas que el disco no reportó.
        var lectura = Leer(1, 120, 987654321);

        Assert.False(lectura.Interpretable);
        Assert.Contains("fabricante", lectura.Texto);
    }

    [Fact]
    public void UnAtributoDesconocido_seMuestraSinCalificar()
    {
        var lectura = Leer(250, 77, 1234);

        Assert.False(lectura.Interpretable);
        Assert.Equal(Severity.Ok, lectura.Estado);
        Assert.Contains("no interpreta", lectura.Texto);
    }

    // ---- Nombres -----------------------------------------------------------

    [Fact]
    public void LosNombresConocidos_seTraducenYLosDemasMuestranElNumero()
    {
        Assert.Equal("Sectores reasignados", SmartModule.Nombre(5));
        Assert.Equal("Sectores pendientes de reasignar", SmartModule.Nombre(197));
        Assert.Equal("Errores de CRC en UDMA", SmartModule.Nombre(199));

        // Inventar un nombre para un atributo no documentado es peor que
        // mostrar su número: este se puede buscar.
        Assert.Contains("250", SmartModule.Nombre(250));
    }
}
