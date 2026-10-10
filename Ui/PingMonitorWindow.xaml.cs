using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using SysDiag.Core;
using SysDiag.Core.Network;

namespace SysDiag.Ui;

/// <summary>Ítem del selector de destino: una clase propia en vez de una tupla, para que el binding de WPF no tenga que adivinar.</summary>
public class DestinoPing
{
    public string Host { get; init; } = "";
    public string Label { get; init; } = "";
    public override string ToString() => Label;
}

public partial class PingMonitorWindow : Window
{
    private const int MaxMuestras = 60;

    private readonly List<double?> _muestras = new();
    private DispatcherTimer _timer;
    private CancellationTokenSource _cts;
    private bool _corriendo;
    private int _generacion;
    private bool _midiendo;
    private double? _ultimoValido;

    public PingMonitorWindow()
    {
        InitializeComponent();
        // Se ajusta antes de cualquier otra cosa: si la pantalla es más chica
        // que el alto declarado en el XAML, el pie de la ventana quedaría fuera
        // del área de trabajo y no habría cómo arrastrarla de vuelta.
        Ventana.AjustarAPantalla(this);

        ComboDestino.ItemsSource = NetworkModule.ObjetivosDisponibles()
            .Select(o => new DestinoPing { Host = o.Host, Label = o.Label })
            .ToList();
        if (ComboDestino.Items.Count > 0) ComboDestino.SelectedIndex = 0;

        SizeChanged += (_, _) => Redibujar();
        Closed += (_, _) => Detener();
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (_corriendo) Detener(); else Iniciar();
    }

    private void Cerrar_Click(object sender, RoutedEventArgs e) => Close();

    private void Iniciar()
    {
        if (ComboDestino.SelectedItem is not DestinoPing destino)
        {
            Dialog.Info("Elegí un destino", "No hay ningún destino seleccionado para medir.");
            return;
        }

        _muestras.Clear();
        _ultimoValido = null;
        PuntoFinal.Visibility = Visibility.Collapsed;
        _generacion++;
        _cts = new CancellationTokenSource();
        _corriendo = true;
        BtnIniciar.Content = "Detener";
        ComboDestino.IsEnabled = false;
        TxtVacio.Visibility = Visibility.Collapsed;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) => await Medir(destino.Host);
        _timer.Start();

        // El primer punto no espera un segundo entero para aparecer.
        _ = Medir(destino.Host);
    }

    private void Detener()
    {
        _corriendo = false;
        _generacion++;
        _timer?.Stop();
        _timer = null;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        BtnIniciar.Content = "Iniciar";
        ComboDestino.IsEnabled = true;
    }

    private async System.Threading.Tasks.Task Medir(string host)
    {
        if (_cts == null || _midiendo) return;
        _midiendo = true;
        int generation = _generacion;
        var token = _cts.Token;

        double? ms;
        try
        {
            ms = await NetworkModule.PingUnaVez(host, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally { _midiendo = false; }
        if (token.IsCancellationRequested || generation != _generacion || !_corriendo) return;

        _muestras.Add(ms);
        if (ms.HasValue) _ultimoValido = ms;
        if (_muestras.Count > MaxMuestras) _muestras.RemoveAt(0);

        ActualizarTextos();
        Redibujar();
    }

    private void ActualizarTextos()
    {
        var validas = _muestras.Where(m => m.HasValue).Select(m => m.Value).ToList();
        var ultimo = _muestras.LastOrDefault();

        if (ultimo.HasValue)
        {
            TxtActual.Text = $"{ultimo.Value:0} ms";
            TxtActual.Foreground = ultimo.Value > 120 ? Res("BBad") : ultimo.Value > 70 ? Res("BWarn") : Res("BAccent");
        }
        else
        {
            TxtActual.Text = "perdido";
            TxtActual.Foreground = Res("BBad");
        }

        TxtPromedio.Text = validas.Count > 0 ? $"{validas.Average():0} ms" : "—";
        TxtMaximo.Text = validas.Count > 0 ? $"{validas.Max():0} ms" : "—";

        double perdidaPct = _muestras.Count > 0
            ? Math.Round((double)_muestras.Count(m => !m.HasValue) / _muestras.Count * 100, 1)
            : 0;
        TxtPerdida.Text = $"{perdidaPct}%";
        TxtPerdida.Foreground = perdidaPct > 0 ? Res("BBad") : Res("BText");

        // p95 y jitter no caben en las cuatro tarjetas de arriba, pero son las
        // dos cifras que explican por qué una red «rápida» se siente mal: el
        // promedio no ve los picos y el p95 sí.
        if (validas.Count > 1)
        {
            double p95 = Stats.Percentil(validas, 0.95);
            double jitter = Stats.JitterRfc(validas);
            TxtResumen.Text = $"{validas.Count} muestras · mín {validas.Min():0} ms · " +
                              $"media {validas.Average():0} ms · p95 {p95:0} ms · " +
                              $"máx {validas.Max():0} ms · jitter {jitter:0.0} ms · " +
                              $"pérdida {perdidaPct}%";
        }
        else
        {
            TxtResumen.Text = validas.Count == 1 ? "1 muestra. Segundo a segundo se arma la serie." : "";
        }
    }

    private void Redibujar()
    {
        double w = Lienzo.ActualWidth;
        double h = Lienzo.ActualHeight;
        if (w <= 0 || h <= 0) return;

        MarcarPerdidas(w, h);

        if (_muestras.Count < 2)
        {
            Linea.Points = null;
            Area.Points = null;
            LineaUmbral.Visibility = Visibility.Collapsed;
            PuntoFinal.Visibility = Visibility.Collapsed;
            return;
        }

        // Los huecos por pérdida se rellenan con el último valor válido solo
        // para que la línea no se corte — la pérdida real sigue viéndose
        // aparte, en el número de arriba y en las franjas del fondo. Es una
        // simplificación deliberada, no un intento de esconder la pérdida.
        var valores = new List<double>();
        double ultimo = _muestras.FirstOrDefault(m => m.HasValue) ?? 0;
        foreach (var m in _muestras)
        {
            if (m.HasValue) ultimo = m.Value;
            valores.Add(ultimo);
        }

        // La escala se redondea con el mismo criterio de todo el programa
        // (Stats.Escala): un tope de 137 ms obliga a leer el número antes de
        // poder comparar dos puntos, y un tope de 150 se lee de un vistazo.
        double max = Math.Max(valores.Max(), 20); // piso de escala para que no se vea plano con pings muy bajos
        var (minimo, maximo, _) = Stats.Escala(0, max, divisiones: 4, piso: 0);
        double rango = Math.Max(maximo - minimo, 0.001);

        // Con 1 px de margen arriba y abajo: una línea pegada al borde del
        // lienzo se corta visualmente y parece dato faltante.
        double Proyectar(double valor) => 1 + (1 - (valor - minimo) / rango) * (h - 2);

        var puntos = new PointCollection();
        for (int i = 0; i < valores.Count; i++)
        {
            double x = valores.Count == 1 ? 0 : i / (double)(valores.Count - 1) * w;
            puntos.Add(new Point(x, Proyectar(valores[i])));
        }

        Linea.Points = puntos;

        var area = new PointCollection(puntos) { new Point(w, h), new Point(0, h) };
        Area.Points = area;

        LineaTope.X1 = 0; LineaTope.X2 = w; LineaTope.Y1 = Proyectar(maximo); LineaTope.Y2 = Proyectar(maximo);
        LineaMedio.X1 = 0; LineaMedio.X2 = w;
        LineaMedio.Y1 = Proyectar((minimo + maximo) / 2); LineaMedio.Y2 = Proyectar((minimo + maximo) / 2);
        LineaBase.X1 = 0; LineaBase.X2 = w; LineaBase.Y1 = h - 1; LineaBase.Y2 = h - 1;

        // El umbral solo se dibuja si entra en la escala: una línea en el borde
        // del lienzo dice «estás justo en el límite» y no es cierto.
        const double umbralMs = 100;
        if (umbralMs <= maximo)
        {
            double y = Proyectar(umbralMs);
            LineaUmbral.X1 = 0; LineaUmbral.X2 = w; LineaUmbral.Y1 = y; LineaUmbral.Y2 = y;
            LineaUmbral.Visibility = Visibility.Visible;
        }
        else
        {
            LineaUmbral.Visibility = Visibility.Collapsed;
        }

        // Punto vivo en la última muestra: el ojo encuentra el presente sin
        // tener que seguir la línea hasta el borde.
        var ultimoPunto = puntos[puntos.Count - 1];
        Canvas.SetLeft(PuntoFinal, ultimoPunto.X - 4.5);
        Canvas.SetTop(PuntoFinal, ultimoPunto.Y - 4.5);
        PuntoFinal.Visibility = Visibility.Visible;

        // Las franjas dicen cuánto miden: sin estas etiquetas el gráfico
        // solo mostraba «una punta», no cuántos milisegundos.
        TxtEscalaTope.Text = $"{maximo:0} ms";
        TxtEscalaMedio.Text = $"{(minimo + maximo) / 2:0} ms";
        TxtEscalaBase.Text = $"{minimo:0} ms";
    }

    /// <summary>
    /// Una franja vertical por cada muestra perdida. Sin esta capa, el
    /// arreglo que rellena los huecos con el último valor válido haría
    /// invisible justo el dato que más importa en una red: no cuánto tarda,
    /// sino cuánto se pierde.
    /// </summary>
    private void MarcarPerdidas(double w, double h)
    {
        CapaPerdidas.Children.Clear();
        if (_muestras.Count < 2) return;

        var pincel = Res("BBad");
        for (int i = 0; i < _muestras.Count; i++)
        {
            if (_muestras[i].HasValue) continue;

            double x = i / (double)(_muestras.Count - 1) * w;
            var franja = new Rectangle { Width = 2, Height = h, Fill = pincel, Opacity = 0.28 };
            Canvas.SetLeft(franja, x - 1);
            CapaPerdidas.Children.Add(franja);
        }
    }

    private static Brush Res(string clave) => (Brush)Application.Current.Resources[clave];
}
