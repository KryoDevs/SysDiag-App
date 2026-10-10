using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
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
    /// <summary>
    /// Nivel tal como lo emite AppLog («INFO», «WARN», «ERROR», «STEP», «OK»).
    /// El texto visible ya lo trae entre corchetes, pero parsear el renglón
    /// para filtrar es frágil: en cuanto el formato cambie, el filtro deja de
    /// encontrar nada y no hay forma de darse cuenta. El nivel viaja aparte.
    /// </summary>
    public string Nivel { get; init; } = "INFO";
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
            TicksOff = Ticks(Color.FromRgb(0x1A, 0x22, 0x40)),
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

/// <summary>Acción rápida de la banda de sección (icono + texto + destino).</summary>
public class AccionRapida
{
    public string Id { get; init; } = "";
    public string Texto { get; init; } = "";
    public string Icono { get; init; } = "";
    public bool EsPrimario { get; init; }
}

/// <summary>
/// Identidad de la sección activa: qué módulo se está mirando, con qué color
/// se presenta y qué acciones propias ofrece. Es lo que hace que cada sección
/// no se vea igual a todas las demás.
/// </summary>
public class ContextoModulo
{
    public string Nombre { get; init; } = "";
    public string Icono { get; init; } = "";
    public string Descripcion { get; init; } = "";
    /// <summary>Clave de recurso del color de acento de la sección («BAccent2», «BWarn»…).</summary>
    public string ColorClave { get; init; } = "BAccent";
    public List<AccionRapida> Acciones { get; init; } = new();
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

    /// <summary>
    /// Identidad y acciones propias de cada sección. El color sigue la
    /// familia: análisis en cian, mantenimiento en violeta, sistema en
    /// ámbar y datos en verde — la misma persona siempre reconoce dónde está.
    /// </summary>
    private static readonly Dictionary<string, ContextoModulo> Contextos = new()
    {
        ["completo"] = new ContextoModulo
        {
            Nombre = "Diagnóstico completo", Icono = "\uE9D9", ColorClave = "BAccent",
            Descripcion = "Una pasada por red, rendimiento, térmicas, almacenamiento y estabilidad.",
            Acciones = new()
            {
                new AccionRapida { Id = "ping-monitor", Texto = "Monitor de ping", Icono = "\uE9D9" },
                new AccionRapida { Id = "historial", Texto = "Historial", Icono = "\uE81C" },
                new AccionRapida { Id = "informe", Texto = "Generar informe", Icono = "\uE896", EsPrimario = true }
            }
        },
        ["red"] = new ContextoModulo
        {
            Nombre = "Red y latencia", Icono = "\uEC05", ColorClave = "BAccent2",
            Descripcion = "Latencia, jitter, pérdida, Wi-Fi, canales y traceroute.",
            Acciones = new()
            {
                new AccionRapida { Id = "ping-monitor", Texto = "Monitor de ping", Icono = "\uE9D9", EsPrimario = true },
                new AccionRapida { Id = "limpiar-dns", Texto = "Vaciar caché DNS", Icono = "\uE9F5" },
                new AccionRapida { Id = "historial", Texto = "Historial", Icono = "\uE81C" }
            }
        },
        ["ping-monitor"] = new ContextoModulo
        {
            Nombre = "Monitor de ping", Icono = "\uE9D9", ColorClave = "BAccent2",
            Descripcion = "Un ping por segundo en vivo: ve si un pico de lag coincide con algo puntual.",
            Acciones = new()
            {
                new AccionRapida { Id = "ping-monitor", Texto = "Abrir monitor", Icono = "\uE9D9", EsPrimario = true },
                new AccionRapida { Id = "ir-red", Texto = "Medir red completa", Icono = "\uEC05" }
            }
        },
        ["rendimiento"] = new ContextoModulo
        {
            Nombre = "Rendimiento", Icono = "\uE9D2", ColorClave = "BOk",
            Descripcion = "CPU real por proceso, memoria y disco medidos en vivo.",
            Acciones = new()
            {
                new AccionRapida { Id = "ver-procesos", Texto = "Ver procesos por CPU", Icono = "\uE9D2", EsPrimario = true },
                new AccionRapida { Id = "optimizar", Texto = "Optimizar", Icono = "\uE9F5" }
            }
        },
        ["termicas"] = new ContextoModulo
        {
            Nombre = "Térmicas y energía", Icono = "\uE9CA", ColorClave = "BWarn",
            Descripcion = "Temperatura, frecuencia, throttling, batería y GPU.",
            Acciones = new()
            {
                new AccionRapida { Id = "energia", Texto = "Opciones de energía", Icono = "\uE9CA", EsPrimario = true },
                new AccionRapida { Id = "optimizar", Texto = "Optimizar", Icono = "\uE9F5" }
            }
        },
        ["almacenamiento"] = new ContextoModulo
        {
            Nombre = "Almacenamiento", Icono = "\uEDA2", ColorClave = "BAccent",
            Descripcion = "Salud SMART, desgaste, temperatura y errores del disco.",
            Acciones = new()
            {
                new AccionRapida { Id = "limpieza", Texto = "Limpieza", Icono = "\uE74D", EsPrimario = true },
                new AccionRapida { Id = "diskmgmt", Texto = "Administrar discos", Icono = "\uEDA2" }
            }
        },
        ["seguridad"] = new ContextoModulo
        {
            Nombre = "Seguridad", Icono = "\uEA18", ColorClave = "BAccent2",
            Descripcion = "Defender, Firewall, BitLocker, TPM, Secure Boot y UAC — solo lectura.",
            Acciones = new()
            {
                new AccionRapida { Id = "defender", Texto = "Seguridad de Windows", Icono = "\uEA18", EsPrimario = true },
                new AccionRapida { Id = "ver-hallazgos", Texto = "Ver hallazgos", Icono = "\uE7BA" }
            }
        },
        ["estabilidad"] = new ContextoModulo
        {
            Nombre = "Estabilidad", Icono = "\uE7BA", ColorClave = "BWarn",
            Descripcion = "Reinicios inesperados, pantallazos, WHEA y volcados de memoria.",
            Acciones = new()
            {
                new AccionRapida { Id = "crear-punto", Texto = "Crear punto de restauración", Icono = "\uE777", EsPrimario = true },
                new AccionRapida { Id = "ver-eventos", Texto = "Ver eventos", Icono = "\uE7BA" }
            }
        },
        ["drivers"] = new ContextoModulo
        {
            Nombre = "Drivers", Icono = "\uE950", ColorClave = "BAccent",
            Descripcion = "Inventario de controladores y novedades de Windows Update para este hardware.",
            Acciones = new()
            {
                new AccionRapida { Id = "buscar-drivers", Texto = "Buscar drivers nuevos", Icono = "\uE950", EsPrimario = true },
                new AccionRapida { Id = "abrir-devmgmt", Texto = "Administrador de dispositivos", Icono = "\uE950" },
                new AccionRapida { Id = "verificar-driver", Texto = "Verificar driver descargado", Icono = "\uEA18" }
            }
        },
        ["arranque"] = new ContextoModulo
        {
            Nombre = "Arranque y software", Icono = "\uE7B5", ColorClave = "BAccent",
            Descripcion = "Programas al inicio, servicios y software instalado.",
            Acciones = new()
            {
                new AccionRapida { Id = "taskmgr", Texto = "Administrador de tareas", Icono = "\uE7B5", EsPrimario = true },
                new AccionRapida { Id = "servicios", Texto = "Servicios", Icono = "\uE7B5" }
            }
        },
        ["actualizaciones"] = new ContextoModulo
        {
            Nombre = "Actualizaciones", Icono = "\uE896", ColorClave = "BOk",
            Descripcion = "Programas con versión nueva disponible vía winget y actualizaciones de Windows.",
            Acciones = new()
            {
                new AccionRapida { Id = "actualizar-todo", Texto = "Actualizar todo", Icono = "\uE896", EsPrimario = true },
                new AccionRapida { Id = "abrir-wu", Texto = "Windows Update", Icono = "\uE896" }
            }
        },
        ["limpieza"] = new ContextoModulo
        {
            Nombre = "Limpieza", Icono = "\uE74D", ColorClave = "BAccent",
            Descripcion = "Calcula y borra archivos temporales con categorías seguras y reversibles donde aplica.",
            Acciones = new()
            {
                new AccionRapida { Id = "limpieza", Texto = "Analizar temporales", Icono = "\uE74D", EsPrimario = true },
                new AccionRapida { Id = "crear-punto", Texto = "Crear punto de restauración", Icono = "\uE777" }
            }
        },
        ["optimizar"] = new ContextoModulo
        {
            Nombre = "Optimizar", Icono = "\uE9F5", ColorClave = "BAccent",
            Descripcion = "Optimizaciones rápidas reversibles de red y energía, más los ajustes de Windows 10/11.",
            Acciones = new()
            {
                new AccionRapida { Id = "optimizar", Texto = "Optimizaciones", Icono = "\uE9F5", EsPrimario = true },
                new AccionRapida { Id = "tweaks", Texto = "Ajustes de Windows", Icono = "\uE713" }
            }
        },
        ["tweaks"] = new ContextoModulo
        {
            Nombre = "Ajustes de Windows", Icono = "\uE713", ColorClave = "BWarn",
            Descripcion = "Ajustes de Windows 10 y 11 por categorías, con respaldo y reversión total.",
            Acciones = new()
            {
                new AccionRapida { Id = "tweaks", Texto = "Abrir ajustes", Icono = "\uE713", EsPrimario = true },
                new AccionRapida { Id = "optimizar", Texto = "Optimizaciones", Icono = "\uE9F5" }
            }
        },
        ["perfiles"] = new ContextoModulo
        {
            Nombre = "Perfiles", Icono = "\uE8FC", ColorClave = "BAccent",
            Descripcion = "Universidad, Trabajo o Juego: combinaciones listas de energía y red.",
            Acciones = new()
            {
                new AccionRapida { Id = "perfiles", Texto = "Abrir perfiles", Icono = "\uE8FC", EsPrimario = true },
                new AccionRapida { Id = "optimizar", Texto = "Optimizar", Icono = "\uE9F5" }
            }
        },
        ["punto-restauracion"] = new ContextoModulo
        {
            Nombre = "Punto de restauración", Icono = "\uE777", ColorClave = "BWarn",
            Descripcion = "Un punto de restauración completo de Windows, para volver atrás si algo sale mal.",
            Acciones = new()
            {
                new AccionRapida { Id = "crear-punto", Texto = "Crear punto", Icono = "\uE777", EsPrimario = true },
                new AccionRapida { Id = "rstrui", Texto = "Restaurar sistema", Icono = "\uE7A7" }
            }
        },
        ["restaurar"] = new ContextoModulo
        {
            Nombre = "Restaurar estado", Icono = "\uE7A7", ColorClave = "BWarn",
            Descripcion = "Deshace la última optimización con el respaldo guardado antes de aplicarla.",
            Acciones = new()
            {
                new AccionRapida { Id = "restaurar", Texto = "Restaurar estado previo", Icono = "\uE7A7", EsPrimario = true },
                new AccionRapida { Id = "crear-punto", Texto = "Crear punto", Icono = "\uE777" }
            }
        },
        ["historial"] = new ContextoModulo
        {
            Nombre = "Historial", Icono = "\uE81C", ColorClave = "BOk",
            Descripcion = "Diagnósticos guardados: abrí cualquiera para ver sus hallazgos.",
            Acciones = new()
            {
                new AccionRapida { Id = "historial", Texto = "Abrir historial", Icono = "\uE81C", EsPrimario = true },
                new AccionRapida { Id = "export-json", Texto = "Exportar todo", Icono = "\uE896" }
            }
        },
        ["ajustes"] = new ContextoModulo
        {
            Nombre = "Ajustes", Icono = "\uE713", ColorClave = "BOk",
            Descripcion = "Segundos de muestreo, cantidad de pings, ventanas de días y licencia.",
            Acciones = new()
            {
                new AccionRapida { Id = "ajustes", Texto = "Abrir ajustes", Icono = "\uE713", EsPrimario = true },
                new AccionRapida { Id = "licencia", Texto = "Activación", Icono = "\uEA18" }
            }
        },
        ["activacion-windows"] = new ContextoModulo
        {
            Nombre = "Activación de Windows", Icono = "\uE975", ColorClave = "BOk",
            Descripcion = "Estado de la licencia de Windows 10/11 y activación por los canales oficiales de Microsoft.",
            Acciones = new()
            {
                new AccionRapida { Id = "activacion-windows", Texto = "Abrir activación", Icono = "\uE975", EsPrimario = true },
                new AccionRapida { Id = "abrir-activacion-os", Texto = "Ajustes de Windows", Icono = "\uE713" },
                new AccionRapida { Id = "crear-punto", Texto = "Crear punto", Icono = "\uE777" }
            }
        }
    };

    public DiagnosticReport Report { get; private set; } = new();

    public MainViewModel()
    {
        AppLog.Line += OnLog;
        Core.Licensing.LicenseService.EstadoCambiado += RefrescarLicencia;
        SetContexto("completo");
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

    // ---- Avance de la corrida ---------------------------------------------
    //
    // La barra del pie era indeterminada incluso cuando este propio ViewModel
    // tenía el conteo: RunCoreAsync recibe la lista de pasos. «3 de 5 · Red y
    // latencia» no es una estimación, es el estado real, y cambia cómo se
    // atraviesa una corrida de cuarenta segundos. Se conserva el modo
    // indeterminado para las acciones de un solo paso (optimizar, limpiar),
    // donde no hay nada que contar y un 0/1 quieto sería peor que la barra que
    // respira: ahí la señal de vida la da el punto de la cabecera.

    private int _avadeTotal;
    private int _avadeHechos;
    private bool _avadeIndeterminado;
    private string _avadeModulo = "";

    public int AvadeTotal
    {
        get => _avadeTotal;
        set { Set(ref _avadeTotal, value); OnPropertyChanged(nameof(AvadeTexto)); }
    }

    public int AvadeHechos
    {
        get => _avadeHechos;
        set { Set(ref _avadeHechos, value); OnPropertyChanged(nameof(AvadeTexto)); }
    }

    /// <summary>True cuando el conteo no dice nada (uno o cero pasos).</summary>
    public bool AvadeIndeterminado { get => _avadeIndeterminado; set => Set(ref _avadeIndeterminado, value); }

    /// <summary>Módulo que se está midiendo, para el rótulo del pie.</summary>
    public string AvadeModulo
    {
        get => _avadeModulo;
        set { Set(ref _avadeModulo, value); OnPropertyChanged(nameof(AvadeTexto)); }
    }

    // El cronómetro del paso. Existe por una razón concreta: con un módulo de WMI
    // colgado, «midiendo» y «no va a terminar» se ven idénticos, y esa es la
    // diferencia entre esperar y reiniciar. Un DispatcherTimer de un segundo que
    // solo vive durante la corrida no cuesta nada cuando la app está quieta —que es
    // casi siempre— y no suma un reloj permanente al de la cabecera.
    private DispatcherTimer _cronometro;
    private Stopwatch _relojPaso;
    private int _segundosPaso;

    /// <summary>Segundos que lleva el módulo actual midiendo.</summary>
    public int SegundosPaso
    {
        get => _segundosPaso;
        private set
        {
            Set(ref _segundosPaso, value);
            OnPropertyChanged(nameof(AvadeTexto));
        }
    }

    private void IniciarCronometro()
    {
        DetenerCronometro();
        _relojPaso = Stopwatch.StartNew();
        SegundosPaso = 0;
        _cronometro = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _cronometro.Tick += (_, _) =>
        {
            if (_disposed || _relojPaso is null) return;
            int antes = SegundosPaso;
            SegundosPaso = (int)_relojPaso.Elapsed.TotalSeconds;
            // Una sola vez, en el umbral: si el usuario tarda más, el log no tiene
            // que llenarse de líneas repetidas. Con el módulo colgado, esto es lo
            // que después permite decir dónde se detuvo la corrida.
            if (antes < SegundosLentos && SegundosPaso >= SegundosLentos)
                AppLog.Write($"{Titulo}: el módulo «{AvadeModulo}» lleva {SegundosPaso} s.", "WARN");
        };
        _cronometro.Start();
    }

    private void DetenerCronometro()
    {
        _cronometro?.Stop();
        _cronometro = null;
        _relojPaso = null;
    }

    /// <summary>Umbral a partir del cual se anota en el registro que un paso tarda.</summary>
    private const int SegundosLentos = 90;

    /// <summary>
    /// Rótulo listo para pintar. Se arma acá y no con un MultiBinding +
    /// conversor en el XAML porque la frase tiene una coma, un «de» y un vacío
    /// cuando no hay corrida: tres reglas de formato no son un conversor, son un
    /// conversor con estados.
    /// </summary>
    public string AvadeTexto => _avadeTotal switch
    {
        0 => "",
        // Con un solo paso no hay fracción que informar: decir «0 de 1» es
        // describir un número que no significa nada. Ahí lo útil es el nombre.
        1 => $"midiendo: {NombreConTiempo(AvadeModulo)}",
        _ => $"{_avadeHechos} de {_avadeTotal} · {NombreConTiempo(AvadeModulo)}"
    };

    private string NombreConTiempo(string modulo) => _segundosPaso switch
    {
        < 2 => modulo,
        < SegundosLentos => $"{modulo} · {_segundosPaso} s",
        _ => $"{modulo} · {_segundosPaso} s (lento)"
    };

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
        ? "Pulsa «Diagnóstico completo» para recopilar red, rendimiento, térmicas, almacenamiento y estabilidad en una sola pasada, o elige un módulo del panel izquierdo para medir una sola parte."
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
            OnPropertyChanged(nameof(FilasTablaFiltradas));
            OnPropertyChanged(nameof(ConteoTabla));
            OnPropertyChanged(nameof(SinCoincidencias));
            OnPropertyChanged(nameof(MostrarAyudaDrivers));
            OnPropertyChanged(nameof(MostrarAccionesDrivers));
            OnPropertyChanged(nameof(AvisoDrivers));
            OnPropertyChanged(nameof(TextoAvisoDrivers));
            OnPropertyChanged(nameof(MostrarAyudaUpdates));
            OnPropertyChanged(nameof(MostrarAccionesTabla));
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

    /// <summary>
    /// Alguna de las tres barras de acciones de la vista Datos está visible.
    /// La vista lo necesita para no dejar un panel vacío con marco cuando la
    /// tabla elegida es de solo lectura (Equipo, Discos, Temporales…).
    /// </summary>
    public bool MostrarAccionesTabla => MostrarAyudaDrivers || MostrarAccionesDrivers || MostrarAyudaUpdates;

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

    private DonutChart _graficoDiscos = new();
    /// <summary>
    /// Composición del espacio ocupado por unidad. Las tarjetas dicen cuánto
    /// queda libre; este gráfico dice de qué está lleno, que es la otra mitad
    /// de la pregunta cuando falta espacio.
    /// </summary>
    public DonutChart GraficoDiscos { get => _graficoDiscos; private set => Set(ref _graficoDiscos, value); }

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

    /// <summary>Ruta real donde se guardan informes y registros: la del pie, sin letra chica inventada.</summary>
    public string RutaSalidaTexto => "Los informes y registros se guardan en " + AppEnv.OutputPath;

    // ---- Contexto de sección ----------------------------------------------

    private ContextoModulo _contexto;
    /// <summary>Identidad y acciones de la sección activa.</summary>
    public ContextoModulo Contexto
    {
        get => _contexto;
        private set
        {
            Set(ref _contexto, value);
            OnPropertyChanged(nameof(ModuloAcento));
            OnPropertyChanged(nameof(ModuloLavado));
        }
    }

    public Brush ModuloAcento => Res(Contexto?.ColorClave ?? "BAccent");

    public Brush ModuloLavado
    {
        get
        {
            var c = ((SolidColorBrush)ModuloAcento).Color;
            var wash = new SolidColorBrush(Color.FromArgb(38, c.R, c.G, c.B));
            wash.Freeze();
            return wash;
        }
    }

    /// <summary>Cambia la identidad de la sección (nombre, icono, color y acciones).</summary>
    public void SetContexto(string clave)
    {
        if (clave != null && Contextos.TryGetValue(clave, out var ctx) && Contexto != ctx) Contexto = ctx;
    }

    // ---- Licencia ----------------------------------------------------------

    private string _licenciaEtiqueta = Core.Licensing.LicenseService.EtiquetaCorta;
    public string LicenciaEtiqueta { get => _licenciaEtiqueta; private set => Set(ref _licenciaEtiqueta, value); }

    private void RefrescarLicencia() => LicenciaEtiqueta = Core.Licensing.LicenseService.EtiquetaCorta;

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
        SetContexto(_moduloActivo);
        // El conteo se fija antes de hacer visible el pie y antes del primer
        // await: en el orden contrario hay un cuadro en el que la barra ya está
        // en pantalla con 0 de 0, y ese cuadro se lee como «no arrancó».
        AvadeTotal = pasos.Length;
        AvadeHechos = 0;
        AvadeIndeterminado = pasos.Length < 2;
        AvadeModulo = NombreModulo(pasos[0].Clave);
        IniciarCronometro();
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
            // El conteo se lleva acá y no dentro de ScanService a propósito: el
            // runner compartido es el mismo del modo sin interfaz, que no tiene a
            // quién informarle. Envolver el trabajo de cada paso en el envoltorio
            // que la UI ya estaba construyendo deja el avance en la capa que lo
            // consume y no cambia ninguna firma.
            var services = pasos.Select(p => (IDiagnosticService)new DelegateDiagnosticService(p.Clave,
                async (rep, tok) =>
                {
                    AvadeModulo = NombreModulo(p.Clave);
                    await p.Trabajo(rep, tok);
                    AvadeHechos++;
                })).ToArray();
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
            DetenerCronometro();
            _cts.Dispose();
            _cts = null;
            Ocupado = false;
            // Se deja el último 5/5 en su sitio y la barra vuelve al modo
            // indeterminado. NO se pone el total a 0: un ProgressBar calcula su
            // relleno dividiendo por (Maximum - Minimum), y con 0/0 eso es un
            // ancho NaN en la próxima pasada de layout —que sí ocurre, porque
            // Oculto se mide y se ordena igual. La limpieza del conteo sucede al
            // arrancar la corrida siguiente, que es cuando importa.
            AvadeIndeterminado = true;
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
        // El anillo de disco se limpia primero: a diferencia de los otros tres,
        // solo se arma si hay volúmenes con datos, y sin este reinicio un
        // diagnóstico que no trajo discos dejaría en pantalla el anillo de la
        // corrida anterior, que ya no describe este equipo.
        GraficoDiscos = new DonutChart();

        // --- Latencia por destino: la comparación es el punto, no el número ---
        // El umbral de 70 ms es el mismo que usa la regla de severidad de red:
        // la línea en el gráfico y el color de la barra cuentan lo mismo, así
        // no hay que aprender dos criterios para leer una sola medición.
        GraficoRed = BarChart.Crear("Latencia por destino",
            "Cuanto más larga la barra, más tarda la respuesta. Comparar el router con los demás separa un problema de tu red de uno del proveedor.",
            Report.Red
                .Where(x => x.Media > 0)
                .Select(x => (x.Destino, x.Media, $"{x.Media} ms", Pincel(x.Estado),
                              $"jitter {x.Jitter} ms · pérdida {x.PerdidaPct} %")),
            unidad: "ms", marca: 70, marcaTexto: "70 ms");

        // --- Procesos por CPU ---
        GraficoProcesos = BarChart.Crear("Procesos que más CPU consumen",
            "Medido como diferencia real durante el muestreo, no como tiempo acumulado desde que arrancó cada proceso.",
            Report.TopCpu
                .Where(x => x.CpuPct > 0)
                .Take(6)
                .Select(x => (x.Proceso, x.CpuPct, $"{x.CpuPct} %",
                              Pincel(x.CpuPct > 40 ? Severity.Warn : Severity.Ok),
                              $"{x.RamMb} MB de memoria")),
            unidad: "%", escalaMax: 100, marca: 40, marcaTexto: "40 %");

        // --- Eventos críticos por tipo ---
        // Escala logarítmica: cuando un tipo se repite 40 000 veces y los
        // demás 3, una escala lineal dibuja una sola barra y cinco ceros. Con
        // logaritmo se comparan órdenes de magnitud, que es lo que importa
        // acá («esto se repite miles de veces» versus «esto pasó dos veces»).
        // Los rótulos se escriben a mano porque la barra mide log10(n) y no n:
        // dejar que se calculen solos pondría «media 2,4» donde va «40 000».
        var eventos = Report.EventosResumen.Take(6).ToList();
        GraficoEventos = BarChart.Crear("Eventos críticos por tipo",
            $"Repeticiones en los últimos {StabilityModule.EventDays} días, en escala logarítmica: cada guía multiplica por diez. " +
            "Un tipo que se repite miles de veces señala un problema persistente, no un incidente aislado.",
            eventos.Select(x => ($"ID {x.Id}", Math.Log10(Math.Max(x.Ocurrencias, 1)),
                              x.Ocurrencias.ToString("N0"),
                              Pincel(x.Ocurrencias > 100 ? Severity.Bad : Severity.Warn),
                              x.Descripcion)),
            escalaTexto: "escala logarítmica · cada guía ×10",
            resumenTexto: eventos.Count == 0 ? "" :
                $"{eventos.Count} tipos · {eventos.Sum(x => x.Ocurrencias):N0} repeticiones en total");

        // --- Composición del disco: cuánto ocupa cada volumen ---
        // Es un anillo y no barras porque la pregunta no es «cuál es más
        // grande» sino «de qué está hecho el total»: se lee como una torta.
        var discos = Report.Discos
            .Where(d => NumericText.TryRead(d.Tamano, out _) && Leer(d.Libre) >= 0)
            .ToList();

        if (discos.Count > 0)
        {
            double totalGb = 0;
            var porciones = new List<(string, double, string, Brush, string)>();
            foreach (var d in discos)
            {
                double usadoGb = Math.Max(Leer(d.Tamano) - Leer(d.Libre), 0);
                if (usadoGb <= 0) continue;
                totalGb += usadoGb;
                porciones.Add(($"{d.Unidad} {d.Etiqueta}".Trim(), usadoGb, d.Libre,
                    Pincel(d.LibrePct < 10 ? Severity.Bad : d.LibrePct < 20 ? Severity.Warn : Severity.Ok),
                    $"{d.Unidad} · {d.Libre} libres de {d.Tamano} · tipo {d.Tipo}"));
            }

            if (porciones.Count > 0)
            {
                GraficoDiscos = DonutChart.Crear("Espacio en disco por unidad",
                    "Cada porción es espacio ocupado; el centro dice cuánto queda libre en total. " +
                    "Con menos del 10 % libre en la unidad del sistema, Windows empieza a fallar al actualizar.",
                    porciones,
                    centro1: AppEnv.FormatBytes(totalGb * 1024 * 1024 * 1024),
                    centro2: "en uso");
            }
        }

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
        OnPropertyChanged(nameof(SinHallazgos));
    }

    /// <summary>
    /// La vista de hallazgos avisa cuando la lista está vacía en vez de
    /// quedarse en blanco: si no, «sin hallazgos» y «todavía no medí nada»
    /// se ven exactamente igual.
    /// </summary>
    public bool SinHallazgos => Hallazgos.Count == 0;

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

        OnPropertyChanged(nameof(SinTablas));
    }

    /// <summary>
    /// La vista Datos se puede abrir antes de medir nada. Sin tablas, el
    /// selector vacío y la rejilla en blanco parecen un fallo; este estado
    /// lo distingue de «medí y no salió nada».
    /// </summary>
    public bool SinTablas => Tablas.Count == 0;

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

    // ---- Filtros de las vistas -------------------------------------------
    //
    // Una tabla de 300 servicios o un registro de 1500 líneas se vuelven
    // inútiles sin forma de acotarlos: el dato está, pero hay que encontrarlo
    // a ojo. Los tres filtros viven en el ViewModel y no en la vista porque
    // el criterio —qué es un «aviso», qué columnas se buscan— es la misma
    // regla que usa el informe HTML, y duplicarla en code-behind la dejaría
    // expuesta a divergir.

    /// <summary>Filtro de hallazgos: 0 todos, 1 críticos, 2 avisos, 3 correctos.</summary>
    private int _filtroHallazgos;
    public int FiltroHallazgos
    {
        get => _filtroHallazgos;
        set
        {
            Set(ref _filtroHallazgos, value);
            OnPropertyChanged(nameof(HallazgosFiltrados));
            OnPropertyChanged(nameof(SinHallazgosPorFiltro));

            // Si lo que estaba seleccionado queda fuera del filtro, se pasa al
            // primero visible: el panel de la derecha seguiría mostrando la
            // recomendación de un hallazgo que ya no está en la lista, y eso
            // se lee como un error de la aplicación.
            var visibles = HallazgosFiltrados.ToList();
            if (HallazgoSeleccionado != null && !visibles.Contains(HallazgoSeleccionado))
                HallazgoSeleccionado = visibles.FirstOrDefault();
        }
    }

    public IEnumerable<Finding> HallazgosFiltrados => _filtroHallazgos switch
    {
        1 => Hallazgos.Where(f => f.Severity == Severity.Bad),
        2 => Hallazgos.Where(f => f.Severity == Severity.Warn),
        3 => Hallazgos.Where(f => f.Severity == Severity.Ok),
        _ => Hallazgos
    };

    /// <summary>
    /// Hay hallazgos, pero ninguno del tipo filtrado. Es un estado distinto de
    /// «sin hallazgos»: el primero es una buena noticia disfrazada de lista
    /// vacía, y merece su propio texto.
    /// </summary>
    public bool SinHallazgosPorFiltro => Hallazgos.Count > 0 && !HallazgosFiltrados.Any();

    public string ContadorHallazgos =>
        $"{Hallazgos.Count(f => f.Severity == Severity.Bad)} críticos · " +
        $"{Hallazgos.Count(f => f.Severity == Severity.Warn)} avisos · " +
        $"{Hallazgos.Count(f => f.Severity == Severity.Ok)} correctos";

    private string _busquedaTabla = "";
    /// <summary>Texto que acota las filas de la tabla activa en la vista Datos.</summary>
    public string BusquedaTabla
    {
        get => _busquedaTabla;
        set
        {
            Set(ref _busquedaTabla, value ?? "");
            OnPropertyChanged(nameof(FilasTablaFiltradas));
            OnPropertyChanged(nameof(ConteoTabla));
            OnPropertyChanged(nameof(SinCoincidencias));
        }
    }

    private static readonly Dictionary<Type, PropertyInfo[]> CacheColumnas = new();

    /// <summary>
    /// Las filas de la tabla cuando hay una búsqueda activa. Se devuelve como
    /// <c>IList</c> de objetos: el DataGrid genera sus columnas a partir del
    /// tipo del primer elemento, así que la lista se construye conservando los
    /// objetos originales y no una copia en texto.
    /// </summary>
    public IList FilasTablaFiltradas
    {
        get
        {
            var filas = FilasTabla;
            if (filas == null) return null;

            string consulta = (_busquedaTabla ?? "").Trim();
            if (consulta.Length == 0) return filas;

            var visibles = new List<object>();
            foreach (object fila in filas)
                if (Coincide(fila, consulta)) visibles.Add(fila);
            return visibles;
        }
    }

    /// <summary>
    /// La tabla tiene filas, pero ninguna coincide con la búsqueda. Se
    /// distingue de <see cref="SinTablas"/> porque la causa es distinta y el
    /// texto que se le debe dar al usuario también.
    /// </summary>
    public bool SinCoincidencias
    {
        get
        {
            var filas = FilasTabla;
            if (filas == null || filas.Count == 0) return false;
            return (_busquedaTabla ?? "").Trim().Length > 0 && (FilasTablaFiltradas?.Count ?? 0) == 0;
        }
    }

    /// <summary>«312 filas» o «7 de 312 filas»: el filtro nunca deja a ciegas.</summary>
    public string ConteoTabla
    {
        get
        {
            var filas = FilasTabla;
            if (filas == null) return "";
            int total = filas.Count;
            int visibles = FilasTablaFiltradas?.Count ?? 0;
            return visibles == total ? $"{total} filas" : $"{visibles} de {total} filas";
        }
    }

    /// <summary>
    /// Busca el texto en todas las columnas visibles de la fila. Las marcadas
    /// <c>[Browsable(false)]</c> se omiten: son datos internos (enumeraciones
    /// de severidad, identificadores) que el usuario no ve en la rejilla, y
    /// encontrarlos por accidente daría resultados que no se explican solos.
    /// </summary>
    private static bool Coincide(object fila, string consulta)
    {
        if (fila == null) return false;

        Type tipo = fila.GetType();
        if (!CacheColumnas.TryGetValue(tipo, out var propiedades))
        {
            propiedades = tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                .Where(p => p.GetCustomAttribute<BrowsableAttribute>()?.Browsable != false)
                .ToArray();
            CacheColumnas[tipo] = propiedades;
        }

        foreach (var propiedad in propiedades)
        {
            object valor;
            try { valor = propiedad.GetValue(fila); }
            catch (Exception ex) when (ex is TargetInvocationException or TargetParameterCountException) { continue; }

            if (valor == null) continue;
            if (valor.ToString()?.IndexOf(consulta, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }

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
            _pendientes.Add(new LogLine { Texto = linea, Color = Res(clave), Nivel = nivel });

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

        // Con un filtro puesto, la vista muestra una colección distinta de la
        // que se acaba de llenar; sin este aviso la lista filtrada se queda
        // congelada en lo que había antes de empezar a escribir.
        OnPropertyChanged(nameof(RegistroFiltrado));
        OnPropertyChanged(nameof(ResumenRegistro));

        LineaAgregada?.Invoke();
    }

    /// <summary>Avisa a la vista que hay líneas nuevas, para seguir el final.</summary>
    public event Action LineaAgregada;

    /// <summary>Nivel del registro en pantalla: «todo», «avisos» o «errores».</summary>
    private string _filtroRegistro = "todo";
    public string FiltroRegistro
    {
        get => _filtroRegistro;
        set
        {
            Set(ref _filtroRegistro, string.IsNullOrWhiteSpace(value) ? "todo" : value);
            OnPropertyChanged(nameof(RegistroFiltrado));
        }
    }

    private string _busquedaRegistro = "";
    /// <summary>Texto libre sobre el registro. Busca en el renglón completo, incluida la hora.</summary>
    public string BusquedaRegistro
    {
        get => _busquedaRegistro;
        set
        {
            Set(ref _busquedaRegistro, value ?? "");
            OnPropertyChanged(nameof(RegistroFiltrado));
        }
    }

    public IEnumerable<LogLine> RegistroFiltrado
    {
        get
        {
            IEnumerable<LogLine> lineas = Registro;

            if (string.Equals(_filtroRegistro, "avisos", StringComparison.OrdinalIgnoreCase))
                lineas = lineas.Where(l => l.Nivel is "WARN" or "ERROR");
            else if (string.Equals(_filtroRegistro, "errores", StringComparison.OrdinalIgnoreCase))
                lineas = lineas.Where(l => l.Nivel == "ERROR");

            string consulta = (_busquedaRegistro ?? "").Trim();
            if (consulta.Length > 0)
                lineas = lineas.Where(l => l.Texto != null
                    && l.Texto.IndexOf(consulta, StringComparison.OrdinalIgnoreCase) >= 0);

            return lineas;
        }
    }

    /// <summary>Cuánto hay y cuánto importa: es lo que decide si vale la pena filtrar.</summary>
    public string ResumenRegistro =>
        $"{Registro.Count} líneas · {Registro.Count(l => l.Nivel == "WARN")} avisos · " +
        $"{Registro.Count(l => l.Nivel == "ERROR")} errores";

    /// <summary>Registro filtrado como texto plano, para el portapapeles y para pegarlo en un informe.</summary>
    public string TextoRegistroFiltrado =>
        string.Join(Environment.NewLine, RegistroFiltrado.Select(l => l.Texto));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DetenerCronometro();
        AppLog.Line -= OnLog;
        Core.Licensing.LicenseService.EstadoCambiado -= RefrescarLicencia;
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

    /// <summary>Nombre legible del módulo para el rótulo de avance.</summary>
    private static string NombreModulo(string clave) =>
        !string.IsNullOrEmpty(clave) && DiagnosticReport.NombresModulos.TryGetValue(clave, out string nombre)
            ? nombre
            : "equipo";

    private void OnPropertyChanged([CallerMemberName] string prop = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}
