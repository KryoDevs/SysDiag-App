using System.Collections.Generic;
using System.Linq;
using SysDiag.Core.Network;
using SysDiag.Models;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// Mapa de canales Wi-Fi: la parte que se puede probar sin un adaptador.
/// Todo lo que decide «a qué canal conviene moverse» vive en
/// <see cref="WifiChannelModule"/> y es puro a propósito: el CI no tiene Wi-Fi
/// y el equipo de quien reporta un problema tampoco es el de quien lo arregla.
/// </summary>
public class CanalesWifiTests
{
    private static WifiNetworkRow Red(string ssid, int canal, int senal = 50) =>
        new() { Ssid = ssid, Canal = canal, SenalPct = senal };

    // ---- Texto de netsh ----------------------------------------------------

    [Fact]
    public void Normalizar_QuitaAcentosMayusculasYEspacios()
    {
        Assert.Equal("senal", NetworkText.Normalizar("  Señal  "));
        Assert.Equal("aeiou n", NetworkText.Normalizar("ÁÉÍÓÚ Ñ"));
        Assert.Equal("", NetworkText.Normalizar(null));
    }

    [Fact]
    public void Entero_LeeElPrimerNumeroYDevuelveCeroSinNumeros()
    {
        Assert.Equal(11, NetworkText.Entero("Canal : 11"));
        Assert.Equal(78, NetworkText.Entero("78 %"));
        Assert.Equal(0, NetworkText.Entero("sin numeros"));
        Assert.Equal(0, NetworkText.Entero(""));
        // Documenta el límite: no lee el signo. Las etiquetas que se leen con
        // esto (canal, señal en %) nunca son negativas; un dBm sí lo es y por eso
        // no se lee acá.
        Assert.Equal(60, NetworkText.Entero("-60 dBm"));
    }

    // ---- Solapamiento ------------------------------------------------------

    [Fact]
    public void SeSolapan_En24GHzCuentaLosCanalesDeAlLado()
    {
        Assert.True(WifiChannelModule.SeSolapan(1, 2));
        Assert.True(WifiChannelModule.SeSolapan(1, 5));   // 4 de separación: se pisan
        Assert.True(WifiChannelModule.SeSolapan(2, 6));   // 4 de separación: se pisan
        Assert.False(WifiChannelModule.SeSolapan(1, 6));  // 5: ya no
        Assert.False(WifiChannelModule.SeSolapan(6, 11));
        Assert.False(WifiChannelModule.SeSolapan(1, 13));
        Assert.True(WifiChannelModule.SeSolapan(1, 1));
    }

    [Fact]
    public void SeSolapan_En5GHzSoloCuentaElMismoCanalYNoCruzaDeBanda()
    {
        Assert.True(WifiChannelModule.SeSolapan(36, 36));
        Assert.False(WifiChannelModule.SeSolapan(36, 40));
        Assert.False(WifiChannelModule.SeSolapan(40, 44));
        // Canales vecinos de bandas distintas no comparten espectro.
        Assert.False(WifiChannelModule.SeSolapan(13, 36));
        Assert.False(WifiChannelModule.SeSolapan(1, 36));
        // Sin canal (0) no hay nada que comparar.
        Assert.False(WifiChannelModule.SeSolapan(0, 1));
        Assert.False(WifiChannelModule.SeSolapan(0, 0));
    }

    [Fact]
    public void EsBanda24_SeparaLaBandaDe24DeLaDe5GHz()
    {
        Assert.False(WifiChannelModule.EsBanda24(0));
        Assert.True(WifiChannelModule.EsBanda24(1));
        Assert.True(WifiChannelModule.EsBanda24(13));
        Assert.True(WifiChannelModule.EsBanda24(35));
        Assert.False(WifiChannelModule.EsBanda24(36));
        Assert.False(WifiChannelModule.EsBanda24(149));
    }

    // ---- Análisis ----------------------------------------------------------

    [Fact]
    public void Analizar_RecomiendaElCanalNoSolapadoConMenosRedes()
    {
        var redes = new[]
        {
            Red("A", 1), Red("B", 2), Red("C", 6), Red("D", 6), Red("E", 6)
        };

        var a = WifiChannelModule.Analizar(redes, canalPropio: 6, ssidPropio: "Casa");

        // El 6 es de los tres buenos, pero acá tiene cuatro redes encima.
        Assert.Equal(11, a.Recomendado);
        Assert.Contains("11", a.Motivo);
        Assert.Equal(4, a.Canales24.Single(c => c.Canal == 6).PuntosDeAcceso);
        Assert.Equal(0, a.Canales24.Single(c => c.Canal == 11).PuntosDeAcceso);
    }

    [Fact]
    public void Analizar_ConElCanalPropioEntreLosBuenosYLibreNoCambia()
    {
        var a = WifiChannelModule.Analizar(new[] { Red("V", 11) }, canalPropio: 6, ssidPropio: "Casa");

        Assert.Equal(6, a.Recomendado);
        Assert.Contains("No hay nada que ganar", a.Motivo);
    }

    [Fact]
    public void Analizar_NoCuentaLaRedPropiaComoVecina()
    {
        var redes = new[]
        {
            Red("Casa", 6, 70), Red("Casa", 6, 50), Red("Vecina", 6, 60)
        };

        var a = WifiChannelModule.Analizar(redes, canalPropio: 6, ssidPropio: "Casa");

        Assert.Equal(3, a.RedesDetectadas);
        Assert.Equal(1, a.VecinasDetectadas);
        Assert.Equal(1, a.Canales24.Single(c => c.Canal == 6).PuntosDeAcceso);
        Assert.True(a.Canales24.Single(c => c.Canal == 6).Propio);
    }

    [Fact]
    public void Analizar_LaFilaDiceLaSenalMasFuerteQueTocaElCanal()
    {
        var redes = new[] { Red("Fuerte", 5, 80), Red("Media", 6, 40) };

        var a = WifiChannelModule.Analizar(redes, canalPropio: 6, ssidPropio: "");

        var fila = a.Canales24.Single(c => c.Canal == 6);
        Assert.Equal(2, fila.PuntosDeAcceso);
        Assert.Equal(1, fila.MismoCanal);
        Assert.Equal("80 %", fila.Senal);
        Assert.Equal(80, fila.SenalPct);
    }

    [Fact]
    public void Analizar_MarcaLaCongestionConLosMismosUmbralesDelDiagnostico()
    {
        var dos = WifiChannelModule.Analizar(new[] { Red("A", 5), Red("B", 7) }, 6, "");
        Assert.Equal(Severity.Warn, dos.Canales24.Single(c => c.Canal == 6).Estado);
        Assert.Equal(0, dos.Canales24.Single(c => c.Canal == 6).MismoCanal);

        var cuatro = WifiChannelModule.Analizar(new[] { Red("A", 4), Red("B", 5), Red("C", 7), Red("D", 8) }, 6, "");
        Assert.Equal(Severity.Bad, cuatro.Canales24.Single(c => c.Canal == 6).Estado);

        var libre = WifiChannelModule.Analizar(new[] { Red("A", 11) }, 6, "");
        Assert.Equal(Severity.Ok, libre.Canales24.Single(c => c.Canal == 1).Estado);
    }

    [Fact]
    public void Analizar_SinCanalPropioNoRecomiendaNada()
    {
        var a = WifiChannelModule.Analizar(new[] { Red("A", 6) }, canalPropio: 0, ssidPropio: "Casa");

        Assert.Equal(0, a.Recomendado);
        Assert.Contains("No se pudo leer el canal", a.Nota);
        Assert.Equal(13, a.Canales24.Count);
    }

    [Fact]
    public void Analizar_SinRedesLoDiceEnLaNota()
    {
        var a = WifiChannelModule.Analizar(new WifiNetworkRow[0], canalPropio: 0, ssidPropio: "");

        Assert.False(a.HayDatos);
        Assert.Equal(0, a.RedesDetectadas);
        Assert.Empty(a.Canales5);
        Assert.Contains("no devolvió redes", a.Nota);
    }

    [Fact]
    public void Analizar_En5GHzListaSoloLosCanalesQueSeVen()
    {
        var redes = new[] { Red("V", 40), Red("W", 44) };

        var a = WifiChannelModule.Analizar(redes, canalPropio: 36, ssidPropio: "Casa");

        Assert.Equal(new[] { 36, 40, 44 }, a.Canales5.Select(c => c.Canal).ToArray());
        Assert.Equal(36, a.Recomendado);
        Assert.Contains("está libre", a.Motivo);
        // Lo que no se puede saber se dice: el ancho de canal no viene en el escaneo.
        Assert.Contains("80 MHz", a.Nota);
    }

    [Fact]
    public void Analizar_En5GHzSeMueveSiSuCanalEstaMasOcupado()
    {
        var redes = new[] { Red("V", 36), Red("W", 36), Red("X", 44) };

        var a = WifiChannelModule.Analizar(redes, canalPropio: 36, ssidPropio: "Casa");

        Assert.Equal(44, a.Recomendado);
    }

    // ---- Lectura de «netsh wlan show interfaces» ---------------------------

    [Fact]
    public void CanalPropio_LeeLaSalidaEnEspanolYEnIngles()
    {
        string espanol =
            "    Nombre                   : Wi-Fi\r\n" +
            "    Descripción              : Intel(R) Wi-Fi 6\r\n" +
            "    Estado                   : conectado\r\n" +
            "    SSID                     : MiRed\r\n" +
            "    Canal                    : 44\r\n" +
            "    Señal                    : 78 %\r\n";

        var (canal, ssid) = WifiChannelModule.CanalPropio(espanol);
        Assert.Equal(44, canal);
        Assert.Equal("MiRed", ssid);

        string ingles = "    Name : Wi-Fi\n    State : connected\n    SSID : Otra\n    Channel : 6\n";
        var (canalEn, ssidEn) = WifiChannelModule.CanalPropio(ingles);
        Assert.Equal(6, canalEn);
        Assert.Equal("Otra", ssidEn);

        Assert.Equal(0, WifiChannelModule.CanalPropio(null).Canal);
        Assert.Equal("", WifiChannelModule.CanalPropio("").Ssid);
    }
}
