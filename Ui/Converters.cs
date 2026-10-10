using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SysDiag.Models;

namespace SysDiag.Ui;

/// <summary>bool -> Visible/Collapsed.</summary>
public class BoolToVisibility : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

/// <summary>bool invertido -> Visible/Collapsed. Para estados vacíos.</summary>
public class BoolToVisibilityInverse : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Collapsed;
}

/// <summary>Severity -> fondo tenue del mismo tono, para los distintivos.</summary>
public class SeverityToWash : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var baseBrush = new SeverityToBrush().Convert(value, targetType, parameter, culture) as SolidColorBrush;
        if (baseBrush == null) return Brushes.Transparent;

        var c = baseBrush.Color;
        // Mismo tono, muy bajo alfa: el distintivo se lee como pertenencia al
        // color de severidad sin competir con el texto.
        var wash = new SolidColorBrush(Color.FromArgb(38, c.R, c.G, c.B));
        wash.Freeze();
        return wash;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Severity -> pincel de la paleta, resuelto desde los recursos.</summary>
public class SeverityToBrush : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string clave = value switch
        {
            Severity.Bad => "BBad",
            Severity.Warn => "BWarn",
            Severity.Ok => "BOk",
            _ => "BTextDim"
        };
        return Application.Current?.Resources[clave] as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Cadena con texto -> Visible; cadena vacía o nula -> Collapsed. Sirve para
/// los rótulos opcionales de una tarjeta: si el dato no viene, el espacio que
/// ocuparía el rótulo desaparece en vez de quedar como un hueco.
/// </summary>
public class NotEmptyToVisibility : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Clave interna de módulo -> nombre legible. Las tarjetas del resumen saben
/// de qué módulo salió cada medición; la clave («termicas») no se muestra tal
/// cual porque es un identificador, no una etiqueta.
/// </summary>
public class ModuleLabelConverter : IValueConverter
{
    private static readonly Dictionary<string, string> Nombres = new(StringComparer.OrdinalIgnoreCase)
    {
        ["red"] = "Red",
        ["rendimiento"] = "Rendimiento",
        ["termicas"] = "Térmicas",
        ["seguridad"] = "Seguridad",
        ["estabilidad"] = "Estabilidad",
        ["almacenamiento"] = "Disco",
        ["arranque"] = "Arranque",
        ["drivers"] = "Drivers",
        ["actualizaciones"] = "Actualizaciones",
        ["limpieza"] = "Limpieza"
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string clave && Nombres.TryGetValue(clave, out string nombre) ? nombre : value as string ?? "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Matemática del anillo de puntaje: 0-100 -> geometría de un arco circular.
///
/// Vive acá y no dentro del convertidor porque la usan dos caminos: el
/// convertidor, que pinta el estado quieto, y <see cref="Motion"/>, que
/// interpola el barrido cuadro a cuadro. Dos copias de la trigonometría
/// garantizan un anillo desalineado en cuanto una de las dos cambie de radio.
/// </summary>
public static class ScoreArc
{
    // Centro y radio fijos: el anillo siempre se dibuja en un lienzo de
    // 120x120, así que la vista solo necesita reservar ese espacio.
    private const double Cx = 60, Cy = 60, R = 50;

    public static Geometry Geometria(double puntaje)
    {
        puntaje = Math.Clamp(puntaje, 0, 100);

        // Un barrido de exactamente 360° deja el punto de inicio y de fin
        // superpuestos, y WPF no puede trazar un arco así: se limita a
        // 359.9°, que visualmente se lee como un círculo completo.
        double angulo = puntaje / 100.0 * 359.9;
        if (angulo <= 0.5) return Geometry.Empty;

        double rad0 = -90 * Math.PI / 180;
        double rad1 = (angulo - 90) * Math.PI / 180;

        var inicio = new Point(Cx + R * Math.Cos(rad0), Cy + R * Math.Sin(rad0));
        var fin = new Point(Cx + R * Math.Cos(rad1), Cy + R * Math.Sin(rad1));

        var figura = new PathFigure { StartPoint = inicio, IsClosed = false };
        figura.Segments.Add(new ArcSegment(
            fin, new Size(R, R), 0, angulo > 180, SweepDirection.Clockwise, isStroked: true));

        var geometria = new PathGeometry();
        geometria.Figures.Add(figura);
        // Se recrea 60 veces por segundo mientras el arco barre; congelarla
        // evita que el render la copie en cada cuadro.
        geometria.Freeze();
        return geometria;
    }
}

/// <summary>
/// Puntaje 0-100 -> geometría del anillo. Envoltorio de <see cref="ScoreArc"/>
/// para el camino sin animación (enlaces directos y estados quietos).
/// </summary>
public class ScoreArcConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => ScoreArc.Geometria(value switch
        {
            int i => i,
            double d => d,
            _ => 0
        });

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
