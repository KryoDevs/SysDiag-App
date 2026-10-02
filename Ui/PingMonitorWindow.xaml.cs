using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
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

    private void Iniciar()
    {
        if (ComboDestino.SelectedItem is not DestinoPing destino)
        {
            Dialog.Info("Elegí un destino", "No hay ningún destino seleccionado para medir.");
            return;
        }

        _muestras.Clear();
        _ultimoValido = null;
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
    }

    private void Redibujar()
    {
        double w = Lienzo.ActualWidth;
        double h = Lienzo.ActualHeight;
        if (w <= 0 || h <= 0 || _muestras.Count < 2) return;

        // Los huecos por pérdida se rellenan con el último valor válido solo
        // para que la línea no se corte — la pérdida real sigue viéndose
        // aparte, en el número de arriba. Es una simplificación deliberada,
        // no un intento de esconder la pérdida.
        var valores = new List<double>();
        double ultimo = _muestras.FirstOrDefault(m => m.HasValue) ?? 0;
        foreach (var m in _muestras)
        {
            if (m.HasValue) ultimo = m.Value;
            valores.Add(ultimo);
        }

        double max = Math.Max(valores.Max(), 20); // piso de escala para que no se vea plano con pings muy bajos
        double min = 0;

        var puntos = new PointCollection();
        for (int i = 0; i < valores.Count; i++)
        {
            double x = valores.Count == 1 ? 0 : i / (double)(valores.Count - 1) * w;
            double y = h - (valores[i] - min) / (max - min) * h;
            puntos.Add(new Point(x, y));
        }

        Linea.Points = puntos;

        var area = new PointCollection(puntos) { new Point(w, h), new Point(0, h) };
        Area.Points = area;

        LineaTope.X1 = 0; LineaTope.X2 = w; LineaTope.Y1 = 0; LineaTope.Y2 = 0;
        LineaMedio.X1 = 0; LineaMedio.X2 = w; LineaMedio.Y1 = h / 2; LineaMedio.Y2 = h / 2;
        LineaBase.X1 = 0; LineaBase.X2 = w; LineaBase.Y1 = h - 1; LineaBase.Y2 = h - 1;
    }

    private static Brush Res(string clave) => (Brush)Application.Current.Resources[clave];
}
