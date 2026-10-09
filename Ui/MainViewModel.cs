using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using SysDiag.Core;
using SysDiag.Core.Diagnostics;
using SysDiag.Core.Drivers;
using SysDiag.Models;
using SysDiag.Services;

namespace SysDiag.Ui;

/// <summary>Una línea del registro, con el color que le corresponde a su nivel.</summary>
public class LogLine
{
    public string Texto { get; init; } = "";
    public Brush Color { get; init; } = Brushes.Gray;
}

/// <summary>
/// Tarjeta del panel Resumen. Trae su propia "regla de escala": dos pinceles de
/// marcas que se reparten según <see cref="Fill"/>, para leer la magnitud de un
/// vistazo sin necesidad de un gráfico.
/// </summary>
public class MetricCard
{
    public string Titulo { get; init; } = "";
    public string Valor { get; init; } = "";
    public string Nota { get; init; } = "";
    public Brush Acento { get; init; } = Brushes.Gray;
    public Brush TicksOn { get; init; }
    public Brush TicksOff { get; init; }
    public GridLength FillStar { get; init; }
    public GridLength RestStar { get; init; }

    /// <summary>Módulo que produjo la medición. Con el reporte fusionado, el
    /// resumen mezcla tarjetas de varias corridas y conviene saber de cuál viene.</summary>
    public string Modulo { get; init; } = "";

    public static MetricCard Create(string titulo, string valor, string nota, Brush acento,
                                    double fill, string modulo = "")
    {
        fill = Math.Clamp(fill, 0.04, 1.0);
        return new MetricCard
        {
            Titulo = titulo,
            Valor = valor,
            Nota = nota,
            Acento = acento,
            Modulo = modulo,
            TicksOn = Ticks(((SolidColorBrush)acento).Color),
            TicksOff = Ticks(Color.FromRgb(0x2A, 0x32, 0x3D)),
            FillStar = new GridLength(fill, GridUnitType.Star),
            RestStar = new GridLength(1 - fill, GridUnitType.Star)
        };
    }

    private static Brush Ticks(Color c)
    {
        var drawing = new GeometryDrawing(
            new SolidColorBrush(c), null,
            new RectangleGeometry(new Rect(0, 0, 1.5, 7)));

        var brush = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 6, 7),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
        brush.Freeze();
        return brush;
    }
}

public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    public ObservableCollection<LogLine> Registro { get; } = new();
    public ObservableCollection<Finding> Hallazgos { get; } = new();
    public ObservableCollection<MetricCard> Tarjetas { get; } = new();
    public ObservableCollection<string> Tablas { get; } = new();

    private readonly IScanService _scan = new ScanService();
    private bool _disposed;
    private bool _cancelable = true;
    private int _previousScore = -1;
    private List<(DateTime Fecha, int Puntaje)> _history = new();
    private readonly Dictionary<string, IList> _tablas = new();

    private CancellationTokenSource _cts;
    private string _moduloActivo = "completo";

    /// <summary>
    /// Qué tablas pertenecen a cada módulo. Al correr uno suelto, la vista
    /// Datos muestra solo lo suyo: ver dieciséis tablas de todo el equipo
    /// cuando acabas de medir la red es ruido, no información.
    /// </summary>
    private static readonly Dictionary<string, string[]> TablasPorModulo = new()
    {
        ["red"] = new[] { "Enlace Wi-Fi", "Latencia y jitter", "Traceroute", "Redes cercanas" },
        ["rendimiento"] = new[] { "Rendimiento", "Procesos por CPU", "Procesos por RAM" },
        ["termicas"] = new[] { "Térmicas", "Batería", "GPU" },
        ["seguridad"] = new[] { "Seguridad" },
        ["estabilidad"] = new[] { "Eventos (resumen)", "Eventos (detalle)", "Errores WHEA", "Volcados de memoria" },
        ["almacenamiento"] = new[] { "Almacenamiento", "Discos" },
        ["drivers"] = new[] { "Drivers disponibles", "Drivers" },
        ["arranque"] = new[] { "Arranque", "Servicios", "Programas instalados" },
        ["actualizaciones"] = new[] { "Actualizaciones disponibles" },
        ["limpieza"] = new[] { "Temporales" }
    };

    public DiagnosticReport Report { get; private set; } = new();

    public MainViewModel()
    {
        AppLog.Line += OnLog;
    }

    // ---- Estado observable ------------------------------------------------

    private string _titulo = "Sin diagnóstico";
    public string Titulo { get => _titulo; set => Set(ref _titulo, value); }

    private string _subtitulo = "Elige un módulo para empezar.";
    public string Subtitulo { get => _subtitulo; set => Set(ref _subtitulo, value); }

    private bool _ocupado;
    public bool Ocupado
    {
        get => _ocupado;
        set
        {
            Set(ref _ocupado, value);
            OnPropertyChanged(nameof(Libre));
            OnPropertyChanged(nameof(BarraVisible));
            OnPropertyChanged(nameof(ContenidoOpacidad));
            OnPropertyChanged(nameof(PuedeCancelar));
            OnPropertyChanged(nameof(PuedeExportar));
        }
    }

    public bool Libre => !_ocupado;
    public bool PuedeCancelar => _ocupado && _cancelable;
    public bool PuedeExportar => Libre && HayDatos;
    public Visibility BarraVisible => _ocupado ? Visibility.Visible : Visibility.Hidden;

    /// <summary>
    /// Mientras se mide, lo que sigue en pantalla son datos de corridas
    /// anteriores. Atenuarlos evita leerlos como si fueran el resultado en
    /// curso, que es justo lo que confunde cuando un módulo tarda.
    /// </summary>
    public double ContenidoOpacidad => _ocupado ? 0.4 : 1.0;

    private bool _hayDatos;
    public bool HayDatos
    {
        get => _hayDatos;
        set { Set(ref _hayDatos, value); OnPropertyChanged(nameof(PuedeExportar)); }
    }

    public string TextoVacio => AppEnv.IsAdmin
        ? "Elige un módulo del panel izquierdo. Si es la primera vez, «Diagnóstico completo» recopila red, rendimiento, térmicas, almacenamiento y estabilidad en una sola pasada."
        : "Todavía no hay mediciones útiles. Algunos módulos requieren permisos de administrador para consultar WMI, el registro y los contadores del sistema. Reinicia la app como administrador para completar el diagnóstico.";

    private Finding _hallazgoSel;
    public Finding HallazgoSeleccionado
    {
        get => _hallazgoSel;
        set
        {
            Set(ref _hallazgoSel, value);
            OnPropertyChanged(nameof(DetalleHallazgo));
            OnPropertyChanged(nameof(BotonReparar));
            OnPropertyChanged(nameof(TextoReparar));
        }
    }

    public Visibility BotonReparar =>
        _hallazgoSel != null && Remediation.Obtener(_hallazgoSel.AccionId) != null
            ? Visibility.Visible : Visibility.Collapsed;

    public string TextoReparar => Remediation.Obtener(_hallazgoSel?.AccionId)?.Titulo ?? "";

    public string DetalleHallazgo => _hallazgoSel == null
        ? "Selecciona un hallazgo para ver la recomendación."
        : string.IsNullOrWhiteSpace(_hallazgoSel.Action)
            ? _hallazgoSel.Message
            : _hallazgoSel.Action;

    private string _tablaSel;
    public string TablaSeleccionada
    {
        get => _tablaSel;
        set
        {
            Set(ref _tablaSel, value);
            OnPropertyChanged(nameof(FilasTabla));
            OnPropertyChanged(nameof(MostrarAyudaDrivers));
            OnPropertyChanged(nameof(MostrarAccionesDrivers));
            OnPropertyChanged(nameof(AvisoDrivers));
            OnPropertyChanged(nameof(TextoAvisoDrivers));
            OnPropertyChanged(nameof(MostrarAyudaUpdates));
        }
    }

    public IList FilasTabla =>
        _tablaSel != null && _tablas.TryGetValue(_tablaSel, out var lista) ? lista : null;

    public bool MostrarAyudaDrivers => _tablaSel == "Drivers";
    public bool MostrarAccionesDrivers => _tablaSel == "Drivers disponibles";

    public Visibility AvisoDrivers =>
        _tablaSel == "Drivers disponibles" && Report.DriversDisponibles.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;

    public string TextoAvisoDrivers => string.IsNullOrEmpty(DriverUpdateModule.UltimoError)
        ? "Windows Update no ofrece drivers más recientes para este equipo. Si un driver concreto sigue apareciendo viejo en la tabla «Drivers», el fabricante puede publicar una versión que Microsoft todavía no distribuye."
        : DriverUpdateModule.UltimoError;
    public bool MostrarAyudaUpdates => _tablaSel == "Actualizaciones disponibles";

    private int _puntaje = -1;
    public int Puntaje { get => _puntaje; private set => Set(ref _puntaje, value); }

    private string _puntajeEtiqueta = "sin datos";
    public string PuntajeEtiqueta { get => _puntajeEtiqueta; private set => Set(ref _puntajeEtiqueta, value); }

    private string _puntajeDesglose = "";
    public string PuntajeDesglose { get => _puntajeDesglose; private set => Set(ref _puntajeDesglose, value); }

    private string _puntajeTendencia = "";
    public string PuntajeTendencia { get => _puntajeTendencia; private set => Set(ref _puntajeTendencia, value); }

    private Brush _puntajeBrush = Brushes.Gray;
    public Brush PuntajeBrush { get => _puntajeBrush; private set => Set(ref _puntajeBrush, value); }

    private Brush _puntajeTicksOn;
    public Brush PuntajeTicksOn { get => _puntajeTicksOn; private set => Set(ref _puntajeTicksOn, value); }

    private Brush _puntajeTicksOff;
    public Brush PuntajeTicksOff { get => _puntajeTicksOff; private set => Set(ref _puntajeTicksOff, value); }

    private GridLength _puntajeFill = new(0.04, GridUnitType.Star);
    public GridLength PuntajeFill { get => _puntajeFill; private set => Set(ref _puntajeFill, value); }

    private GridLength _puntajeRest = new(0.96, GridUnitType.Star);
    public GridLength PuntajeRest { get => _puntajeRest; private set => Set(ref _puntajeRest, value); }

    private BarChart _graficoRed = new();
    public BarChart GraficoRed { get => _graficoRed; private set => Set(ref _graficoRed, value); }

    private BarChart _graficoProcesos = new();
    public BarChart GraficoProcesos { get => _graficoProcesos; private set => Set(ref _graficoProcesos, value); }

    private BarChart _graficoEventos = new();
    public BarChart GraficoEventos { get => _graficoEventos; private set => Set(ref _graficoEventos, value); }

    private HistoryChart _graficoHistorial = new();
    public HistoryChart GraficoHistorial { get => _graficoHistorial; private set => Set(ref _graficoHistorial, value); }

    private string _sugerencia = "";
    /// <summary>
    /// Qué conviene mirar después, según lo que ya se midió y lo que falta.
    /// Ocupa el espacio bajo las tarjetas con algo accionable en vez de dejarlo
    /// en blanco, y orienta a quien no sabe por dónde seguir.
    /// </summary>
    public string Sugerencia { get => _sugerencia; private set => Set(ref _sugerencia, value); }

    private string _equipo = "SysDiag";
    /// <summary>Va en la barra de título: identifica el equipo, no repite el módulo.</summary>
    public string Equipo { get => _equipo; private set => Set(ref _equipo, value); }

    private string _horaSistema = DateTime.Now.ToString("HH:mm");
    /// <summary>Reloj del sistema en la cabecera. Lo actualiza un temporizador
    /// de la ventana; el ViewModel solo expone dónde guardar el valor.</summary>
    public string HoraSistema { get => _horaSistema; set => Set(ref _horaSistema, value); }

    public bool EsAdmin => AppEnv.IsAdmin;
    public Visibility AvisoAdmin => AppEnv.IsAdmin ? Visibility.Collapsed : Visibility.Visible;
    public string Version => AppEnv.Version;

    // ---- Ejecución --------------------------------------------------------

    public void Cancelar() { if (PuedeCancelar) _cts?.Cancel(); }

    public Task<bool> RunAsync(string titulo,
        params (string Clave, Func<DiagnosticReport, CancellationToken, Task> Trabajo)[] pasos)
        => RunCoreAsync(titulo, true, pasos);

    public Task<bool> RunActionAsync(string titulo, Func<DiagnosticReport, CancellationToken, Task> trabajo,
        bool permiteCancelar = false, string modulo = "accion")
        => RunCoreAsync(titulo, permiteCancelar, (modulo, trabajo));

    private async Task<bool> RunCoreAsync(string titulo, bool permiteCancelar,
        params (string Clave, Func<DiagnosticReport, CancellationToken, Task> Trabajo)[] pasos)
    {
        if (Ocupado || _disposed || pasos.Length == 0) return false;
        _cts = new CancellationTokenSource();
        _cancelable = permiteCancelar;
        _moduloActivo = pasos.Length > 1 ? "completo" : pasos[0].Clave;
        Ocupado = true;
        Titulo = titulo;
        Subtitulo = permiteCancelar ? "Midiendo. El detalle va apareciendo en Registro."
            : "Operación en curso. No se puede interrumpir de forma segura; revisa Registro.";
        bool diagnostic = pasos.Any(p => DiagnosticReport.NombresModulos.ContainsKey(p.Clave));
        bool completed = false;
        bool archived = false;
        if (diagnostic)
        {
            Report.Id = Guid.NewGuid();
            Report.Inicio = DateTime.Now;
            Report.Fin = null;
        }
        Wmi.ResetAccessState();
        try
        {
            var services = pasos.Select(p => (IDiagnosticService)new DelegateDiagnosticService(p.Clave, p.Trabajo)).ToArray();
            Report = await _scan.EjecutarAsync(Report, services, _cts.Token);
            completed = true;
            if (diagnostic)
            {
                // Cobertura de ESTA corrida, no del reporte fusionado (que conserva módulos de corridas anteriores).
                // Tendencia y comparación usan la misma cobertura: no mezclar un «Red» suelto con uno completo.
                string[] ejecutados = pasos.Select(p => p.Clave)
                    .Where(k => DiagnosticReport.NombresModulos.ContainsKey(k))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                // JSON y lectura de historial pueden ser grandes: nunca bloquear el dispatcher.
                var history = await Task.Run(() =>
                {
                    int previous = Exporter.PuntajeAnterior(Report.Inicio, modulos: ejecutados);
                    bool saved = Exporter.Archivar(Report.ParaArchivo(ejecutados));
                    return (previous, saved, series: Exporter.Historial(modulos: ejecutados));
                });
                _previousScore = history.previous;
                archived = history.saved;
                _history = history.series;
            }
            AppLog.Write($"{titulo}: completado.", "OK");
            int bad = Report.Hallazgos.Count(f => f.Severity == Severity.Bad);
            int warnings = Report.Hallazgos.Count(f => f.Severity == Severity.Warn);
            Subtitulo = diagnostic
                ? $"{bad} crítico(s) · {warnings} aviso(s) · finalizado a las {DateTime.Now:HH:mm}. {Report.ResumenEstado()}"
                : "Operación completada. Repite el módulo correspondiente para verificar el efecto.";
        }
        catch (OperationCanceledException)
        {
            AppLog.Write($"{titulo}: cancelado. Se conservaron únicamente los pasos que terminaron.", "WARN");
            Subtitulo = "Cancelado: los datos visibles pueden incluir mediciones anteriores. No se archivó como diagnóstico completado.";
        }
        catch (Exception ex)
        {
            AppLog.Write($"{titulo}: {ex}", "ERROR");
            Subtitulo = "No se completó la operación. Revisa Registro; si hubo cambios, usa el respaldo para restaurarlos.";
            Dialog.Error("No se pudo completar la operación", ex.Message);
        }
        finally
        {
            if (diagnostic) Report.Fin = DateTime.Now;
            _cts.Dispose();
            _cts = null;
            Ocupado = false;
            Refresh(archived);
        }
        return completed;
    }

    // ---- Pintado ----------------------------------------------------------

    private void Refresh(bool archivar)
    {
        if (Report.Sistema.Count > 0) Equipo = Report.Equipo;

        BuildScore(archivar);
        BuildCards();
        BuildCharts();
        BuildFindings();
        BuildTables();

        HayDatos = Report.TieneDatosRelevantes();
        BuildSugerencia();

    }

    private void BuildCharts()
    {
        // --- Latencia por destino: la comparación es el punto, no el número ---
        GraficoRed = BarChart.Crear("Latencia por destino",
            "Cuanto más larga la barra, más tarda la respuesta. Comparar el router con los demás separa un problema de tu red de uno del proveedor.",
            Report.Red
                .Where(x => x.Media > 0)
                .Select(x => (x.Destino, x.Media, $"{x.Media} ms", Pincel(x.Estado),
                              $"jitter {x.Jitter} ms · pérdida {x.PerdidaPct} %")));

        // --- Procesos por CPU ---
        GraficoProcesos = BarChart.Crear("Procesos que más CPU consumen",
            "Medido como diferencia real durante el muestreo, no como tiempo acumulado desde que arrancó cada proceso.",
            Report.TopCpu
                .Where(x => x.CpuPct > 0)
                .Take(6)
                .Select(x => (x.Proceso, x.CpuPct, $"{x.CpuPct} %",
                              Pincel(x.CpuPct > 40 ? Severity.Warn : Severity.Ok),
                              $"{x.RamMb} MB de memoria")));

        // --- Eventos críticos por tipo ---
        GraficoEventos = BarChart.Crear("Eventos críticos por tipo",
            $"Repeticiones en los últimos {StabilityModule.EventDays} días. Un tipo que se repite miles de veces señala un problema persistente, no un incidente aislado.",
            Report.EventosResumen
                .Take(6)
                .Select(x => ($"ID {x.Id}", (double)x.Ocurrencias, x.Ocurrencias.ToString("N0"),
                              Pincel(x.Ocurrencias > 100 ? Severity.Bad : Severity.Warn),
                              x.Descripcion)));

        GraficoHistorial = HistoryChart.Crear(_history);
    }

    private void BuildSugerencia()
    {
        // Lo urgente manda sobre lo que falte por medir.
        var critico = Report.Hallazgos.FirstOrDefault(h => h.Severity == Severity.Bad);
        if (critico != null)
        {
            Sugerencia = $"Hay un hallazgo crítico en {critico.Area}: {critico.Message} " +
                         "Revísalo en la pestaña Hallazgos, donde está la recomendación completa.";
            return;
        }

        var faltantes = Report.ModulosFaltantes();

        if (faltantes.Count > 0)
        {
            Sugerencia = $"Todavía no has medido: {string.Join(", ", faltantes)}. " +
                         "El resumen se completa a medida que corres cada módulo.";
            return;
        }

        if (!AppEnv.IsAdmin)
        {
            Sugerencia = "Ya cubriste todos los módulos. Reiniciar como administrador " +
                         "desbloquea los contadores de fiabilidad del disco y el registro de eventos completo.";
            return;
        }

        Sugerencia = "Ya cubriste todos los módulos. Genera el informe para guardar el estado, " +
                     "o vuelve a medir después de un cambio para comparar contra este diagnóstico.";
    }

    private void BuildScore(bool archivar)
    {
        int puntaje = HealthScore.Calcular(Report);
        Report.Puntaje = puntaje;

        Puntaje = puntaje;
        PuntajeEtiqueta = HealthScore.Etiqueta(puntaje);
        if (puntaje >= 0 && Report.ModulosFaltantes().Count > 0) PuntajeEtiqueta += " · parcial";
        PuntajeDesglose = puntaje < 0 ? "" : HealthScore.Desglose(Report);
        PuntajeBrush = Pincel(HealthScore.Nivel(puntaje));

        double fill = puntaje < 0 ? 0.04 : Math.Clamp(puntaje / 100.0, 0.04, 1.0);
        PuntajeFill = new GridLength(fill, GridUnitType.Star);
        PuntajeRest = new GridLength(1 - fill, GridUnitType.Star);

        var tarjeta = MetricCard.Create("", "", "", PuntajeBrush, fill);
        PuntajeTicksOn = tarjeta.TicksOn;
        PuntajeTicksOff = tarjeta.TicksOff;

        if (puntaje < 0)
        {
            PuntajeTendencia = "";
            return;
        }

        // Se archiva y se compara contra la corrida anterior: una medición
        // aislada no dice si algo mejoró o empeoró.
        if (!archivar)
        {
            PuntajeTendencia = "No se archivó una nueva medición completada.";
            return;
        }
        int anterior = _previousScore;

        PuntajeTendencia = anterior < 0
            ? "Primer diagnóstico guardado. El próximo se comparará contra este."
            : puntaje > anterior
                ? $"Mejoró {puntaje - anterior} puntos respecto al diagnóstico anterior ({anterior})."
                : puntaje < anterior
                    ? $"Bajó {anterior - puntaje} puntos respecto al diagnóstico anterior ({anterior})."
                    : $"Sin cambios respecto al diagnóstico anterior ({anterior}).";
    }

    private Brush Pincel(Severity s) => s switch
    {
        Severity.Bad => Res("BBad"),
        Severity.Warn => Res("BWarn"),
        _ => Res("BOk")
    };

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    private void BuildCards()
    {
        Tarjetas.Clear();

        var riot = Report.Red.Where(x => x.Destino.StartsWith("Riot") && x.Media > 0)
            .OrderBy(x => x.Media).FirstOrDefault();
        var red = riot ?? Report.Red.FirstOrDefault(x => x.Destino == "Salida a internet");
        if (red != null)
        {
            Tarjetas.Add(MetricCard.Create($"Latencia · {red.Destino}", $"{red.Media} ms",
                $"jitter {red.Jitter} ms · pérdida {red.PerdidaPct} %",
                Pincel(red.Estado), red.Media / 150.0, "red"));
        }

        var cpu = Report.RendimientoResumen.FirstOrDefault(x => x.Clave == "CPU total");
        if (cpu != null && NumericText.TryRead(cpu.Valor, out double cpuPercent))
        {
            Tarjetas.Add(MetricCard.Create("Uso de CPU", cpu.Valor,
                Report.RendimientoResumen.FirstOrDefault(x => x.Clave == "RAM en uso")?.Valor ?? "",
                Pincel(cpuPercent > 85 ? Severity.Bad : cpuPercent > 60 ? Severity.Warn : Severity.Ok), cpuPercent / 100.0,
                "rendimiento"));
        }

        var desgaste = Report.Bateria.FirstOrDefault(x => x.Clave == "Desgaste de la batería");
        if (desgaste != null)
        {
            double pct = Leer(desgaste.Valor);
            Tarjetas.Add(MetricCard.Create("Desgaste de batería", desgaste.Valor,
                "contra la capacidad de fábrica",
                Pincel(pct >= 30 ? Severity.Bad : pct >= 15 ? Severity.Warn : Severity.Ok), pct / 100.0,
                "termicas"));
        }

        if (Report.Seguridad.Count > 0)
        {
            int riesgos = Report.Seguridad.Count(x => x.Nivel == Severity.Bad);
            int avisosSeg = Report.Seguridad.Count(x => x.Nivel == Severity.Warn);
            var peor = riesgos > 0 ? Severity.Bad : avisosSeg > 0 ? Severity.Warn : Severity.Ok;
            string texto = riesgos > 0 ? "Requiere atención" : avisosSeg > 0 ? "Con avisos" : "Protegido";

            Tarjetas.Add(MetricCard.Create("Seguridad", texto,
                $"{Report.Seguridad.Count} componentes revisados", Pincel(peor),
                riesgos > 0 ? 1.0 : avisosSeg > 0 ? 0.55 : 0.2, "seguridad"));
        }

        if (Report.Whea.Count > 0)
        {
            Tarjetas.Add(MetricCard.Create("Errores WHEA", Report.Whea.Count.ToString(),
                $"últimos {StabilityModule.WheaDays} días",
                Pincel(Report.Whea.Count >= 20 ? Severity.Bad : Severity.Warn),
                Math.Min(Report.Whea.Count / 50.0, 1.0), "estabilidad"));
        }

        if (Report.EventosResumen.Count > 0)
        {
            int total = Report.EventosResumen.Sum(x => x.Ocurrencias);
            Tarjetas.Add(MetricCard.Create("Eventos críticos", total.ToString(),
                $"últimos {StabilityModule.EventDays} días", Pincel(Severity.Warn),
                Math.Min(total / 200.0, 1.0), "estabilidad"));
        }

        var disco = Report.Almacenamiento.FirstOrDefault();
        if (disco != null)
        {
            // Sin contadores de fiabilidad (requieren administrador) el desgaste
            // llega como «n/d»: mostrarlo es ruido, mejor omitir esa parte.
            var detalle = new List<string> { disco.Tipo };
            if (disco.Desgaste != "n/d") detalle.Add($"desgaste {disco.Desgaste}");
            if (disco.Horas != "n/d") detalle.Add(disco.Horas);

            Tarjetas.Add(MetricCard.Create("Salud del disco", disco.Salud,
                string.Join(" · ", detalle), Pincel(disco.Estado),
                disco.Estado == Severity.Ok ? 0.25 : disco.Estado == Severity.Warn ? 0.6 : 1.0,
                "almacenamiento"));
        }

        if (Report.Arranque.Count > 0)
        {
            Tarjetas.Add(MetricCard.Create("Programas al inicio", Report.Arranque.Count.ToString(),
                $"{Report.Servicios.Count} servicios automáticos",
                Pincel(Report.Arranque.Count >= 20 ? Severity.Warn : Severity.Ok),
                Math.Min(Report.Arranque.Count / 30.0, 1.0), "arranque"));
        }

        if (Report.DriversDisponibles.Count > 0)
        {
            Tarjetas.Add(MetricCard.Create("Drivers disponibles",
                Report.DriversDisponibles.Count.ToString(), "en Windows Update",
                Pincel(Severity.Warn),
                Math.Min(Report.DriversDisponibles.Count / 6.0, 1.0), "drivers"));
        }

        if (Report.Actualizaciones.Count > 0)
        {
            Tarjetas.Add(MetricCard.Create("Actualizaciones", Report.Actualizaciones.Count.ToString(),
                "programas con versión nueva",
                Pincel(Report.Actualizaciones.Count >= 10 ? Severity.Warn : Severity.Ok),
                Math.Min(Report.Actualizaciones.Count / 20.0, 1.0), "actualizaciones"));
        }

        var temp = Report.Termicas.FirstOrDefault(x => x.Clave == "Temperatura (ACPI)");
        if (temp != null && temp.Valor.Contains("°C"))
        {
            double c = Leer(temp.Valor);
            Tarjetas.Add(MetricCard.Create("Temperatura", temp.Valor,
                Report.Termicas.FirstOrDefault(x => x.Clave == "Frecuencia actual")?.Valor ?? "",
                Pincel(c >= 90 ? Severity.Bad : c >= 80 ? Severity.Warn : Severity.Ok), c / 100.0,
                "termicas"));
        }
    }

    private static double Leer(string text) => NumericText.TryRead(text, out double value) ? value : 0;

    private void BuildFindings()
    {
        Hallazgos.Clear();
        foreach (var f in Report.Hallazgos.OrderBy(x =>
                     x.Severity == Severity.Bad ? 0 : x.Severity == Severity.Warn ? 1 : 2))
            Hallazgos.Add(f);

        HallazgoSeleccionado = Hallazgos.FirstOrDefault();
    }

    private void BuildTables()
    {
        string previa = TablaSeleccionada;

        _tablas.Clear();
        Offer("Equipo", Report.Sistema);
        Offer("Discos", Report.Discos);
        Offer("Módulos de RAM", Report.Memoria);
        Offer("Enlace Wi-Fi", Report.WiFi);
        Offer("Latencia y jitter", Report.Red);
        Offer("Traceroute", Report.Traceroute);
        Offer("Redes cercanas", Report.RedesCercanas);
        Offer("Rendimiento", Report.RendimientoResumen);
        Offer("Procesos por CPU", Report.TopCpu);
        Offer("Procesos por RAM", Report.TopRam);
        Offer("Térmicas", Report.Termicas);
        Offer("Batería", Report.Bateria);
        Offer("GPU", Report.Gpus);
        Offer("Seguridad", Report.Seguridad);
        Offer("Eventos (resumen)", Report.EventosResumen);
        Offer("Eventos (detalle)", Report.EventosDetalle);
        Offer("Errores WHEA", Report.Whea);
        Offer("Volcados de memoria", Report.Minidumps);
        Offer("Almacenamiento", Report.Almacenamiento);
        Offer("Drivers disponibles", Report.DriversDisponibles, BusquedaDriversHecha);
        Offer("Drivers", Report.Drivers);
        Offer("Actualizaciones disponibles", Report.Actualizaciones);
        Offer("Arranque", Report.Arranque);
        Offer("Servicios", Report.Servicios);
        Offer("Programas instalados", Report.Programas);
        Offer("Temporales", Report.Limpieza);

        // Un módulo suelto muestra lo suyo primero; el inventario del equipo
        // queda al final, disponible pero sin estorbar.
        List<string> orden;
        if (_moduloActivo != "completo" && TablasPorModulo.TryGetValue(_moduloActivo, out var propias))
        {
            orden = propias.Where(_tablas.ContainsKey).ToList();
            orden.AddRange(new[] { "Equipo", "Discos", "Módulos de RAM" }
                .Where(t => _tablas.ContainsKey(t) && !orden.Contains(t)));
        }
        else
        {
            orden = _tablas.Keys.ToList();
        }

        Tablas.Clear();
        foreach (var k in orden) Tablas.Add(k);

        TablaSeleccionada = previa != null && Tablas.Contains(previa)
            ? previa
            : Tablas.FirstOrDefault();
    }

    private void Offer(string nombre, IList lista, bool aunqueVacia = false)
    {
        if (lista is { Count: > 0 } || (aunqueVacia && lista != null))
            _tablas[nombre] = lista;
    }

    /// <summary>
    /// Tablas que deben aparecer aunque estén vacías, porque el vacío mismo es
    /// el resultado que el usuario necesita ver (con su explicación al lado).
    /// </summary>
    public bool BusquedaDriversHecha { get; set; }

    // ---- Registro ---------------------------------------------------------

    private readonly List<LogLine> _pendientes = new();
    private bool _volcadoProgramado;

    private void OnLog(string linea, string nivel)
    {
        var app = Application.Current;
        if (_disposed || app == null || app.Dispatcher.HasShutdownStarted || app.Dispatcher.HasShutdownFinished) return;

        string clave = nivel switch
        {
            "OK" => "BOk",
            "WARN" => "BWarn",
            "ERROR" => "BBad",
            "STEP" => "BAccent",
            _ => "BTextDim"
        };

        app.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_disposed) return;
            _pendientes.Add(new LogLine { Texto = linea, Color = Res(clave) });

            // Las líneas llegan en ráfaga (un traceroute suelta decenas de
            // golpe). Añadirlas de a una obliga a la lista a recalcular su
            // diseño cada vez; acumularlas y volcarlas juntas evita ese trabajo.
            if (_volcadoProgramado) return;
            _volcadoProgramado = true;

            app.Dispatcher.BeginInvoke(new Action(Volcar),
                System.Windows.Threading.DispatcherPriority.Background);
        }));
    }

    private void Volcar()
    {
        _volcadoProgramado = false;
        if (_disposed || _pendientes.Count == 0) return;

        foreach (var l in _pendientes) Registro.Add(l);
        _pendientes.Clear();

        // El registro es una traza en vivo, no un archivo: el histórico
        // completo queda en disco, así que en pantalla basta lo reciente.
        while (Registro.Count > 1500) Registro.RemoveAt(0);

        LineaAgregada?.Invoke();
    }

    /// <summary>Avisa a la vista que hay líneas nuevas, para seguir el final.</summary>
    public event Action LineaAgregada;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        AppLog.Line -= OnLog;
        _cts?.Cancel();
        if (!Ocupado) _cts?.Dispose();
        _pendientes.Clear();
    }

    // ---- INotifyPropertyChanged ------------------------------------------

    public event PropertyChangedEventHandler PropertyChanged;

    private void Set<T>(ref T campo, T valor, [CallerMemberName] string prop = null)
    {
        if (Equals(campo, valor)) return;
        campo = valor;
        OnPropertyChanged(prop);
    }

    private void OnPropertyChanged([CallerMemberName] string prop = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}
