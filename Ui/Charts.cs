using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using SysDiag.Core;

namespace SysDiag.Ui;

/// <summary>
/// Matemática de gráficos. Delega en <see cref="Stats"/> para todo lo que es
/// estadística y escalas: las dos implementaciones vivían duplicadas hasta que
/// un recolector de Core necesitó percentiles, y una segunda copia del mismo
/// redondeo de escala es exactamente el tipo de duplicado que un día dibuja un
/// gráfico distinto del informe. Lo que queda acá es solo la proyección a
/// píxeles, que sí es asunto de la interfaz.
/// </summary>
public static class ChartMath
{
    /// <summary>Redondea hacia arriba al peldaño más cercano de la escala (ver <see cref="Stats.Techo"/>).</summary>
    public static double Techo(double maximo) => Stats.Techo(maximo);

    /// <summary>Percentil por interpolación lineal (ver <see cref="Stats.Percentil"/>).</summary>
    public static double Percentil(IReadOnlyList<double> valores, double p) => Stats.Percentil(valores, p);

    /// <summary>Desviación estándar muestral (ver <see cref="Stats.Desviacion"/>).</summary>
    public static double Desviacion(IReadOnlyList<double> valores) => Stats.Desviacion(valores);

    /// <summary>Jitter según RFC 3550 (ver <see cref="Stats.JitterRfc"/>).</summary>
    public static double JitterRfc(IReadOnlyList<double> muestras) => Stats.JitterRfc(muestras);

    /// <summary>Rango de eje con paso redondo (ver <see cref="Stats.Escala"/>).</summary>
    public static (double Minimo, double Maximo, double Paso) Escala(
        double min, double max, int divisiones = 4, double? piso = null, double? tope = null)
        => Stats.Escala(min, max, divisiones, piso, tope);

    /// <summary>Formato compacto para etiquetas de eje (ver <see cref="Stats.Formato"/>).</summary>
    public static string Formato(double valor) => Stats.Formato(valor);

    /// <summary>
    /// Líneas de la rejilla de un eje vertical, de arriba (máximo) a abajo
    /// (mínimo). Devuelve la posición en píxeles y el rótulo ya formateado
    /// para que la vista no tenga que repetir esta aritmética.
    /// </summary>
    public static List<GuiaEje> Rejilla(double minimo, double maximo, double paso, double alto,
        double margenSuperior = 0, double margenInferior = 0)
    {
        var guias = new List<GuiaEje>();
        if (!double.IsFinite(minimo) || !double.IsFinite(maximo) || maximo <= minimo || paso <= 0) return guias;
        if (alto <= 0) return guias;

        // El bucle se cuenta con enteros y no sumando el paso: con pasos
        // fraccionarios (1,25) los errores de coma flotante se acumulan y la
        // última guía termina fuera del lienzo o no aparece.
        int divisiones = (int)Math.Floor((maximo - minimo) / paso + 1e-9);
        for (int i = 0; i <= divisiones; i++)
        {
            double valor = minimo + paso * i;
            double fraccion = (valor - minimo) / (maximo - minimo);
            double y = margenSuperior + (1 - fraccion) * (alto - margenSuperior - margenInferior);
            guias.Add(new GuiaEje
            {
                Valor = valor,
                Texto = Stats.Formato(valor),
                Y = Math.Round(y, 2),
                YTexto = Math.Round(y - 6, 2)
            });
        }
        return guias;
    }
}

/// <summary>Una línea horizontal de la rejilla de un eje, con su rótulo.</summary>
public class GuiaEje
{
    public double Valor { get; init; }
    public string Texto { get; init; } = "";
    public double Y { get; init; }
    /// <summary>Posición del rótulo: la línea menos medio alto de texto, para que quede centrado.</summary>
    public double YTexto { get; init; }
}

/// <summary>
/// Una barra del gráfico comparativo. Llega con el ancho ya resuelto en
/// píxeles porque una barra tiene que poder compararse con las demás de un
/// vistazo, y eso exige una escala común calculada sobre un máximo redondo
/// del conjunto, no sobre el máximo crudo (que dejaría la barra más larga
/// pegada al borde y sin margen para leer las guías).
/// </summary>
public class BarItem
{
    public string Etiqueta { get; init; } = "";
    public string ValorTexto { get; init; } = "";
    public string Detalle { get; init; } = "";
    /// <summary>Valor crudo, por si la vista quiere ordenar o filtrar sin parsear el texto.</summary>
    public double Valor { get; init; }
    /// <summary>Qué parte del riel ocupa (0-1). Permite reescalar en XAML sin recalcular.</summary>
    public double Fraccion { get; init; }
    public double Ancho { get; init; }
    public double AnchoPista { get; init; } = BarChart.AnchoPistaDefecto;
    public Brush Color { get; init; } = Brushes.Gray;
    /// <summary>Riel con las guías de escala ya dibujadas: se comparte entre todas las barras del gráfico.</summary>
    public Brush Pista { get; init; } = Brushes.Transparent;
    public double? MarcaX { get; init; }
    public bool MarcaVisible { get; init; }
}

/// <summary>Gráfico de barras horizontales con escala compartida y rejilla.</summary>
public class BarChart
{
    public const double AnchoPistaDefecto = 420;

    public string Titulo { get; init; } = "";
    public string Nota { get; init; } = "";
    public string Unidad { get; init; } = "";
    /// <summary>Rótulo de escala: «0 — 400 ms · guías cada 100 ms».</summary>
    public string Escala { get; init; } = "";
    /// <summary>Una frase con el agregado del conjunto: «4 destinos · media 38 ms».</summary>
    public string Resumen { get; init; } = "";
    public double AnchoPista { get; init; } = AnchoPistaDefecto;
    public List<BarItem> Barras { get; init; } = new();
    public bool Visible => Barras.Count > 0;

    public static BarChart Vacio(string titulo = "", string nota = "") => new() { Titulo = titulo, Nota = nota };

    /// <summary>
    /// El riel: un fondo y cuatro guías al 25/50/75/100 % del máximo. Se
    /// construye como pincel teselado en vez de como cuatro líneas por fila
    /// porque una barra es una fila de un ItemsControl: dibujar la rejilla
    /// por fila son 4 × N elementos y un desalineo posible; dibujarla en el
    /// pincel son cero elementos extra y la rejilla nace alineada.
    /// </summary>
    private static Brush CrearPista(double ancho)
    {
        var grupo = new DrawingGroup();
        grupo.Children.Add(new GeometryDrawing(
            new SolidColorBrush(Color.FromArgb(18, 0xFF, 0xFF, 0xFF)),
            null,
            new RectangleGeometry(new Rect(0, 0, ancho, 10))));

        var trazo = new Pen(new SolidColorBrush(Color.FromArgb(40, 0xFF, 0xFF, 0xFF)), 1);
        for (int i = 1; i <= 4; i++)
        {
            double x = Math.Round(ancho * i / 4.0) - 0.5;
            if (x <= 0) continue;
            grupo.Children.Add(new GeometryDrawing(null, trazo,
                new LineGeometry(new Point(x, 0), new Point(x, 10))));
        }

        var pincel = new DrawingBrush(grupo)
        {
            Viewport = new Rect(0, 0, ancho, 10),
            ViewportUnits = BrushMappingMode.Absolute,
            TileMode = TileMode.None,
            Stretch = Stretch.None
        };
        pincel.Freeze();
        return pincel;
    }

    public static BarChart Crear(string titulo, string nota,
        IEnumerable<(string Etiqueta, double Valor, string Texto, Brush Color, string Detalle)> datos,
        string unidad = "", double? escalaMax = null, double? marca = null, string marcaTexto = "",
        double anchoPista = AnchoPistaDefecto, int maximo = 12, bool ordenar = false,
        string escalaTexto = null, string resumenTexto = null)
    {
        var lista = (datos ?? Enumerable.Empty<(string, double, string, Brush, string)>())
            .Where(x => double.IsFinite(x.Valor) && x.Valor >= 0)
            .ToList();

        if (lista.Count == 0) return Vacio(titulo, nota);

        if (ordenar) lista = lista.OrderByDescending(x => x.Valor).ToList();
        if (maximo > 0 && lista.Count > maximo) lista = lista.Take(maximo).ToList();

        // El techo se redondea hacia arriba: un eje que dice «0 — 137,4 ms»
        // obliga a leer el número antes de poder comparar dos barras.
        double max = lista.Max(x => x.Valor);
        double techo = escalaMax ?? (max > 0 ? ChartMath.Techo(max) : 1);
        if (techo <= 0) techo = 1;

        double ancho = anchoPista > 0 ? anchoPista : AnchoPistaDefecto;
        var pista = CrearPista(ancho);

        double? marcaX = marca.HasValue && marca.Value > 0 && marca.Value <= techo
            ? Math.Round(marca.Value / techo * ancho, 2)
            : (double?)null;

        var barras = lista.Select(x => new BarItem
        {
            Etiqueta = x.Etiqueta,
            ValorTexto = x.Texto,
            Detalle = x.Detalle,
            Valor = x.Valor,
            Fraccion = Math.Clamp(x.Valor / techo, 0, 1),
            // Mínimo visible: una barra de cero píxeles se lee como dato
            // ausente y no como valor bajo.
            Ancho = Math.Max(3, Math.Clamp(x.Valor / techo, 0, 1) * ancho),
            AnchoPista = ancho,
            Color = x.Color,
            Pista = pista,
            MarcaX = marcaX,
            MarcaVisible = marcaX.HasValue
        }).ToList();

        double paso = techo / 4.0;
        // Los rótulos se calculan solos sobre el valor de las barras, que es
        // lo correcto siempre que la barra mida la magnitud que se muestra.
        // Cuando no es así —una escala logarítmica, un índice— el llamador
        // pasa el texto y este cálculo no se usa: mentir en el pie de un
        // gráfico es peor que no ponerlo.
        string escala = escalaTexto ??
            ($"0 — {ChartMath.Formato(techo)}{(string.IsNullOrEmpty(unidad) ? "" : " " + unidad)}" +
             $" · guías cada {ChartMath.Formato(paso)}{(string.IsNullOrEmpty(unidad) ? "" : " " + unidad)}" +
             (marcaX.HasValue && !string.IsNullOrEmpty(marcaTexto) ? " · línea: " + marcaTexto : ""));

        string resumen = resumenTexto ?? (lista.Count == 1
            ? "1 medición"
            : $"{lista.Count} mediciones · media {ChartMath.Formato(lista.Average(x => x.Valor))}" +
              (string.IsNullOrEmpty(unidad) ? "" : " " + unidad) +
              $" · máx {ChartMath.Formato(lista.Max(x => x.Valor))}" +
              (string.IsNullOrEmpty(unidad) ? "" : " " + unidad));

        return new BarChart
        {
            Titulo = titulo,
            Nota = nota,
            Unidad = unidad,
            Escala = escala,
            Resumen = resumen,
            AnchoPista = ancho,
            Barras = barras
        };
    }
}

/// <summary>Un punto marcado del historial, con su rótulo al pasar el puntero.</summary>
public class HistoryDot
{
    public double X { get; init; }
    public double Y { get; init; }
    public string Tooltip { get; init; } = "";
    public bool EsUltimo { get; init; }
}

/// <summary>
/// Línea del historial de puntaje. Se dibuja como polilínea sobre un lienzo
/// de tamaño fijo; los puntos vienen ya proyectados.
///
/// El eje ya no está clavado en 0-100: con puntajes que se mueven entre 78
/// y 84, una escala completa aplana la serie y cualquier variación real se
/// pierde. Ahora el eje se ajusta al rango de los datos —con margen y paso
/// redondo— y nunca sale de 0-100, que es el dominio del puntaje.
/// </summary>
public class HistoryChart
{
    /// <summary>
    /// Medidas del lienzo. La vista las repite como literales en el XAML
    /// (620 × 120) porque un <c>Canvas</c> no se estira solo y enlazar el
    /// ancho de una <c>Line</c> exigiría un RelativeSource por cada guía.
    /// Si se cambian acá, se cambian allá: están una al lado de la otra.
    /// </summary>
    public const double Ancho = 620;
    public const double Alto = 120;

    public PointCollection Puntos { get; init; } = new();
    public PointCollection Area { get; init; } = new();
    public List<HistoryDot> Marcas { get; init; } = new();
    public List<GuiaEje> Guias { get; init; } = new();
    public string Rango { get; init; } = "";
    public string Escala { get; init; } = "";
    public string Resumen { get; init; } = "";
    public double? MediaY { get; init; }
    public string MediaTexto { get; init; } = "";
    /// <summary>Último punto, ya centrado: la vista lo coloca sin aritmética.</summary>
    public double UltimoX { get; init; }
    public double UltimoY { get; init; }
    public bool Visible => Puntos.Count >= 2;

    public static HistoryChart Crear(List<(DateTime Fecha, int Puntaje)> serie)
    {
        if (serie == null || serie.Count < 2) return new HistoryChart();

        var datos = serie.OrderBy(x => x.Fecha).TakeLast(30).ToList();

        int min = datos.Min(x => x.Puntaje);
        int max = datos.Max(x => x.Puntaje);
        var (minimo, maximo, paso) = ChartMath.Escala(min, max, divisiones: 4, piso: 0, tope: 100);
        var guias = ChartMath.Rejilla(minimo, maximo, paso, Alto);

        var puntos = new PointCollection();
        var marcas = new List<HistoryDot>();

        for (int i = 0; i < datos.Count; i++)
        {
            double x = datos.Count == 1 ? 0 : i / (double)(datos.Count - 1) * Ancho;
            // El eje se invierte: en pantalla el origen está arriba y un
            // puntaje alto tiene que dibujarse arriba.
            double y = Alto - (datos[i].Puntaje - minimo) / (maximo - minimo) * Alto;

            puntos.Add(new Point(x, y));
            marcas.Add(new HistoryDot
            {
                X = x - 3,
                Y = y - 3,
                Tooltip = $"{datos[i].Fecha:yyyy-MM-dd HH:mm} · {datos[i].Puntaje}/100",
                EsUltimo = i == datos.Count - 1
            });
        }

        double media = datos.Average(x => x.Puntaje);
        double mediaY = Alto - (media - minimo) / (maximo - minimo) * Alto;

        var ultimo = puntos[puntos.Count - 1];

        // El área bajo la línea se cierra por la base para poder rellenarla.
        var area = new PointCollection(puntos) { new Point(Ancho, Alto), new Point(0, Alto) };

        int primero = datos[0].Puntaje;
        int ultimoValor = datos[datos.Count - 1].Puntaje;
        int delta = ultimoValor - primero;

        return new HistoryChart
        {
            Puntos = puntos,
            Area = area,
            Marcas = marcas,
            Guias = guias,
            MediaY = mediaY,
            MediaTexto = $"media {media:0}",
            UltimoX = ultimo.X - 4.5,
            UltimoY = ultimo.Y - 4.5,
            Rango = $"{datos.First().Fecha:dd MMM} — {datos.Last().Fecha:dd MMM}  ·  {datos.Count} diagnósticos",
            Escala = $"{ChartMath.Formato(minimo)} — {ChartMath.Formato(maximo)}",
            Resumen = delta == 0
                ? $"{ultimoValor} ahora · {min} mín · {max} máx · sin cambios desde el primero"
                : $"{ultimoValor} ahora · {min} mín · {max} máx · " +
                  (delta > 0 ? $"+{delta}" : delta.ToString(CultureInfo.InvariantCulture)) +
                  " desde el primero"
        };
    }
}

/// <summary>
/// Serie temporal en vivo: latencia, CPU o cualquier magnitud que se
/// muestrea una vez por segundo. Es un buffer circular con los agregados ya
/// calculados, para que la vista solo dibuje.
/// </summary>
public class LiveSeries
{
    private readonly Queue<double?> _muestras = new();

    public int Capacidad { get; }
    public int Total { get; private set; }
    public int Perdidos { get; private set; }

    public LiveSeries(int capacidad = 60) => Capacidad = Math.Clamp(capacidad, 2, 600);

    /// <summary><c>null</c> cuenta como muestra perdida: se conserva el hueco, que es el dato.</summary>
    public void Agregar(double? valor)
    {
        _muestras.Enqueue(valor);
        Total++;
        if (valor == null) Perdidos++;
        while (_muestras.Count > Capacidad) _muestras.Dequeue();
    }

    public void Reiniciar()
    {
        _muestras.Clear();
        Total = 0;
        Perdidos = 0;
    }

    public List<double> Valores => _muestras.Where(m => m.HasValue).Select(m => m.Value).ToList();

    public double? Ultimo => _muestras.LastOrDefault();
    public double PerdidaPct => Total > 0 ? Math.Round((double)Perdidos / Total * 100, 1) : 0;

    public double Minimo => Valores.Count > 0 ? Valores.Min() : 0;
    public double Maximo => Valores.Count > 0 ? Valores.Max() : 0;
    public double Media => Valores.Count > 0 ? Math.Round(Valores.Average(), 1) : 0;
    public double P95 => Valores.Count > 0 ? Math.Round(ChartMath.Percentil(Valores, 0.95), 1) : 0;
    public double Jitter => Valores.Count > 1 ? Math.Round(ChartMath.JitterRfc(Valores), 1) : 0;

    /// <summary>
    /// Serie con los huecos rellenos con el último valor válido, para que la
    /// línea no se corte en cada paquete perdido. La pérdida se sigue viendo
    /// aparte: en el número de arriba y en las marcas del pie.
    /// </summary>
    public List<double> Trazo(double inicial = 0)
    {
        var valores = new List<double>();
        double ultimo = _muestras.FirstOrDefault(m => m.HasValue) ?? inicial;
        foreach (var m in _muestras)
        {
            if (m.HasValue) ultimo = m.Value;
            valores.Add(ultimo);
        }
        return valores;
    }

    /// <summary>Posiciones (0-1) de las muestras perdidas, para marcarlas en el pie del gráfico.</summary>
    public List<double> PosicionesPerdidas()
    {
        var posiciones = new List<double>();
        if (_muestras.Count < 2) return posiciones;
        for (int i = 0; i < _muestras.Count; i++)
            if (_muestras.ElementAt(i) == null)
                posiciones.Add(i / (double)(_muestras.Count - 1));
        return posiciones;
    }

    /// <summary>Dibuja la serie. Devuelve un gráfico inerte: quien llama lo enlaza, no lo anima.</summary>
    public LineChart Grafico(double ancho, double alto, double? umbral = null, string unidad = "ms",
        double piso = 0)
    {
        return LineChart.Crear(Trazo(), ancho, alto, umbral, unidad, piso);
    }
}

/// <summary>
/// Gráfico de líneas para series en vivo o históricas cortas: latencia por
/// segundo, uso de CPU durante una corrida. Con rejilla, umbral y escala
/// calculada, para que «una punta» se pueda leer como «180 ms».
/// </summary>
public class LineChart
{
    public PointCollection Puntos { get; init; } = new();
    public PointCollection Area { get; init; } = new();
    public List<GuiaEje> Guias { get; init; } = new();
    public List<double> Perdidas { get; init; } = new();
    public double? UmbralY { get; init; }
    public string UmbralTexto { get; init; } = "";
    public bool UmbralVisible { get; init; }
    public double Ancho { get; init; }
    public double Alto { get; init; }
    public string Resumen { get; init; } = "";
    /// <summary>Último punto ya centrado, para el punto vivo del extremo.</summary>
    public double UltimoX { get; init; }
    public double UltimoY { get; init; }
    public bool Visible => Puntos.Count >= 2;

    public static LineChart Crear(IReadOnlyList<double> valores, double ancho = 620, double alto = 110,
        double? umbral = null, string unidad = "ms", double piso = 0, List<double> perdidas = null)
    {
        if (valores == null || valores.Count < 2 || ancho <= 0 || alto <= 0) return new LineChart();

        double min = Math.Min(valores.Min(), piso);
        double max = valores.Max();

        // Con pings de 8 ms una escala de 0-8 hace que el ruido de fondo
        // parezca una montaña: el piso de escala mantiene la proporción.
        double pisoEscala = Math.Max(20, ChartMath.Techo(max) * 0.25);
        if (max < pisoEscala) max = pisoEscala;

        var (minimo, maximo, paso) = ChartMath.Escala(min, max, divisiones: 4, piso: 0);
        var guias = ChartMath.Rejilla(minimo, maximo, paso, alto, margenSuperior: 1, margenInferior: 1);

        var puntos = new PointCollection();
        for (int i = 0; i < valores.Count; i++)
        {
            double x = i / (double)(valores.Count - 1) * ancho;
            double fraccion = (valores[i] - minimo) / (maximo - minimo);
            double y = 1 + (1 - Math.Clamp(fraccion, 0, 1)) * (alto - 2);
            puntos.Add(new Point(Math.Round(x, 2), Math.Round(y, 2)));
        }

        var area = new PointCollection(puntos) { new Point(ancho, alto), new Point(0, alto) };
        var ultimo = puntos[puntos.Count - 1];

        double? umbralY = umbral.HasValue && umbral.Value >= minimo && umbral.Value <= maximo
            ? 1 + (1 - (umbral.Value - minimo) / (maximo - minimo)) * (alto - 2)
            : (double?)null;

        var validas = valores.ToList();
        return new LineChart
        {
            Puntos = puntos,
            Area = area,
            Guias = guias,
            Perdidas = perdidas ?? new List<double>(),
            UmbralY = umbralY,
            UmbralTexto = umbral.HasValue ? $"{ChartMath.Formato(umbral.Value)} {unidad}".Trim() : "",
            UmbralVisible = umbralY.HasValue,
            Ancho = ancho,
            Alto = alto,
            UltimoX = ultimo.X - 4.5,
            UltimoY = ultimo.Y - 4.5,
            Resumen = $"mín {ChartMath.Formato(validas.Min())} · media {ChartMath.Formato(validas.Average())} · " +
                      $"máx {ChartMath.Formato(validas.Max())} · p95 {ChartMath.Formato(ChartMath.Percentil(validas, 0.95))}" +
                      (string.IsNullOrEmpty(unidad) ? "" : " " + unidad)
        };
    }
}

/// <summary>Una porción del anillo: arco ya trazado, listo para enlazar.</summary>
public class DonutSlice
{
    public string Etiqueta { get; init; } = "";
    public string ValorTexto { get; init; } = "";
    public string Detalle { get; init; } = "";
    public string Porcentaje { get; init; } = "";
    public Brush Color { get; init; } = Brushes.Gray;
    public Geometry Arco { get; init; } = Geometry.Empty;
    public double Fraccion { get; init; }
}

/// <summary>
/// Anillo de composición: cuánto ocupa cada parte de un total. Sirve para
/// espacio en disco y para memoria, que son las dos magnitudes donde la
/// pregunta no es «cuánto» sino «de qué está hecho el total».
/// </summary>
public class DonutChart
{
    /// <summary>Lado del lienzo. El anillo y el hueco central se dibujan contra este tamaño.</summary>
    public const double Lado = 132;
    private const double Centro = Lado / 2.0;
    private const double RadioExterno = 62;
    private const double RadioInterno = 43;

    public string Titulo { get; init; } = "";
    public string Nota { get; init; } = "";
    public string Centro1 { get; init; } = "";
    public string Centro2 { get; init; } = "";
    public List<DonutSlice> Segmentos { get; init; } = new();
    public bool Visible => Segmentos.Count > 0;

    public static DonutChart Crear(string titulo, string nota,
        IEnumerable<(string Etiqueta, double Valor, string Texto, Brush Color, string Detalle)> datos,
        string centro1 = "", string centro2 = "", int maximo = 6)
    {
        var lista = (datos ?? Enumerable.Empty<(string, double, string, Brush, string)>())
            .Where(x => double.IsFinite(x.Valor) && x.Valor > 0)
            .OrderByDescending(x => x.Valor)
            .ToList();

        if (lista.Count == 0) return new DonutChart { Titulo = titulo, Nota = nota };

        double total = lista.Sum(x => x.Valor);
        if (total <= 0) return new DonutChart { Titulo = titulo, Nota = nota };

        if (maximo > 0 && lista.Count > maximo)
        {
            // El resto se agrupa en «Otros»: doce porciones de 2 % no se leen,
            // y el hueco que dejan se confunde con espacio libre.
            var cabeza = lista.Take(maximo - 1).ToList();
            double resto = lista.Skip(maximo - 1).Sum(x => x.Valor);
            if (resto > 0)
                cabeza.Add(($"Otros ({lista.Count - maximo + 1})", resto, "", Brushes.DimGray,
                            string.Join(", ", lista.Skip(maximo - 1).Select(x => x.Etiqueta))));
            lista = cabeza;
        }

        var segmentos = new List<DonutSlice>();
        double acumulado = 0;
        foreach (var x in lista)
        {
            double fraccion = x.Valor / total;
            segmentos.Add(new DonutSlice
            {
                Etiqueta = x.Etiqueta,
                ValorTexto = x.Texto,
                Detalle = x.Detalle,
                Porcentaje = $"{Math.Round(fraccion * 100, 1):0.#} %".Replace(",", ".", StringComparison.Ordinal),
                Color = x.Color,
                Fraccion = fraccion,
                Arco = Anillo(acumulado, fraccion)
            });
            acumulado += fraccion;
        }

        return new DonutChart
        {
            Titulo = titulo,
            Nota = nota,
            Centro1 = centro1,
            Centro2 = centro2,
            Segmentos = segmentos
        };
    }

    /// <summary>
    /// Sector de anillo: arco exterior de ida, línea al interior, arco
    /// interior de vuelta. Como <c>ScoreArc</c>, el barrido se limita a
    /// 359.9°: un arco de 360° exactos tiene principio y fin en el mismo
    /// punto y WPF no puede trazarlo.
    /// </summary>
    private static Geometry Anillo(double inicio, double fraccion)
    {
        fraccion = Math.Clamp(fraccion, 0, 1);
        if (fraccion <= 0.0004) return Geometry.Empty;

        double a0 = inicio * 360.0 - 90.0;
        double a1 = Math.Min(a0 + fraccion * 360.0, a0 + 359.9);
        bool grande = a1 - a0 > 180;

        double r0 = a0 * Math.PI / 180;
        double r1 = a1 * Math.PI / 180;

        var p0 = new Point(Centro + RadioExterno * Math.Cos(r0), Centro + RadioExterno * Math.Sin(r0));
        var p1 = new Point(Centro + RadioExterno * Math.Cos(r1), Centro + RadioExterno * Math.Sin(r1));
        var p2 = new Point(Centro + RadioInterno * Math.Cos(r1), Centro + RadioInterno * Math.Sin(r1));
        var p3 = new Point(Centro + RadioInterno * Math.Cos(r0), Centro + RadioInterno * Math.Sin(r0));

        var figura = new PathFigure { StartPoint = p0, IsClosed = true };
        figura.Segments.Add(new ArcSegment(p1, new Size(RadioExterno, RadioExterno), 0, grande,
            SweepDirection.Clockwise, true));
        figura.Segments.Add(new LineSegment(p2, true));
        figura.Segments.Add(new ArcSegment(p3, new Size(RadioInterno, RadioInterno), 0, grande,
            SweepDirection.Counterclockwise, true));

        var geometria = new PathGeometry();
        geometria.Figures.Add(figura);
        if (geometria.CanFreeze) geometria.Freeze();
        return geometria;
    }
}
