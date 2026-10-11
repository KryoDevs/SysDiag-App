using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SysDiag.Models;
using SysDiag.Ui;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// Ui/Converters.cs no tenía pruebas. Estas se limitan a lo que no depende de una aplicación WPF en
/// marcha: sin <c>Application.Current</c> los pinceles caen a su valor por defecto, y las pruebas no
/// lo asumen como correcto, solo comprueban lo que el convertidor decide.
/// </summary>
public class ConvertersTests
{
    private static readonly CultureInfo Cultura = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData(true, Visibility.Visible)]
    [InlineData(false, Visibility.Collapsed)]
    [InlineData(null, Visibility.Collapsed)]
    public void BoolToVisibility_SoloLaVerdadMuestra(object? valor, Visibility esperado)
    {
        Assert.Equal(esperado, new BoolToVisibility().Convert(valor!, typeof(Visibility), null!, Cultura));
    }

    [Theory]
    [InlineData(true, Visibility.Collapsed)]
    [InlineData(false, Visibility.Visible)]
    public void BoolToVisibilityInverse_InvierteElEstado(bool valor, Visibility esperado)
    {
        Assert.Equal(esperado, new BoolToVisibilityInverse().Convert(valor, typeof(Visibility), null!, Cultura));
    }

    [Theory]
    [InlineData("", Visibility.Collapsed)]
    [InlineData("   ", Visibility.Collapsed)]
    [InlineData(null, Visibility.Collapsed)]
    [InlineData("Rótulo", Visibility.Visible)]
    public void NotEmptyToVisibility_UnRotuloVacioSeOculta(string? texto, Visibility esperado)
    {
        Assert.Equal(esperado, new NotEmptyToVisibility().Convert(texto!, typeof(Visibility), null!, Cultura));
    }

    [Theory]
    [InlineData("termicas", "Térmicas")]
    [InlineData("TERMICAS", "Térmicas")]
    [InlineData("almacenamiento", "Disco")]
    [InlineData("modulo-desconocido", "modulo-desconocido")]
    public void ModuleLabel_TraduceLaClaveYDejaLasDesconocidasComoEstan(string clave, string esperado)
    {
        Assert.Equal(esperado, new ModuleLabelConverter().Convert(clave, typeof(string), null!, Cultura));
    }

    [Fact]
    public void ModuleLabel_NoSeRompeConValoresQueNoSonTexto()
    {
        var convertidor = new ModuleLabelConverter();
        Assert.Equal("", convertidor.Convert(null!, typeof(string), null!, Cultura));
        Assert.Equal("", convertidor.Convert(42, typeof(string), null!, Cultura));
    }

    [Theory]
    [InlineData(Severity.Bad)]
    [InlineData(Severity.Warn)]
    [InlineData(Severity.Ok)]
    public void SeverityToBrush_SiempreDevuelveUnPincel(Severity severidad)
    {
        Assert.IsAssignableFrom<Brush>(new SeverityToBrush().Convert(severidad, typeof(Brush), null!, Cultura));
    }

    [Theory]
    [InlineData(Severity.Bad)]
    [InlineData(Severity.Warn)]
    [InlineData(Severity.Ok)]
    public void SeverityToWash_EsUnTintePocoOpacoDelMismoTono(Severity severidad)
    {
        var lavado = Assert.IsType<SolidColorBrush>(new SeverityToWash().Convert(severidad, typeof(Brush), null!, Cultura));
        // Alfa 38 de 255: el distintivo se lee como pertenencia al color sin competir con el texto.
        Assert.Equal(38, lavado.Color.A);
    }
}
