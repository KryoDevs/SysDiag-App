using SysDiag.Core.Windows;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// Las partes puras del módulo de activación: normalización, validación y
/// enmascarado de claves.
///
/// Son las únicas que se pueden probar sin Windows y sin una licencia real, y
/// son justamente las que tienen que estar probadas. Una validación laxa deja
/// pasar una clave mal escrita hasta la utilidad del sistema; una validación
/// estricta de más rechaza una clave correcta. Y el enmascarado es la única
/// barrera entre una clave de producto y un archivo de registro que se comparte
/// cuando se pide soporte: si se rompe, se filtra una licencia.
/// </summary>
public class ActivationTests
{
    private const string ClaveValida = "AAAAABBBBBCCCCCDDDDDEEEEE";

    [Fact]
    public void Normalizar_QuitaGuionesEspaciosYPasaAMayusculas()
    {
        Assert.Equal(ClaveValida, ActivationModule.NormalizarClave("aaaaa-bbbbb-ccccc-ddddd-eeeee"));
        Assert.Equal(ClaveValida, ActivationModule.NormalizarClave(" aaaaa bbbbb ccccc ddddd eeeee "));
        Assert.Equal(ClaveValida, ActivationModule.NormalizarClave("AAAAABBBBBCCCCCDDDDDEEEEE"));
    }

    [Fact]
    public void Normalizar_DeVacioNoRompe()
    {
        Assert.Equal("", ActivationModule.NormalizarClave(null));
        Assert.Equal("", ActivationModule.NormalizarClave(""));
        Assert.Equal("", ActivationModule.NormalizarClave("   "));
    }

    [Fact]
    public void Validar_AceptaUnaClaveDe25Caracteres()
    {
        Assert.Null(ActivationModule.ValidarClave(ClaveValida));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("AAAAABBBBBCCCCCDDDDD")]          // 20: falta un grupo
    [InlineData("AAAAABBBBBCCCCCDDDDDEEEEEAAAAA")] // 30: un grupo de más
    [InlineData("AAAA!BBBBBCCCCCDDDDDEEEEE")]      // carácter no alfanumérico
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAA")]      // 25 pero todo igual
    public void Validar_RechazaLoQueNoPuedeSerUnaClave(string clave)
    {
        Assert.NotNull(ActivationModule.ValidarClave(ActivationModule.NormalizarClave(clave)));
    }

    [Fact]
    public void Enmascarar_NoDejaVerLaClaveCompleta()
    {
        string enmascarada = ActivationModule.Enmascarar(ClaveValida);

        Assert.DoesNotContain("AAAAA", enmascarada);
        Assert.EndsWith("EEEEE", enmascarada);
        Assert.True(enmascarada.Length < ClaveValida.Length,
            "el enmascarado tiene que ser más corto que la clave, no igual");
    }

    [Fact]
    public void Enmascarar_DeClavesCortasNoDevuelveNadaAprovechable()
    {
        Assert.DoesNotContain("AB", ActivationModule.Enmascarar("AB"));
        Assert.Equal("", ActivationModule.Enmascarar(""));
        Assert.Equal("", ActivationModule.Enmascarar(null));
    }
}
