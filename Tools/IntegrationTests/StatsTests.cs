using System;
using System.Collections.Generic;
using System.Linq;
using SysDiag.Core;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// Estadística y escalas de <see cref="Stats"/>.
///
/// Estas pruebas existen porque la escala de un gráfico es el tipo de código
/// que parece funcionar siempre hasta que deja de hacerlo: un máximo que cae
/// justo en una potencia de diez, una serie de un solo punto, un rango donde
/// todos los valores son iguales. Ninguno de esos casos se ve al mirar la
/// pantalla con datos normales, y todos dibujan algo roto.
///
/// Son funciones puras, así que corren sin ventana, sin WPF y sin hardware.
/// </summary>
public class StatsTests
{
    // ---- Percentiles ------------------------------------------------------

    [Fact]
    public void Percentil_ConSerieVacia_DevuelveCero()
    {
        Assert.Equal(0, Stats.Percentil(Array.Empty<double>(), 0.95));
    }

    [Fact]
    public void Percentil_ConUnValor_DevuelveEseValor()
    {
        Assert.Equal(42, Stats.Percentil(new[] { 42d }, 0.5));
    }

    [Fact]
    public void Percentil_InterpolaYNoSaltaAlExtremo()
    {
        // El método de rango entero devolvería 4 ó 5 a secas; la interpolación
        // da el valor entre los dos, que es lo que hace que una serie corta no
        // dé saltos al agregar una muestra.
        var serie = new[] { 1d, 2, 3, 4, 5 };
        Assert.Equal(1, Stats.Percentil(serie, 0));
        Assert.Equal(5, Stats.Percentil(serie, 1));
        Assert.Equal(3, Stats.Percentil(serie, 0.5));
    }

    [Fact]
    public void Percentil_NoDependeDelOrdenDeLlegada()
    {
        var desordenada = new[] { 90d, 10, 50, 30, 70 };
        var ordenada = new[] { 10d, 30, 50, 70, 90 };
        Assert.Equal(Stats.Percentil(ordenada, 0.95), Stats.Percentil(desordenada, 0.95));
    }

    // ---- Desviación -------------------------------------------------------

    [Fact]
    public void Desviacion_ConMenosDeDosMuestras_EsCeroYNoNaN()
    {
        Assert.Equal(0, Stats.Desviacion(Array.Empty<double>()));
        Assert.Equal(0, Stats.Desviacion(new[] { 7d }));
    }

    [Fact]
    public void Desviacion_DeSerieConstante_EsCero()
    {
        Assert.Equal(0, Stats.Desviacion(new[] { 5d, 5, 5, 5 }));
    }

    [Fact]
    public void Desviacion_UsaNmenosUno()
    {
        // Muestral, no poblacional: con {2, 4, 4, 4, 5, 5, 7, 9} la muestral
        // da 2,138. Si algún día se cambia a poblacional, este número avisa.
        var serie = new[] { 2d, 4, 4, 4, 5, 5, 7, 9 };
        Assert.Equal(2.138, Math.Round(Stats.Desviacion(serie), 3));
    }

    // ---- Jitter -----------------------------------------------------------

    [Fact]
    public void Jitter_DeSerieEstable_EsCero()
    {
        Assert.Equal(0, Stats.JitterRfc(new[] { 20d, 20, 20, 20 }));
    }

    [Fact]
    public void Jitter_UnPicoAislado_NoArruinaLaLectura()
    {
        // Es la razón de usar RFC 3550 en lugar de la media de diferencias
        // absolutas: un único paquete perdido y recuperado no puede hacer que
        // una red estable parezca inestable.
        double estable = Stats.JitterRfc(new[] { 20d, 20, 20, 20, 20, 20, 20, 20 });
        double conPico = Stats.JitterRfc(new[] { 20d, 20, 20, 300, 20, 20, 20, 20 });

        Assert.Equal(0, estable);
        Assert.True(conPico < 35, $"Un solo pico no debería disparar el jitter: {conPico}");
        Assert.True(conPico > 0);
    }

    [Fact]
    public void Jitter_DeAlternancia_Sube()
    {
        double alternando = Stats.JitterRfc(new[] { 10d, 90, 10, 90, 10, 90, 10, 90 });
        double estable = Stats.JitterRfc(new[] { 50d, 50, 50, 50, 50, 50, 50, 50 });
        Assert.True(alternando > estable, $"{alternando} debería superar {estable}");
    }

    // ---- Escala -----------------------------------------------------------

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(7, 8)]
    [InlineData(10, 10)]
    [InlineData(137, 150)]
    [InlineData(320, 400)]
    [InlineData(999, 1000)]
    public void Techo_RedondeaAlPeldañoDeLaEscala(double valor, double esperado)
    {
        Assert.Equal(esperado, Stats.Techo(valor));
    }

    [Fact]
    public void Techo_ConValoresNoUtiles_DevuelveUno()
    {
        Assert.Equal(1, Stats.Techo(0));
        Assert.Equal(1, Stats.Techo(-5));
        Assert.Equal(1, Stats.Techo(double.NaN));
    }

    [Fact]
    public void Escala_SiempreContieneLosDatos()
    {
        // Es la invariante que sostiene todos los gráficos: si un valor queda
        // fuera del rango, la línea se sale del lienzo y el punto no se ve.
        foreach (var serie in new[]
        {
            new[] { 78d, 82, 80 },
            new[] { 0d, 100 },
            new[] { 12d },
            new[] { -5d, 5 },
            new[] { 1_000_000d, 1_000_001 }
        })
        {
            var (minimo, maximo, paso) = Stats.Escala(serie.Min(), serie.Max(), 4);
            Assert.True(paso > 0, "el paso tiene que ser positivo");
            Assert.True(minimo <= serie.Min(), $"{minimo} no contiene {serie.Min()}");
            Assert.True(maximo >= serie.Max(), $"{maximo} no contiene {serie.Max()}");
            Assert.True(maximo > minimo, "el rango no puede ser nulo");
        }
    }

    [Fact]
    public void Escala_ConSerieConstante_NoColapsa()
    {
        var (minimo, maximo, paso) = Stats.Escala(50, 50, 4);
        Assert.True(maximo > minimo);
        Assert.True(paso > 0);
        Assert.True(minimo <= 50 && maximo >= 50);
    }

    [Fact]
    public void Escala_RespetaElDominioDelPuntaje()
    {
        // El puntaje es 0-100: un eje que prometa 120 dibujaría un techo
        // imposible y haría que 100 no llegue nunca arriba.
        for (int puntaje = 0; puntaje <= 100; puntaje += 7)
        {
            var (minimo, maximo, _) = Stats.Escala(puntaje, puntaje, 4, piso: 0, tope: 100);
            Assert.True(minimo >= 0, $"mínimo {minimo} se salió por abajo");
            Assert.True(maximo <= 100, $"máximo {maximo} se salió por arriba");
            Assert.True(minimo <= puntaje && maximo >= puntaje);
        }
    }

    [Fact]
    public void Escala_ConRangoInvertido_LoOrdena()
    {
        var (minimo, maximo, _) = Stats.Escala(90, 10, 4);
        Assert.True(minimo <= 10);
        Assert.True(maximo >= 90);
    }

    // ---- Formato ----------------------------------------------------------

    [Theory]
    [InlineData(0, "0")]
    [InlineData(78, "78")]
    [InlineData(100, "100")]
    [InlineData(12_345, "12,3 k")]
    [InlineData(2_500_000, "2,5 M")]
    public void Formato_EsCompacto(double valor, string esperado)
    {
        // La cultura se fija en la prueba y no se confía en la del agente de
        // compilación: en español el separador decimal es coma y en inglés es
        // punto, y sin esto la prueba pasaría en una máquina y fallaría en otra
        // sin que nada haya cambiado en el código.
        var anterior = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("es-ES");
            Assert.Equal(esperado, Stats.Formato(valor));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = anterior;
        }
    }

    [Fact]
    public void Formato_NoMuestraMasDeDosDecimales()
    {
        // Un rótulo de eje con cuatro decimales es más largo que la rejilla a
        // la que acompaña.
        foreach (double valor in new[] { 0.5, 1.0 / 3, 2.0 / 3, 99.99, 1234.5678 })
        {
            string texto = Stats.Formato(valor);
            int separador = texto.IndexOfAny(new[] { ',', '.' });
            if (separador < 0) continue;
            int decimales = texto.Length - separador - 1;
            // «12,3 k» y «2,5 M» llevan sufijo: se descuenta antes de contar.
            if (texto.EndsWith(" k", StringComparison.Ordinal) || texto.EndsWith(" M", StringComparison.Ordinal))
                decimales -= 2;
            Assert.True(decimales <= 2, $"\"{texto}\" tiene demasiados decimales para un eje");
        }
    }

    [Fact]
    public void Formato_DeNoFinito_EsGuionYNoNaN()
    {
        Assert.Equal("—", Stats.Formato(double.NaN));
        Assert.Equal("—", Stats.Formato(double.PositiveInfinity));
    }
}
