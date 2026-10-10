using SysDiag.Core.Licensing;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// Comportamiento del archivo de licencia que la auditoría encontró roto: la
/// aplicación **borraba** el código del usuario cuando no verificaba
/// (`_archivo.Codigo = null`), y como el código vinculado se deriva de
/// máquina\usuario, renombrar el PC o entrar con otra cuenta apagaba la licencia
/// de alguien que la compró y le quitaba hasta el código para recuperarla.
///
/// Estas pruebas existen gracias a que `Inicializar` acepta una ruta: sin eso,
/// comprobar esto habría significado tocar el archivo de licencia real de quien
/// ejecuta la suite, que es exactamente el daño que el arreglo quería evitar.
/// Se apunta a un directorio temporal y no se restaura al final a propósito:
/// "restaurar" significaría llamar a `Inicializar()` sin ruta, y eso escribe en
/// `LocalAppData`.
/// </summary>
public class LicenseRegressionTests : IDisposable
{
    private readonly string _directorio;
    private readonly string _ruta;

    public LicenseRegressionTests()
    {
        _directorio = Path.Combine(Path.GetTempPath(), "sysdiag-prueba-licencia-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directorio);
        _ruta = Path.Combine(_directorio, "licencia.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_directorio, recursive: true); }
        catch (IOException) { /* el borrado de una prueba no puede hacer fallar nada */ }
    }

    /// <summary>
    /// El caso del comprador: un archivo en el formato anterior (sin `Equipo`) con
    /// un código que no verifica. El estado tiene que ser el de prueba —no se
    /// concede nada a ciegas—, pero el código sobrevive en memoria y en disco, y
    /// el usuario recibe una explicación.
    /// </summary>
    [Fact]
    public void LicenseService_KeepsAnUnverifiableCodeInsteadOfDeletingIt()
    {
        string codigo = "SDG7-AAAAA-BBBBB-CCCCC-DDDDD-EEEEE";
        File.WriteAllText(_ruta, """
            {"PrimerInicio":"PRIMER_INICIO","Codigo":"CODIGO","Activacion":"2026-01-02T10:00:00"}
            """.Replace("PRIMER_INICIO", DateTime.Now.AddDays(-1).ToString("o"))
               .Replace("CODIGO", codigo));

        LicenseService.Inicializar(_ruta);

        Assert.Equal(EstadoLicencia.Prueba, LicenseService.Estado);
        Assert.Equal(codigo, LicenseService.CodigoActivo);
        Assert.Contains(codigo, File.ReadAllText(_ruta));
        Assert.False(string.IsNullOrEmpty(LicenseService.Aviso));
    }

    /// <summary>
    /// Un archivo corrupto no puede dejar la aplicación sin prueba que perder: se
    /// abre en estado de prueba y se escribe un archivo nuevo. Y si el JSON está
    /// roto no se destruye lo que había adentro sin oportunidad de recuperarlo.
    /// </summary>
    [Fact]
    public void LicenseService_StartsInTrialWhenTheFileIsNotReadable()
    {
        File.WriteAllText(_ruta, "{ esto no es json ");
        LicenseService.Inicializar(_ruta);

        Assert.Equal(EstadoLicencia.Prueba, LicenseService.Estado);
        Assert.True(LicenseService.DiasPruebaRestantes > 0);
        Assert.True(File.Exists(_ruta));
    }

    /// <summary>
    /// La otra cara: un código emitido por el propio emisor, no vinculado, sigue
    /// activando. Si alguna vez se toca el formato o la clave, esto tiene que
    /// gritar antes de que el usuario se entere.
    /// </summary>
    [Fact]
    public void LicenseService_ActivatesACodeIssuedByTheBundledEmitter()
    {
        // Primero se apunta el servicio al archivo temporal: `Activar` escribe, y
        // sin este orden el test estaría escribiendo en la licencia real de
        // quien corre la suite.
        File.WriteAllText(_ruta, "{ no hay archivo todavia ");
        LicenseService.Inicializar(_ruta);

        string codigo = LicenseCrypto.Generar(dias: 30);
        Assert.True(LicenseService.Activar(codigo), LicenseService.UltimoError);
        Assert.Equal(EstadoLicencia.Pro, LicenseService.Estado);

        // Y la prueba de que el estado persiste donde debe: releer el archivo
        // desde cero tiene que dar Pro otra vez.
        LicenseService.Inicializar(_ruta);
        Assert.Equal(EstadoLicencia.Pro, LicenseService.Estado);
        Assert.Equal(codigo, LicenseService.CodigoActivo);
    }

    /// <summary>
    /// Un código perpetuo escrito con minúsculas, espacios y sin prefijo tiene que
    /// entrar igual: se teclea desde un correo o un PDF, no desde un programa.
    /// </summary>
    [Fact]
    public void LicenseService_ToleratesHowUsersTypeACode()
    {
        File.WriteAllText(_ruta, "{ no hay archivo todavia ");
        LicenseService.Inicializar(_ruta);

        string limpio = LicenseCrypto.Generar(dias: 0);
        string escrito = "  " + limpio.ToLowerInvariant().Replace("-", " ") + "  ";
        Assert.True(LicenseService.Activar(escrito), LicenseService.UltimoError);
        Assert.True(LicenseCrypto.Verificar(limpio, out CodigoInfo info, out _));
        Assert.True(info.EsPerpetua);
    }
}
