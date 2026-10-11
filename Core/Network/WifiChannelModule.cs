using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SysDiag.Models;

namespace SysDiag.Core.Network;

/// <summary>
/// Mapa de canales Wi-Fi: de las redes que se ven alrededor, cuáles comparten
/// espectro con qué canal, y cuál conviene usar.
///
/// El diagnóstico general ya avisa «N redes comparten tu canal». Eso contesta
/// la mitad de la pregunta: no dice **a qué canal conviene moverse** ni por qué,
/// y en 2.4 GHz —donde los canales se solapan— mirar solo el número propio
/// hace perder de vista la mitad de la interferencia.
///
/// Tres decisiones que valen la pena:
///
/// - **En 2.4 GHz se cuentan los solapamientos, no las coincidencias.** Los
///   canales 1 a 13 están separados por 5 MHz, pero cada red ocupa unos 22 MHz:
///   dos redes se interfieren si sus canales distan menos de 5. Por eso la
///   recomendación sale de los tres únicos canales que no se tocan entre sí
///   (1, 6 y 11), y no del canal con menos coincidencias exactas.
/// - **La red propia no es interferencia.** Aparece en el escaneo como cualquier
///   otra y contarla inflaría justo el canal que se está analizando.
/// - **Lo que no se puede saber, se dice.** Windows no informa el ancho de canal
///   de cada red, así que en 5 GHz la comparación es por número de canal exacto
///   y eso queda escrito en la nota: dos redes de 80 MHz pueden compartir
///   espectro sin compartir el número.
/// </summary>
public static class WifiChannelModule
{
    /// <summary>
    /// Separación mínima, en número de canal, para que dos redes de 2.4 GHz no
    /// se interfieran. Cada canal ocupa 5 MHz de la banda y una red ocupa unos
    /// 22 MHz: 22 / 5 ≈ 4,4, y de ahí el 5.
    /// </summary>
    public const int SeparacionSinSolape = 5;

    /// <summary>El primer canal de 5 GHz que se ve en un escaneo doméstico.</summary>
    public const int Primero5GHz = 36;

    /// <summary>
    /// Los tres canales de 2.4 GHz que no se solapan entre sí. Es el conjunto
    /// entre el que tiene sentido elegir: recomendar el 3 sería recomendar una
    /// banda que de todos modos choca con el 1 y con el 6.
    /// </summary>
    public static readonly int[] NoSolapados24 = { 1, 6, 11 };

    public static bool EsBanda24(int canal) => canal > 0 && canal < Primero5GHz;

    /// <summary>¿Estas dos redes comparten espectro? Ver la nota de la clase.</summary>
    public static bool SeSolapan(int canalA, int canalB)
    {
        if (canalA <= 0 || canalB <= 0) return false;
        if (EsBanda24(canalA) != EsBanda24(canalB)) return false;
        return EsBanda24(canalA)
            ? Math.Abs(canalA - canalB) < SeparacionSinSolape
            : canalA == canalB;
    }

    /// <summary>
    /// Análisis puro: mismas redes, mismo resultado, sin tocar el equipo. Es lo
    /// que se puede probar sin un adaptador Wi-Fi.
    /// </summary>
    /// <param name="redes">Lo que devolvió el escaneo (una fila por BSSID).</param>
    /// <param name="canalPropio">Canal de la conexión del equipo, 0 si no hay o no se pudo leer.</param>
    /// <param name="ssidPropio">Nombre de la red propia, para no contarla como vecina.</param>
    public static AnalisisCanales Analizar(IEnumerable<WifiNetworkRow> redes, int canalPropio, string ssidPropio)
    {
        var todas = (redes ?? Enumerable.Empty<WifiNetworkRow>())
            .Where(r => r != null && r.Canal > 0)
            .ToList();

        // Una red con el mismo nombre que la propia es la propia o un repetidor
        // de la propia; en cualquiera de los dos casos moverse de canal no la
        // evita, así que no cuenta como interferencia. Es el mismo criterio que
        // usa el diagnóstico general.
        string propio = (ssidPropio ?? "").Trim();
        var vecinas = propio.Length == 0
            ? todas
            : todas.Where(r => !string.Equals((r.Ssid ?? "").Trim(), propio, StringComparison.OrdinalIgnoreCase)).ToList();

        var analisis = new AnalisisCanales
        {
            CanalPropio = canalPropio,
            SsidPropio = propio,
            RedesDetectadas = todas.Count,
            VecinasDetectadas = vecinas.Count
        };

        analisis.Canales24 = Enumerable.Range(1, 13)
            .Select(c => Ocupacion(c, vecinas, canalPropio))
            .ToList();

        // En 5 GHz no hay una lista fija razonable (son decenas de canales y la
        // mayoría están vacíos): se listan los observados, más el propio y el
        // recomendado, que son los dos que el usuario necesita comparar.
        var canales5 = vecinas.Where(r => !EsBanda24(r.Canal)).Select(r => r.Canal)
            .Concat(canalPropio >= Primero5GHz ? new[] { canalPropio } : Array.Empty<int>())
            .Distinct().OrderBy(c => c).ToList();

        Recomendar(analisis, vecinas, canalPropio);

        if (analisis.Recomendado > 0 && !canales5.Contains(analisis.Recomendado) && !EsBanda24(analisis.Recomendado))
        {
            canales5.Add(analisis.Recomendado);
            canales5.Sort();
        }

        analisis.Canales5 = canales5.Select(c => Ocupacion(c, vecinas, canalPropio)).ToList();
        analisis.Nota = Nota(analisis, vecinas, canalPropio);
        return analisis;
    }

    private static CanalOcupacion Ocupacion(int canal, List<WifiNetworkRow> vecinas, int canalPropio)
    {
        var tocan = vecinas.Where(r => SeSolapan(r.Canal, canal)).ToList();
        int maxima = tocan.Count == 0 ? 0 : tocan.Max(r => r.SenalPct);
        return new CanalOcupacion
        {
            Canal = canal,
            Banda = EsBanda24(canal) ? "2.4 GHz" : "5 GHz",
            PuntosDeAcceso = tocan.Count,
            MismoCanal = tocan.Count(r => r.Canal == canal),
            SenalPct = maxima,
            Senal = maxima > 0 ? $"{maxima} %" : "n/d",
            Propio = canal == canalPropio,
            // Los mismos umbrales que usa el diagnóstico general para avisar de
            // congestión: si acá dijera otra cosa, la app se contradiría sola.
            Estado = tocan.Count >= 4 ? Severity.Bad : tocan.Count >= 2 ? Severity.Warn : Severity.Ok
        };
    }

    /// <summary>
    /// Elige canal dentro de la banda en la que está el equipo. Si no se pudo
    /// leer el canal propio no se recomienda nada: recomendar un canal para una
    /// banda en la que el equipo quizá no esté es una respuesta que no cambia
    /// nada de lo que el usuario puede hacer.
    /// </summary>
    private static void Recomendar(AnalisisCanales analisis, List<WifiNetworkRow> vecinas, int canalPropio)
    {
        if (canalPropio <= 0) return;

        if (EsBanda24(canalPropio))
        {
            var conteos = NoSolapados24
                .Select(c => (Canal: c, Tocan: vecinas.Count(r => SeSolapan(r.Canal, c))))
                .ToList();
            int minimo = conteos.Min(x => x.Tocan);
            // Empate: quedarse donde está, si está entre los buenos. Cambiar el
            // canal del router es trabajo para el usuario y solo se justifica si
            // el cambio mejora algo.
            var elegido = conteos.FirstOrDefault(x => x.Canal == canalPropio && x.Tocan == minimo);
            if (elegido.Canal == 0) elegido = conteos.First(x => x.Tocan == minimo);

            string detalle = string.Join(", ", conteos.Select(x => $"{x.Canal}: {x.Tocan}"));
            analisis.Recomendado = elegido.Canal;
            // El texto no dice «tu canal no es de los tres buenos»: eso es falso
            // cuando el canal propio es el 6 con cuatro redes encima y conviene
            // mudarse al 11. Dice lo que sí es cierto en los dos casos.
            analisis.Motivo = elegido.Canal == canalPropio
                ? $"El canal {canalPropio} es de los tres que no se solapan entre sí (1, 6 y 11) y es el que menos redes tiene encima: {elegido.Tocan}. No hay nada que ganar cambiándolo."
                : $"Redes vecinas que tocan cada canal sin solape: {detalle}. Conviene pasar el router del {canalPropio} al {elegido.Canal}, que tiene menos redes encima.";
            return;
        }

        analisis.Recomendado = canalPropio;
        var miCanal = vecinas.Count(r => r.Canal == canalPropio);
        analisis.Motivo = miCanal == 0
            ? $"En 5 GHz los canales no se solapan entre sí: tu canal {canalPropio} está libre de otras redes."
            : $"En 5 GHz los canales no se solapan entre sí. Tu canal {canalPropio} tiene {miCanal} red(es) más; los canales de abajo muestran si hay alguno libre cerca.";
        if (miCanal > 0)
        {
            var menos = vecinas.Where(r => !EsBanda24(r.Canal)).GroupBy(r => r.Canal)
                .OrderBy(g => g.Count()).ThenBy(g => g.Key).First();
            if (menos.Key != canalPropio && menos.Count() < miCanal) analisis.Recomendado = menos.Key;
        }
    }

    private static string Nota(AnalisisCanales analisis, List<WifiNetworkRow> vecinas, int canalPropio)
    {
        if (analisis.RedesDetectadas == 0)
            return "El adaptador no devolvió redes cercanas. Puede ser un equipo por cable, un adaptador que no informa lo que ve alrededor, o el servicio de WLAN detenido.";
        if (canalPropio <= 0)
            return "No se pudo leer el canal de la conexión del equipo, así que no hay recomendación: abajo están las redes que se ven, por canal.";
        if (!EsBanda24(canalPropio) && vecinas.Count(r => !EsBanda24(r.Canal)) > 1)
            return "El escaneo de Windows no informa el ancho de canal: en 5 GHz dos redes de 80 MHz pueden compartir espectro sin compartir el número de canal, y eso no se ve acá.";
        return "";
    }

    /// <summary>
    /// Lee el canal y el nombre de la red propia de «netsh wlan show interfaces».
    /// Puro a propósito: se prueba con la salida de un equipo y con la de otro.
    /// </summary>
    public static (int Canal, string Ssid) CanalPropio(string raw)
    {
        int canal = 0;
        string ssid = "";
        foreach (string linea in (raw ?? "").Split('\n'))
        {
            int idx = linea.IndexOf(':');
            if (idx <= 0) continue;
            string clave = NetworkText.Normalizar(linea.Substring(0, idx));
            string valor = linea.Substring(idx + 1).Trim();

            if ((clave == "canal" || clave == "channel") && canal == 0) canal = NetworkText.Entero(valor);
            else if (clave == "ssid" && ssid.Length == 0 && !string.IsNullOrWhiteSpace(valor)) ssid = valor;
        }
        return (canal, ssid);
    }

    /// <summary>
    /// Medición real: el mismo escaneo que usa el diagnóstico general y el canal
    /// de la conexión. Separado del análisis para que lo que se prueba sea la
    /// lógica y no el adaptador de quien corre las pruebas.
    /// </summary>
    public static AnalisisCanales Medir(CancellationToken token = default)
    {
        string redes = AppEnv.RunConsole("netsh", "wlan show networks mode=bssid", timeoutMs: 25000, token: token);
        string interfaces = AppEnv.RunConsole("netsh", "wlan show interfaces", timeoutMs: 15000, token: token);
        var (canal, ssid) = CanalPropio(interfaces);
        var lista = NetworkModule.ParseRedesCercanas(redes);
        AppLog.Write($"Mapa de canales: {lista.Count} BSSID detectados, canal propio {canal} ({ssid})");
        return Analizar(lista, canal, ssid);
    }
}
