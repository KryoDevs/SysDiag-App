using System.IO;
using SysDiag.Core.Windows;
using SysDiag.Models;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// Los ajustes llegan a los módulos como <see cref="OpcionesDiagnostico"/>, no por estáticos.
/// SettingsService tampoco tenía ninguna prueba: estas usan un archivo temporal, no el settings.json del usuario.
/// </summary>
public class OpcionesYAjustesTests
{
    [Fact]
    public void OpcionesPorDefecto_CoincidenConLosDefectosDeAjustes()
    {
        var defecto = AppSettings.PorDefecto();
        var o = OpcionesDiagnostico.PorDefecto;

        Assert.Equal(defecto.SampleSeconds, o.MuestreoSegundos);
        Assert.Equal(defecto.PingCount, o.Pings);
        Assert.Equal(defecto.EventDays, o.DiasEventos);
        Assert.Equal(defecto.WheaDays, o.DiasWhea);
        Assert.Equal(defecto.HistorialMaximo, o.HistorialMaximo);
        Assert.Equal(defecto.LogsMaximo, o.LogsMaximo);
    }

    [Fact]
    public void Desde_NormalizaLosValoresFueraDeRango()
    {
        var o = OpcionesDiagnostico.Desde(new AppSettings { EventDays = 999, SampleSeconds = 0 });

        Assert.Equal(90, o.DiasEventos);
        Assert.Equal(2, o.MuestreoSegundos);
    }

    [Fact]
    public void GuardarYCargar_IdaYVueltaConValores()
    {
        using var dir = new TemporaryDirectory();
        string ruta = Path.Combine(dir.Path, "settings.json");

        Assert.True(SettingsService.Guardar(new AppSettings { SampleSeconds = 7, PingCount = 30, EventDays = 45 }, ruta));
        var cargado = SettingsService.Cargar(ruta);

        Assert.Equal(7, cargado.SampleSeconds);
        Assert.Equal(30, cargado.PingCount);
        Assert.Equal(45, cargado.EventDays);
    }

    [Fact]
    public void Cargar_ArchivoCorruptoOInexistenteUsaLosDefectos()
    {
        using var dir = new TemporaryDirectory();
        string ruta = Path.Combine(dir.Path, "settings.json");

        var sinArchivo = SettingsService.Cargar(ruta);
        Assert.Equal(AppSettings.PorDefecto().SampleSeconds, sinArchivo.SampleSeconds);

        File.WriteAllText(ruta, "{ esto no es json");
        var corrupto = SettingsService.Cargar(ruta);
        Assert.Equal(AppSettings.PorDefecto().EventDays, corrupto.EventDays);
    }

    [Fact]
    public void Cargar_NormalizaUnArchivoEditadoAMano()
    {
        using var dir = new TemporaryDirectory();
        string ruta = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(ruta, "{\"SampleSeconds\":0,\"PingCount\":-1,\"EventDays\":2147483647}");

        var cargado = SettingsService.Cargar(ruta);

        Assert.Equal(2, cargado.SampleSeconds);
        Assert.Equal(5, cargado.PingCount);
        Assert.Equal(90, cargado.EventDays);
    }
}
