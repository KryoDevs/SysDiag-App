using System;
using System.Linq;
using System.Windows;
using SysDiag.Models;

namespace SysDiag.Ui;

/// <summary>
/// Estado de la interfaz que sobrevive al cierre: tamaño, posición, si estaba
/// maximizada, qué vista estaba abierta y qué filtro del registro.
///
/// Es de las cosas que nadie pide y todos notan cuando faltan (MEJORAS §8.2,
/// ítem 15): abrir la aplicación y encontrarla donde se dejó.
///
/// Dos decisiones:
///
/// - **Una posición guardada se valida contra la pantalla virtual entera**, que
///   es la unión de todos los monitores. Una ventana guardada en un monitor
///   secundario se abre ahí, y una guardada en un monitor que ya no está se
///   recentra en vez de aparecer fuera del escritorio, que no tiene salida
///   visible para el usuario.
/// - **Lo que se guarda es el tamaño de reposo**, no el de la ventana
///   maximizada: si se guardara el de la maximizada, «restaurar» la dejaría
///   tan grande como el monitor y el botón no tendría efecto útil.
///
/// La regla de ubicación vive separada de la ventana para poder probarla sin
/// abrir ninguna (igual que <see cref="Ventana.Recortar"/>).
/// </summary>
public sealed class EstadoVentana
{
    /// <summary>
    /// Cuántos píxeles de la ventana tienen que quedar dentro del escritorio
    /// para considerarla recuperable. Menos que esto no alcanza para agarrarla
    /// con el mouse y arrastrarla de vuelta.
    /// </summary>
    public const double VisibleMinimo = 120;

    /// <summary>Las cuatro vistas del segmento superior de la ventana principal.</summary>
    public static string[] Vistas => AppSettings.VistasValidas;

    /// <summary>Los tres filtros del registro, tal como viajan en el Tag del XAML.</summary>
    public static string[] Filtros => AppSettings.FiltrosValidos;

    public bool HayPosicion { get; init; }
    public double Ancho { get; init; }
    public double Alto { get; init; }
    public double X { get; init; }
    public double Y { get; init; }

    public bool Maximizada { get; init; }
    public string Vista { get; init; } = "";
    public string Filtro { get; init; } = "";

    /// <summary>
    /// Lee el estado guardado. Nunca falla por un archivo raro: un valor que no
    /// sirve se descarta y la ventana nace como la primera vez. Una posición a
    /// medias (sin las cuatro coordenadas) tampoco se usa.
    /// </summary>
    public static EstadoVentana Desde(AppSettings ajustes)
    {
        bool hayPosicion = ajustes?.VentanaX is double x && ajustes.VentanaY is double y
            && ajustes.VentanaAncho is double ancho && ajustes.VentanaAlto is double alto
            && double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(ancho) && double.IsFinite(alto)
            && ancho > 0 && alto > 0;

        return new EstadoVentana
        {
            HayPosicion = hayPosicion,
            Ancho = hayPosicion ? ajustes!.VentanaAncho!.Value : 0,
            Alto = hayPosicion ? ajustes!.VentanaAlto!.Value : 0,
            X = hayPosicion ? ajustes!.VentanaX!.Value : 0,
            Y = hayPosicion ? ajustes!.VentanaY!.Value : 0,
            Maximizada = ajustes?.VentanaMaximizada ?? false,
            Vista = Vistas.Contains(ajustes?.UltimaVista ?? "") ? ajustes!.UltimaVista : "",
            Filtro = Filtros.Contains(ajustes?.UltimoFiltroRegistro ?? "") ? ajustes!.UltimoFiltroRegistro : ""
        };
    }

    /// <summary>
    /// Dónde debe nacer la ventana, o <c>null</c> para dejarla como la declara
    /// el XAML. El tamaño se acota al mínimo de la ventana y al área de trabajo
    /// del monitor principal —el mismo tope que usa
    /// <see cref="Ventana.AjustarAPantalla"/> al abrir cada ventana—, y la
    /// posición solo se respeta si deja al menos <see cref="VisibleMinimo"/>
    /// píxeles dentro del escritorio virtual.
    /// </summary>
    /// <param name="estado">Lo guardado en settings.json.</param>
    /// <param name="minAncho">MinWidth de la ventana.</param>
    /// <param name="minAlto">MinHeight de la ventana.</param>
    /// <param name="areaTrabajo">Área de trabajo del monitor principal, sin la barra de tareas.</param>
    /// <param name="pantallaVirtual">Unión de todos los monitores del escritorio.</param>
    public static Rect? Ubicar(EstadoVentana estado, double minAncho, double minAlto, Rect areaTrabajo, Rect pantallaVirtual)
    {
        if (estado is not { HayPosicion: true }) return null;
        if (pantallaVirtual.IsEmpty || pantallaVirtual.Width <= 0 || pantallaVirtual.Height <= 0) return null;

        double ancho = Acotar(estado.Ancho, minAncho, areaTrabajo.Width);
        double alto = Acotar(estado.Alto, minAlto, areaTrabajo.Height);
        if (ancho <= 0 || alto <= 0) return null;

        var guardada = new Rect(estado.X, estado.Y, ancho, alto);
        var visible = Rect.Intersect(guardada, pantallaVirtual);
        if (visible.IsEmpty || visible.Width < VisibleMinimo || visible.Height < VisibleMinimo)
        {
            // Se recentra en el monitor principal: la posición guardada apunta a
            // una pantalla que ya no existe (o a un borde casi invisible).
            return new Rect(
                areaTrabajo.X + Math.Max(0, (areaTrabajo.Width - ancho) / 2),
                areaTrabajo.Y + Math.Max(0, (areaTrabajo.Height - alto) / 2),
                ancho, alto);
        }
        return guardada;
    }

    /// <summary>Un valor entre el mínimo de la ventana y el tope disponible, o 0 si no sirve.</summary>
    private static double Acotar(double valor, double minimo, double tope)
    {
        if (!double.IsFinite(valor) || valor <= 0) return 0;
        double piso = double.IsFinite(minimo) && minimo > 0 ? minimo : 0;
        double maximo = double.IsFinite(tope) && tope > 0 ? tope : valor;
        return Math.Min(Math.Max(valor, piso), Math.Max(piso, maximo));
    }

    /// <summary>El escritorio completo: la unión de todos los monitores conectados.</summary>
    public static Rect PantallaVirtual() => new(
        SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
        SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);

    /// <summary>
    /// Guarda el estado de <paramref name="ventana"/> en los ajustes recibidos.
    /// No escribe el archivo: eso lo decide quien llama, que es quien sabe si
    /// hay algo más que guardar.
    /// </summary>
    public static void Volcar(AppSettings ajustes, Window ventana, string vista, string filtro)
    {
        if (ajustes == null || ventana == null) return;

        // Con la ventana maximizada, Width/Height son los del monitor entero y
        // Left/Top los del maximizado; RestoreBounds es el tamaño al que vuelve
        // el botón «restaurar», que es el único que tiene sentido recordar.
        Rect caja = ventana.WindowState == WindowState.Normal
            ? new Rect(ventana.Left, ventana.Top, ventana.Width, ventana.Height)
            : ventana.RestoreBounds;

        VolcarRect(ajustes, caja, ventana.WindowState == WindowState.Maximized, vista, filtro);
    }

    /// <summary>
    /// La parte pura de <see cref="Volcar"/>: recibe el rectángulo ya resuelto
    /// para poder probarse sin abrir ninguna ventana. Una caja que no sirve no
    /// pisa una posición anterior buena.
    /// </summary>
    public static void VolcarRect(AppSettings ajustes, Rect caja, bool maximizada, string vista, string filtro)
    {
        if (ajustes == null) return;

        bool cajaValida = !caja.IsEmpty
            && double.IsFinite(caja.X) && double.IsFinite(caja.Y)
            && double.IsFinite(caja.Width) && double.IsFinite(caja.Height)
            && caja.Width > 0 && caja.Height > 0;
        if (cajaValida)
        {
            ajustes.VentanaX = caja.X;
            ajustes.VentanaY = caja.Y;
            ajustes.VentanaAncho = caja.Width;
            ajustes.VentanaAlto = caja.Height;
        }

        ajustes.VentanaMaximizada = maximizada;
        ajustes.UltimaVista = Vistas.Contains(vista ?? "") ? vista : "";
        ajustes.UltimoFiltroRegistro = Filtros.Contains(filtro ?? "") ? filtro : "";
    }
}
