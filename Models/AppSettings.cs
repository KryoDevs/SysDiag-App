namespace SysDiag.Models;

/// <summary>
/// Parámetros ajustables tal como se guardan en settings.json. Los módulos no los leen:
/// <see cref="OpcionesDiagnostico"/> es la copia inmutable que recibe cada corrida.
/// Esta clase es la capa de carga/guardado y el lugar de los valores por defecto.
/// </summary>
public class AppSettings
{
    /// <summary>Segundos de muestreo para medir CPU real por proceso.</summary>
    public int SampleSeconds { get; set; } = 5;

    /// <summary>Cuántos pings por destino al medir latencia.</summary>
    public int PingCount { get; set; } = 20;

    /// <summary>Ventana de días para el registro de eventos críticos general.</summary>
    public int EventDays { get; set; } = 30;

    /// <summary>Ventana de días para el escaneo dedicado de errores WHEA.</summary>
    public int WheaDays { get; set; } = 15;

    /// <summary>Cuántos diagnósticos históricos conservar antes de descartar los más viejos.</summary>
    public int HistorialMaximo { get; set; } = 60;

    /// <summary>Cuántos archivos de registro conservar en logs/.</summary>
    public int LogsMaximo { get; set; } = 30;

    // ---- Estado de la ventana principal (MEJORAS §8.2, ítem 15) ------------
    // Se guarda para que la aplicación abra donde se dejó. Son anulables y no
    // centinelas numéricos por dos razones: un 0 real es una posición legítima
    // (borde izquierdo del monitor) y no se distinguiría de un campo ausente, y
    // un NaN guardado no es JSON válido, así que escribir los ajustes fallaría.

    public double? VentanaX { get; set; }
    public double? VentanaY { get; set; }
    public double? VentanaAncho { get; set; }
    public double? VentanaAlto { get; set; }
    public bool VentanaMaximizada { get; set; }

    /// <summary>Vista abierta al cerrar: Resumen, Hallazgos, Datos o Registro.</summary>
    public string UltimaVista { get; set; } = "";

    /// <summary>Filtro del registro al cerrar: todo, avisos o errores.</summary>
    public string UltimoFiltroRegistro { get; set; } = "";

    /// <summary>
    /// Valores válidos del estado de la ventana. Viven acá y no en la ventana
    /// porque es esta clase la que acepta o descarta lo que venga de
    /// settings.json: un valor escrito a mano, o dejado por una versión
    /// anterior, no puede hacer fallar la carga.
    /// </summary>
    public static readonly string[] VistasValidas = { "Resumen", "Hallazgos", "Datos", "Registro" };

    /// <summary>Filtros del registro, tal como viajan en el Tag del XAML.</summary>
    public static readonly string[] FiltrosValidos = { "todo", "avisos", "errores" };

    public static AppSettings PorDefecto() => new();

    /// <summary>También valida JSON cargado o llamadas directas, no solo los campos de la UI.</summary>
    public AppSettings Normalizar()
    {
        var normalizado = new AppSettings
        {
            SampleSeconds = Math.Clamp(SampleSeconds, 2, 30),
            PingCount = Math.Clamp(PingCount, 5, 100),
            EventDays = Math.Clamp(EventDays, 1, 90),
            WheaDays = Math.Clamp(WheaDays, 1, 90),
            HistorialMaximo = Math.Clamp(HistorialMaximo, 5, 500),
            LogsMaximo = Math.Clamp(LogsMaximo, 5, 200),
            VentanaMaximizada = VentanaMaximizada,
            UltimaVista = Array.IndexOf(VistasValidas, UltimaVista) >= 0 ? UltimaVista : "",
            UltimoFiltroRegistro = Array.IndexOf(FiltrosValidos, UltimoFiltroRegistro) >= 0 ? UltimoFiltroRegistro : ""
        };

        // Una posición a medias (o con coordenadas imposibles) no se conserva:
        // es preferible que la ventana aparezca centrada a que aparezca fuera
        // del escritorio. Tampoco se conserva una posición sin tamaño, que es
        // lo que deja un field a medio escribir a mano.
        if (VentanaX is double x && VentanaY is double y && VentanaAncho is double ancho && VentanaAlto is double alto
            && double.IsFinite(x) && double.IsFinite(y)
            && double.IsFinite(ancho) && double.IsFinite(alto)
            && ancho > 0 && alto > 0)
        {
            normalizado.VentanaX = x;
            normalizado.VentanaY = y;
            normalizado.VentanaAncho = ancho;
            normalizado.VentanaAlto = alto;
        }

        return normalizado;
    }

    /// <summary>
    /// Interpreta el texto de un campo de Ajustes: si no es un número válido
    /// usa el valor por defecto, y si está fuera de rango lo acota. Vive acá
    /// (no en el code-behind de la ventana) para poder probarlo sin abrir
    /// ninguna ventana.
    /// </summary>
    public static int LeerCampo(string texto, int minimo, int maximo, int porDefecto)
        => int.TryParse(texto, out int v) ? Math.Clamp(v, minimo, maximo) : porDefecto;
}
