using System;
using System.Text.Json;
using System.Windows;
using SysDiag.Models;
using SysDiag.Ui;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// Estado de la ventana principal (MEJORAS §8.2, ítem 15): qué se guarda al
/// cerrar y qué se hace con eso al abrir. Se prueba la parte pura —la que
/// recibe un <see cref="Rect"/>— porque la otra solo lee la ventana real, y una
/// ventana real necesita una sesión interactiva que el CI no tiene.
/// </summary>
public class EstadoVentanaTests
{
    private static readonly Rect AreaTrabajo = new(0, 0, 1920, 1040);
    private static readonly Rect Escritorio = new(0, 0, 1920, 1080);

    /// <summary>Tamaño mínimo real de la ventana principal, tomado del XAML.</summary>
    private const double MinAncho = 1060;
    private const double MinAlto = 640;

    private static AppSettings Guardado(double x, double y, double ancho, double alto) => new()
    {
        VentanaX = x,
        VentanaY = y,
        VentanaAncho = ancho,
        VentanaAlto = alto
    };

    // ---- Lectura de settings.json -----------------------------------------

    [Fact]
    public void Desde_SinNadaGuardadoNoHayPosicion()
    {
        var vacio = EstadoVentana.Desde(AppSettings.PorDefecto());

        Assert.False(vacio.HayPosicion);
        Assert.False(vacio.Maximizada);
        Assert.Equal("", vacio.Vista);
        Assert.Equal(0d, vacio.Ancho);
        Assert.Equal(0d, vacio.X);
    }

    [Fact]
    public void Desde_UnaPosicionAMediasOImposibleSeDescarta()
    {
        // Solo X e Y: sin tamaño, la ventana no se puede reabrir donde estaba.
        Assert.False(EstadoVentana.Desde(new AppSettings { VentanaX = 10, VentanaY = 20 }).HayPosicion);
        // Un valor no finito tampoco: no es una coordenada.
        Assert.False(EstadoVentana.Desde(new AppSettings
        {
            VentanaX = 0, VentanaY = 0, VentanaAncho = double.PositiveInfinity, VentanaAlto = 600
        }).HayPosicion);
        Assert.False(EstadoVentana.Desde(Guardado(0, 0, 0, 600)).HayPosicion);
    }

    [Fact]
    public void Desde_ConservaLaPosicionYElTamanioGuardados()
    {
        var estado = EstadoVentana.Desde(Guardado(200, 150, 1340, 860));

        Assert.True(estado.HayPosicion);
        Assert.Equal(200d, estado.X);
        Assert.Equal(150d, estado.Y);
        Assert.Equal(1340d, estado.Ancho);
        Assert.Equal(860d, estado.Alto);
    }

    [Fact]
    public void Desde_SoloAceptaVistasYFiltrosConocidos()
    {
        var bueno = EstadoVentana.Desde(new AppSettings { UltimaVista = "Datos", UltimoFiltroRegistro = "errores" });
        Assert.Equal("Datos", bueno.Vista);
        Assert.Equal("errores", bueno.Filtro);

        // Un valor de otra versión (o escrito a mano) no puede decidir la
        // interfaz: se descarta y la ventana abre como la primera vez.
        var raro = EstadoVentana.Desde(new AppSettings { UltimaVista = "Inventada", UltimoFiltroRegistro = "nada" });
        Assert.Equal("", raro.Vista);
        Assert.Equal("", raro.Filtro);
    }

    [Fact]
    public void Desde_ConservaElEstadoMaximizado()
    {
        var estado = EstadoVentana.Desde(new AppSettings { VentanaMaximizada = true });

        Assert.True(estado.Maximizada);
        Assert.False(estado.HayPosicion);
    }

    // ---- Ubicación en el escritorio ---------------------------------------

    [Fact]
    public void Ubicar_SinPosicionGuardadaNoDecideNada()
    {
        var caja = EstadoVentana.Ubicar(EstadoVentana.Desde(AppSettings.PorDefecto()),
            MinAncho, MinAlto, AreaTrabajo, Escritorio);

        Assert.False(caja.HasValue);
    }

    [Fact]
    public void Ubicar_SinEscritorioNoDevuelveCaja()
    {
        var caja = EstadoVentana.Ubicar(EstadoVentana.Desde(Guardado(0, 0, 1340, 860)),
            MinAncho, MinAlto, AreaTrabajo, Rect.Empty);

        Assert.False(caja.HasValue);
    }

    [Fact]
    public void Ubicar_RespetaLaPosicionGuardadaDentroDelEscritorio()
    {
        // Guardada en un monitor secundario que sigue conectado (escritorio de
        // 3840 de ancho): se reabre ahí y no en el principal.
        var escritorio = new Rect(0, 0, 3840, 1080);

        var caja = EstadoVentana.Ubicar(EstadoVentana.Desde(Guardado(2000, 100, 1200, 760)),
            MinAncho, MinAlto, AreaTrabajo, escritorio);

        Assert.True(caja.HasValue);
        Assert.Equal(new Rect(2000, 100, 1200, 760), caja.Value);
    }

    [Fact]
    public void Ubicar_AcotaElTamanioAlAreaDeTrabajo()
    {
        // Una ventana más grande que la pantalla no se reabre así: es la misma
        // regla que Ventana.AjustarAPantalla aplica al abrir.
        var caja = EstadoVentana.Ubicar(EstadoVentana.Desde(Guardado(0, 0, 4000, 3000)),
            MinAncho, MinAlto, AreaTrabajo, Escritorio);

        Assert.True(caja.HasValue);
        Assert.Equal(new Rect(0, 0, 1920, 1040), caja.Value);
    }

    [Fact]
    public void Ubicar_RespetaElTamanioMinimoDeLaVentana()
    {
        var caja = EstadoVentana.Ubicar(EstadoVentana.Desde(Guardado(100, 100, 300, 200)),
            MinAncho, MinAlto, AreaTrabajo, Escritorio);

        Assert.True(caja.HasValue);
        Assert.Equal(new Rect(100, 100, 1060, 640), caja.Value);
    }

    [Fact]
    public void Ubicar_RecentraCuandoLaPantallaYaNoEsta()
    {
        // Guardada en un monitor que ya no existe: sin esto la ventana nacería
        // fuera del escritorio y no habría forma de agarrarla con el mouse.
        var caja = EstadoVentana.Ubicar(EstadoVentana.Desde(Guardado(5000, 5000, 800, 600)),
            MinAncho, MinAlto, AreaTrabajo, Escritorio);

        Assert.True(caja.HasValue);
        Assert.Equal(new Rect(430, 200, 1060, 640), caja.Value);
    }

    [Fact]
    public void Ubicar_RecentraCuandoSoloQuedaUnBordeVisible()
    {
        // 70 px dentro del escritorio: se ve, pero no alcanza para arrastrarla.
        var caja = EstadoVentana.Ubicar(EstadoVentana.Desde(Guardado(1850, 100, 800, 600)),
            MinAncho, MinAlto, AreaTrabajo, Escritorio);

        Assert.True(caja.HasValue);
        Assert.Equal(new Rect(430, 200, 1060, 640), caja.Value);
    }

    // ---- Escritura al cerrar ----------------------------------------------

    [Fact]
    public void VolcarRect_GuardaLaCajaYLasPreferencias()
    {
        var ajustes = new AppSettings();

        EstadoVentana.VolcarRect(ajustes, new Rect(10, 20, 800, 600), maximizada: true,
            vista: "Datos", filtro: "avisos");

        Assert.Equal(10d, ajustes.VentanaX);
        Assert.Equal(20d, ajustes.VentanaY);
        Assert.Equal(800d, ajustes.VentanaAncho);
        Assert.Equal(600d, ajustes.VentanaAlto);
        Assert.True(ajustes.VentanaMaximizada);
        Assert.Equal("Datos", ajustes.UltimaVista);
        Assert.Equal("avisos", ajustes.UltimoFiltroRegistro);
    }

    [Fact]
    public void VolcarRect_UnaCajaInvalidaNoPisaLoGuardado()
    {
        var ajustes = Guardado(5, 6, 700, 500);

        EstadoVentana.VolcarRect(ajustes, Rect.Empty, maximizada: false, vista: "Inventada", filtro: "nada");

        Assert.Equal(5d, ajustes.VentanaX);
        Assert.Equal(6d, ajustes.VentanaY);
        Assert.Equal(700d, ajustes.VentanaAncho);
        Assert.Equal(500d, ajustes.VentanaAlto);
        // Lo que no es una vista conocida no se guarda: no hay que dejar en el
        // archivo un valor que la próxima carga va a descartar.
        Assert.Equal("", ajustes.UltimaVista);
        Assert.Equal("", ajustes.UltimoFiltroRegistro);
        Assert.False(ajustes.VentanaMaximizada);
    }

    [Fact]
    public void LaPosicionSobreviveALaIdaYVueltaPorJson()
    {
        var ajustes = new AppSettings();
        EstadoVentana.VolcarRect(ajustes, new Rect(1200, 40, 900, 700), maximizada: true,
            vista: "Hallazgos", filtro: "todo");

        // Es el camino real: settings.json. Un NaN acá rompería la escritura
        // («no es JSON válido»), y por eso la posición guardada es anulable.
        string json = JsonSerializer.Serialize(ajustes.Normalizar());
        var vuelta = JsonSerializer.Deserialize<AppSettings>(json)!.Normalizar();

        Assert.Equal(1200d, vuelta.VentanaX);
        Assert.Equal(40d, vuelta.VentanaY);
        Assert.Equal(900d, vuelta.VentanaAncho);
        Assert.Equal(700d, vuelta.VentanaAlto);
        Assert.True(vuelta.VentanaMaximizada);
        Assert.Equal("Hallazgos", vuelta.UltimaVista);
    }

    [Fact]
    public void Normalizar_DescartaUnaPosicionImposible()
    {
        var imposible = new AppSettings { VentanaX = 10, VentanaY = 10, VentanaAncho = -5, VentanaAlto = 600 };
        Assert.Null(imposible.Normalizar().VentanaX);

        // Y sin nada guardado, la normalización no inventa una posición.
        Assert.Null(AppSettings.PorDefecto().Normalizar().VentanaX);
    }
}
