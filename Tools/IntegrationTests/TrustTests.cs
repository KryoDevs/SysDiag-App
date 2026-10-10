using System;
using System.Collections.Generic;
using System.Linq;
using SysDiag.Core.Diagnostics;
using SysDiag.Core.Windows;
using SysDiag.Models;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// Pruebas del lote «Confiar» sobre las partes que son funciones puras:
/// comparación de valores releídos, redacción de informes y armado del
/// Markdown.
///
/// Están acá por lo mismo que `StatsTests`: lo que se puede probar sin Windows
/// se prueba sin Windows. La verificación post-cambio y la redacción son las
/// dos piezas nuevas en las que un error no se ve —la primera dice «aplicado»
/// de algo que no quedó, la segunda deja pasar un dato que debería haber
/// quitado—, las dos fallan en silencio, y las dos se pueden ejercitar con un
/// reporte construido a mano.
/// </summary>
public class TrustTests
{
    // ---- ChangeVerifier ---------------------------------------------------

    [Fact]
    public void Coincide_EnteroEscritoYEnteroLeido_delRegistro()
    {
        // El registro devuelve `int` donde se escribió un 0 y `long` donde se
        // escribió un valor grande. Comparar por texto haría fallar la
        // verificación justo en los DWORD más comunes.
        Assert.True(ChangeVerifier.Coincide(0, "0"));
        Assert.True(ChangeVerifier.Coincide(1, "1"));
        Assert.True(ChangeVerifier.Coincide(1000000L, "1000000"));
    }

    [Fact]
    public void Coincide_ValorAusente_noEsAplicado()
    {
        // «No hay valor» no es «el valor correcto». Sin esto, un ajuste que
        // Windows repuso se daría por aplicado en el mismo instante.
        Assert.False(ChangeVerifier.Coincide(null, "0"));
        Assert.False(ChangeVerifier.Coincide(null, ""));
    }

    [Fact]
    public void Coincide_Cadena_devuelveTextoExacto()
    {
        Assert.True(ChangeVerifier.Coincide("Auto", "Auto"));
        Assert.False(ChangeVerifier.Coincide("auto", "Auto"));
    }

    [Fact]
    public void Verificar_releeAlMomentoYNoUnValorCacheado()
    {
        // El delegado se invoca dentro de Verificar: si se le pasara un valor
        // ya leído, la comprobación daría siempre «bien» y no serviría para
        // nada. Este contador lo detecta.
        int lecturas = 0;
        var verificacion = ChangeVerifier.Verificar("ajuste", () => { lecturas++; return 1; }, "1");

        Assert.True(verificacion.Aplicado);
        Assert.Equal(1, lecturas);
    }

    [Fact]
    public void Verificar_cuandoNoQueda_explicaPorQuePuedeSer()
    {
        var verificacion = ChangeVerifier.Verificar("ajuste", () => 0, "1", requiereAdmin: true);

        Assert.False(verificacion.Aplicado);
        Assert.Equal("1", verificacion.Esperado);
        // El motivo nunca afirma una causa que no se pudo comprobar: enumera
        // las plausibles. Pero tiene que decir algo, o el usuario se queda
        // mirando un «no se aplicó» sin nada más.
        Assert.False(string.IsNullOrWhiteSpace(verificacion.Motivo));
    }

    [Fact]
    public void Verificar_sinAccesoAlReleer_informaYNoFalla()
    {
        var verificacion = ChangeVerifier.Verificar("ajuste",
            () => throw new UnauthorizedAccessException("sin permisos"), "1");

        Assert.False(verificacion.Aplicado);
        Assert.Equal("sin acceso", verificacion.Leido);
    }

    // ---- Redactor ---------------------------------------------------------

    private static DiagnosticReport ReporteConDatosIdentificables()
    {
        var r = new DiagnosticReport { Equipo = "PC-DE-MARIANA" };
        r.Sistema.Add(new KeyValueRow("Número de serie", "SN-8899-AABB"));
        r.Sistema.Add(new KeyValueRow("Procesador", "Intel Core i7"));
        r.WiFi.Add(new KeyValueRow("SSID", "Fibra-Casa-2G"));
        r.RedesCercanas.Add(new WifiNetworkRow { Ssid = "Vecinos-5G", Canal = 36 });
        r.RedesCercanas.Add(new WifiNetworkRow { Ssid = "Vecinos-2G", Canal = 6 });
        r.Hallazgos.Add(new Finding { Severity = Severity.Warn, Area = "Disco", Message = "Poco espacio" });
        return r;
    }

    [Fact]
    public void Redaccion_quitaElNombreDelEquipo()
    {
        var original = ReporteConDatosIdentificables();
        var copia = Redactor.Aplicar(original);

        Assert.NotEqual("PC-DE-MARIANA", copia.Equipo);
        Assert.DoesNotContain("PC-DE-MARIANA", JsonDelCopia(copia));
    }

    [Fact]
    public void Redaccion_noTocaElInformeOriginal()
    {
        // El informe del usuario tiene que seguir legible en su carpeta: es
        // suyo. Si la redacción se aplicara sobre el original, compartir un
        // informe dejaría inutilizable el archivo local.
        var original = ReporteConDatosIdentificables();
        Redactor.Aplicar(original);

        Assert.Equal("PC-DE-MARIANA", original.Equipo);
        Assert.Contains("SN-8899-AABB", original.Sistema.First(x => x.Clave == "Número de serie").Valor);
    }

    [Fact]
    public void Redaccion_dosRedesDistintasSiguenSiendoDistinguibles()
    {
        // Todas las redes con el mismo texto dejaría ilegible la tabla de
        // solapamiento de canales, que es justo lo que más se comparte.
        var copia = Redactor.Aplicar(ReporteConDatosIdentificables());

        var ssids = copia.RedesCercanas.Select(x => x.Ssid).ToList();
        Assert.Equal(2, ssids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Redaccion_quitaSeriesYNombresDeRed()
    {
        var copia = Redactor.Aplicar(ReporteConDatosIdentificables());
        string json = JsonDelCopia(copia);

        Assert.DoesNotContain("SN-8899-AABB", json);
        Assert.DoesNotContain("Fibra-Casa-2G", json);
        Assert.DoesNotContain("Vecinos-5G", json);
    }

    [Fact]
    public void Redaccion_declaraEnElPropioInformeQueSequito()
    {
        // Uno censurado en silencio confunde a quien lo recibe y le hace dudar
        // de los datos que sí están.
        var copia = Redactor.Aplicar(ReporteConDatosIdentificables());

        Assert.Contains(copia.Sistema, x => x.Clave == "Informe redactado");
        Assert.Contains("redact", copia.Sistema.First(x => x.Clave == "Informe redactado").Valor,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Redaccion_conservaLosDatosQueNoIdentifican()
    {
        // Redactar de más arruina el informe: si se borra el procesador, quien
        // intenta ayudar no tiene con qué trabajar.
        var copia = Redactor.Aplicar(ReporteConDatosIdentificables());

        Assert.Contains("Intel Core i7", copia.Sistema.First(x => x.Clave == "Procesador").Valor);
        Assert.NotEmpty(copia.Hallazgos);
    }

    // ---- MarkdownReport ---------------------------------------------------

    [Fact]
    public void Markdown_incluyePuntajeYAlcance()
    {
        var r = new DiagnosticReport { Equipo = "EQUIPO" };
        r.Hallazgos.Add(new Finding { Severity = Severity.Bad, Area = "Disco", Message = "Poco espacio libre" });

        string md = MarkdownReport.Construir(r);

        Assert.Contains("# Diagnóstico SysDiag", md);
        Assert.Contains("## Alcance", md);
        Assert.Contains("## Hallazgos", md);
        Assert.Contains("Poco espacio libre", md);
    }

    [Fact]
    public void Markdown_elAlcanceVaAntesQueLosHallazgos()
    {
        // Un puntaje alto sobre un diagnóstico incompleto significa otra cosa.
        // Si la tabla va sin el alcance al lado, miente por omisión.
        var r = new DiagnosticReport();
        string md = MarkdownReport.Construir(r);

        Assert.True(md.IndexOf("## Alcance", StringComparison.Ordinal)
                    < md.IndexOf("## Hallazgos", StringComparison.Ordinal));
    }

    [Fact]
    public void Markdown_escapaLasBarrasVerticalesDeLasCeldas()
    {
        // Un `|` dentro de una celda rompe la tabla entera, no solo la fila:
        // en Markdown no hay escape cómodo y lo que se ve después es una
        // columna fantasma.
        var r = new DiagnosticReport();
        r.Sistema.Add(new KeyValueRow("Ruta", @"C:\Users\pepe|Datos"));

        string md = MarkdownReport.Construir(r);

        Assert.Contains(@"C:\Users\pepe\|Datos", md);
    }

    private static string JsonDelCopia(DiagnosticReport r) => Exporter.Serializar(r);
}
