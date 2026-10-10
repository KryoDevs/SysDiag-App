using SysDiag.Ui;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// La regla de recorte de ventanas, probada como lo que es: aritmética sobre
/// dobles. <see cref="Ventana.AjustarAPantalla"/> necesita una ventana real y un
/// monitor, así que su parte interesante —qué se recorta, qué no, y quién gana
/// cuando no cabe— vive separada en <see cref="Ventana.Recortar"/>.
///
/// Los números de los casos son los de la pantalla que motivó el arreglo: un
/// panel de 1366×768 deja 728 px de alto útil, de los que se descuentan 24 de
/// margen. Con esos valores, <c>MainWindow</c> (alto 860) pasaba a 704 y el pie
/// con «Generar informe» quedaba fuera del área de trabajo, sin borde de
/// arrastre al que alcanzar.
///
/// Dos expectativas de estas las escribió mal su autor y el CI las rechazó: la
/// primera versión asumía que un mínimo de 900 «empuja» hacia arriba un alto
/// declarado de 860, y que un máximo de 660 se estira hasta los 704 disponibles.
/// Ninguna de las dos cosas hace la función, y ninguna debería hacerla:
/// <c>Recortar</c> solo achica. Los dos casos se quedan, con la expectativa
/// correcta, porque son justamente la frontera del contrato.
/// </summary>
public class WindowSizingTests
{
    [Theory]
    [InlineData(860.0, 640.0, 704.0, 704.0)]   // MainWindow en 1366x768: se recorta y el pie vuelve a entrar
    [InlineData(660.0, 440.0, 704.0, 660.0)]   // lo que cabe se queda como lo escribió el XAML
    [InlineData(1000.0, 900.0, 704.0, 900.0)]  // el mínimo declarado le gana al área: se recorta a 900, no a 704
    [InlineData(860.0, 900.0, 704.0, 860.0)]   // y nadie sube nada: 860 con piso 900 se queda en 860
    [InlineData(704.0, 0.0, 704.0, 704.0)]     // en el límite exacto no se toca nada (la comparación es ">", no ">=")
    public void Ventana_RecortaAlAreaDisponible(double declarado, double piso, double disponible, double esperado) =>
        Assert.Equal(esperado, Ventana.Recortar(declarado, piso, disponible), 3);

    /// <summary>
    /// NaN significa «la ventana no fijó ese tamaño». Recortarlo sería inventarse
    /// un valor: en particular, convertir el alto de una ventana con
    /// <c>SizeToContent</c> en un número fijo le quita el autoajuste.
    /// </summary>
    [Fact]
    public void Ventana_NoTocaLoQueNoFueDeclarado()
    {
        Assert.True(double.IsNaN(Ventana.Recortar(double.NaN, 0, 704)));
        Assert.True(double.IsNaN(Ventana.Recortar(double.NaN, 640, 704)));
    }

    /// <summary>
    /// El defecto que apareció al separar la función para poder probarla: el valor
    /// por defecto de <c>MaxHeight</c> y <c>MaxWidth</c> es infinito positivo, no
    /// <c>NaN</c>. Comprobar solo el <c>NaN</c> recortaba el máximo de las nueve
    /// ventanas que no declaran ninguno, quitándoles de hecho el máximo.
    /// </summary>
    [Fact]
    public void Ventana_NoRecortaUnMaximoNoDeclarado()
    {
        Assert.True(double.IsPositiveInfinity(Ventana.Recortar(double.PositiveInfinity, 0, 704)));
        Assert.True(double.IsPositiveInfinity(Ventana.Recortar(double.PositiveInfinity, 640, 704)));
    }

    /// <summary>
    /// El piso se expresa como mínimo declarado de la ventana; si la ventana no lo
    /// declaró, el piso es 0 y el recorte manda entero. Es el caso de las ventanas
    /// de <c>PanelShell</c>, que solo ponen <c>MaxHeight</c>. Un máximo declarado no
    /// se sube hasta el área disponible: significa «no más de esto», y estirarlo
    /// sería cambiarle el diseño a la ventana.
    /// </summary>
    [Theory]
    [InlineData(820.0, 704.0)]   // TweaksWindow: MaxHeight 820 sobre un área de 704, se recorta
    [InlineData(660.0, 660.0)]   // Dialog: 660 cabe y no se toca
    [InlineData(704.0, 704.0)]   // en el límite exacto, tampoco
    public void Ventana_RecortaElMaxHeightSinMinimoDeclarado(double max, double esperado) =>
        Assert.Equal(esperado, Ventana.Recortar(max, 0, 704), 3);

    /// <summary>
    /// Un área de trabajo inválida no puede dejar a la ventana en 0 px. Lo que lo
    /// impide es la salida temprana de <c>AjustarAPantalla</c>, no el recorte: esta
    /// prueba fija esa responsabilidad para que nadie "arregle" el 0 moviendo la
    /// guarda a <c>Recortar</c> y deja a medias las ventanas reales.
    /// </summary>
    [Fact]
    public void Ventana_EsLaVentanaQuienFiltraElAreaInvalida()
    {
        Assert.Equal(0.0, Ventana.Recortar(860, 0, 0), 3);
        Assert.Equal(640.0, Ventana.Recortar(860, 640, 0), 3);
    }
}
