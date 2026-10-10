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
/// </summary>
public class WindowSizingTests
{
    [Theory]
    [InlineData(860.0, 640.0, 704.0, 704.0)]   // MainWindow en 1366x768: se recorta y el pie vuelve a entrar
    [InlineData(660.0, 440.0, 704.0, 660.0)]   // lo que cabe se queda exactamente como lo escribió el XAML
    [InlineData(860.0, 900.0, 704.0, 900.0)]   // el mínimo declarado le gana al área disponible
    [InlineData(704.0, 0.0, 704.0, 704.0)]     // en el límite exacto no se toca nada (no es ">=")
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
    /// El piso se expresa como mínimo declarado de la ventana, pero si la ventana
    /// no lo declaró el piso es 0 y el recorte manda entero. Es el caso de las
    /// ventanas de <c>PanelShell</c>, que solo ponen <c>MaxHeight</c>.
    /// </summary>
    [Theory]
    [InlineData(820.0, 704.0)]   // TweaksWindow: MaxHeight 820 sobre un área de 728
    [InlineData(660.0, 704.0)]   // Dialog: 660 cabe y no se toca
    public void Ventana_RecortaElMaxHeightSinMinimoDeclarado(double max, double esperado) =>
        Assert.Equal(esperado, Ventana.Recortar(max, 0, 704), 3);

    /// <summary>
    /// El defecto que apareció AL separar la función para probarla: el valor por
    /// defecto de <c>MaxHeight</c> y <c>MaxWidth</c> es infinito positivo, no
    /// <c>NaN</c>. Comprobar solo el <c>NaN</c> recortaba el máximo de las nueve
    /// ventanas que no declaran ninguno, quitándoles de hecho el máximo.
    /// </summary>
    [Fact]
    public void Ventana_NoRecortaUnMaximoNoDeclarado()
    {
        Assert.True(double.IsPositiveInfinity(Ventana.Recortar(double.PositiveInfinity, 0, 704)));
        Assert.True(double.IsPositiveInfinity(Ventana.Recortar(double.PositiveInfinity, 640, 704)));
        // Y un máximo sí declarado, sí se recorta: ese es el caso de TweaksWindow.
        Assert.Equal(704.0, Ventana.Recortar(820, 0, 704), 3);
    }

    /// <summary>
    /// Un área de trabajo inválida no puede dejar a la ventana en 0 px: si el
    /// sistema no da un área utilizable, no se aplica ninguna regla.
    /// </summary>
    [Fact]
    public void Ventana_SinAreaDeTrabajoNoRecorta()
    {
        // El recorte con disponible <= 0 y sin mínimo declarado daría un tope de 0;
        // lo que protege a la ventana es que AjustarAPantalla sale antes, así que
        // lo que se fija acá es la otra mitad: que Recortar no intente ser listo.
        Assert.Equal(0.0, Ventana.Recortar(860, 0, 0), 3);
        Assert.Equal(640.0, Ventana.Recortar(860, 640, 0), 3);
    }
}
