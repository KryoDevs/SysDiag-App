using System;
using System.Globalization;
using System.Linq;

namespace SysDiag.Core;

/// <summary>
/// Estadística y escalas, sin dependencia de WPF.
///
/// Vive en Core y no junto a los gráficos por dos razones. La primera es de
/// arquitectura: un recolector de Core no puede referenciar la capa de
/// interfaz, y las mediciones necesitan percentiles y desviaciones igual que
/// los gráficos (ver <c>NetworkModule</c>). La segunda es de verificación:
/// siendo funciones puras se prueban sin abrir una ventana
/// (<c>Tools/IntegrationTests/StatsTests.cs</c>), que es la única forma
/// razonable de comprobar un redondeo de escala.
/// </summary>
public static class Stats
{
    /// <summary>
    /// Peldaños admitidos para una escala. Con solo 1/2/5, un máximo de 320
    /// subía a 500 y la barra más larga ocupaba dos tercios del riel; con esta
    /// lista sube a 400 y el riel se aprovecha.
    /// </summary>
    private static readonly double[] Peldaños = { 1, 1.25, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10 };

    /// <summary>Redondea hacia arriba al peldaño más cercano de la serie 1/1.25/1.5/2/2.5/3/4/5/6/8/10 × 10^n.</summary>
    public static double Techo(double maximo)
    {
        if (!double.IsFinite(maximo) || maximo <= 0) return 1;

        double exponente = Math.Floor(Math.Log10(maximo));
        double escala = Math.Pow(10, exponente);
        double m = maximo / escala;

        foreach (double paso in Peldaños)
            if (m <= paso + 1e-9) return paso * escala;

        return 10 * escala;
    }

    /// <summary>Percentil por interpolación lineal (el de Excel): evita los saltos del método de rango entero en series cortas.</summary>
    public static double Percentil(IReadOnlyList<double> valores, double p)
    {
        if (valores == null || valores.Count == 0) return 0;
        if (valores.Count == 1) return valores[0];

        var orden = valores.OrderBy(x => x).ToArray();
        double pos = (orden.Length - 1) * Math.Clamp(p, 0, 1);
        int bajo = (int)Math.Floor(pos);
        int alto = Math.Min(bajo + 1, orden.Length - 1);
        return orden[bajo] + (orden[alto] - orden[bajo]) * (pos - bajo);
    }

    /// <summary>Desviación estándar muestral (n-1). Con menos de dos muestras es 0, no NaN.</summary>
    public static double Desviacion(IReadOnlyList<double> valores)
    {
        if (valores == null || valores.Count < 2) return 0;
        double media = valores.Average();
        double suma = 0;
        foreach (double v in valores) suma += (v - media) * (v - media);
        return Math.Sqrt(suma / (valores.Count - 1));
    }

    /// <summary>
    /// Jitter según RFC 3550: variación suavizada del retardo entre llegadas
    /// consecutivas. Es la definición que usan las herramientas de voz y video.
    /// A diferencia de la media de diferencias absolutas, no se dispara con un
    /// único pico aislado: un paquete perdido y recuperado no arruina la
    /// lectura de los siguientes.
    /// </summary>
    public static double JitterRfc(IReadOnlyList<double> muestras)
    {
        if (muestras == null || muestras.Count < 2) return 0;
        double jitter = 0;
        for (int i = 1; i < muestras.Count; i++)
        {
            double d = Math.Abs(muestras[i] - muestras[i - 1]);
            jitter += (d - jitter) / 16.0;
        }
        return jitter;
    }

    /// <summary>
    /// Rango "agradable" para un eje: margen del 15 % alrededor de los datos,
    /// paso redondo y límites alineados al paso. <paramref name="piso"/> y
    /// <paramref name="tope"/> acotan el resultado cuando el dominio lo tiene
    /// (un puntaje no puede salirse de 0-100).
    /// </summary>
    public static (double Minimo, double Maximo, double Paso) Escala(
        double min, double max, int divisiones = 4, double? piso = null, double? tope = null)
    {
        divisiones = Math.Clamp(divisiones, 1, 8);
        if (!double.IsFinite(min) || !double.IsFinite(max)) { min = 0; max = 1; }
        if (max < min) (min, max) = (max, min);

        double margen = Math.Max((max - min) * 0.15, 0.5);
        double lo = min - margen;
        double hi = max + margen;

        if (piso.HasValue)
        {
            lo = Math.Max(lo, piso.Value);
            hi = Math.Max(hi, piso.Value + 0.5);
        }
        if (tope.HasValue)
        {
            hi = Math.Min(hi, tope.Value);
            lo = Math.Min(lo, tope.Value - 0.5);
        }
        if (hi - lo < 0.5) hi = lo + 0.5;

        double paso = Techo((hi - lo) / divisiones);
        lo = Math.Floor(lo / paso) * paso;
        hi = Math.Ceiling(hi / paso) * paso;

        if (piso.HasValue) lo = Math.Max(lo, piso.Value);
        if (tope.HasValue) hi = Math.Min(hi, tope.Value);
        if (hi <= lo) hi = lo + paso;

        return (lo, hi, paso);
    }

    /// <summary>Formato compacto y estable para etiquetas de eje: 1.2 M, 34 k, 78, 0.5.</summary>
    public static string Formato(double valor)
    {
        if (!double.IsFinite(valor)) return "—";
        double absoluto = Math.Abs(valor);
        if (absoluto >= 1_000_000) return (valor / 1_000_000).ToString("0.##", CultureInfo.CurrentCulture) + " M";
        if (absoluto >= 10_000) return (valor / 1000).ToString("0.#", CultureInfo.CurrentCulture) + " k";
        if (absoluto >= 100) return valor.ToString("0", CultureInfo.CurrentCulture);
        if (absoluto >= 1) return valor.ToString("0.#", CultureInfo.CurrentCulture);
        if (absoluto == 0) return "0";
        return valor.ToString("0.##", CultureInfo.CurrentCulture);
    }
}
