using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SysDiag.Models;

namespace SysDiag.Core.Network;

public static class NetworkModule
{
    public static int PingCount = 20;

    /// <summary>
    /// Los mismos destinos que usa el diagnóstico de red, expuestos aparte
    /// para que el monitor en vivo pueda ofrecer la misma lista sin
    /// duplicar la lógica de qué está disponible.
    /// </summary>
    public static List<(string Host, string Label)> ObjetivosDisponibles()
    {
        var objetivos = new List<(string Host, string Label)>();

        string gw = DefaultGateway();
        if (!string.IsNullOrEmpty(gw)) objetivos.Add((gw, "Router / gateway"));

        objetivos.Add(("1.1.1.1", "Salida a internet"));

        // Medir contra los servidores de un juego específico solo tiene
        // sentido si ese juego está instalado. Para cualquiera que no
        // juegue League of Legends sería un chequeo de red irrelevante, y
        // de paso revelaría qué tiene instalado sin que lo haya pedido.
        if (LeagueOfLegendsInstalado())
        {
            // Servidores regionales de chat de Riot: sí resuelven por DNS y
            // sirven como referencia de distancia a su infraestructura
            // LATAM. Los hosts de juego reales de Riot Direct no son
            // pingables ni rastreables.
            objetivos.Add(("la1.chat.si.riotgames.com", "Riot LAN (Latam Norte)"));
            objetivos.Add(("la2.chat.si.riotgames.com", "Riot LAS (Latam Sur)"));
        }

        return objetivos;
    }

    /// <summary>
    /// Un solo ping, para el monitor en vivo — a diferencia de MeasureAsync,
    /// que manda un lote completo y devuelve el promedio, esto es lo que
    /// hace falta para ir agregando un punto por vez a un gráfico que se
    /// actualiza solo.
    /// </summary>
    public static async Task<double?> PingUnaVez(string host, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, 1200).WaitAsync(token);
            return reply?.Status == IPStatus.Success ? reply.RoundtripTime : (double?)null;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return null;
        }
    }

    public static async Task RunAsync(DiagnosticReport r, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        AppLog.Write("Calidad de red", "STEP");

        ReadWifi(r, token);
        ScanChannels(r, token);

        var objetivos = ObjetivosDisponibles();

        // Los destinos se miden en paralelo. En serie esto tardaba tantos
        // segundos como destinos por muestras; ahora el total lo marca el
        // destino más lento, no la suma de todos.
        var medidas = await Task.WhenAll(
            objetivos.Select(o => MeasureAsync(o.Host, o.Label, PingCount, token)));

        var resultados = medidas.Where(x => x != null).ToList();
        r.Red = resultados;

        Analyze(r, resultados);

        var destino = resultados
            .Where(x => x.Destino.StartsWith("Riot") && x.Media > 0)
            .OrderBy(x => x.Media)
            .FirstOrDefault()
            ?? resultados.FirstOrDefault(x => x.Destino == "Salida a internet");

        if (destino != null)
        {
            r.TracerouteDestino = destino.Destino;
            r.Traceroute = await TracerouteAsync(destino.Host, token);
        }
    }

    /// <summary>
    /// Mide RTT, jitter y pérdida con ICMP. El jitter es la media de las
    /// diferencias absolutas entre muestras consecutivas: es lo que se percibe
    /// como tirón, aunque el ping medio sea bajo. Las muestras de un mismo
    /// destino van en serie a propósito, porque medir variación exige
    /// espaciarlas en el tiempo.
    /// </summary>
    public static async Task<LatencyResult> MeasureAsync(
        string target, string label, int count, CancellationToken token)
    {
        if (count is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(count));
        token.ThrowIfCancellationRequested();
        IPAddress ip = await ResolveAsync(target, token);
        if (ip == null)
        {
            AppLog.Write($"{label} ({target}): no resuelve, se omite.", "WARN");
            return null;
        }

        var rtts = new List<double>();
        int perdidos = 0;

        // Una instancia de Ping no admite operaciones simultáneas, así que
        // cada destino usa la suya.
        using (var ping = new Ping())
        {
            for (int i = 0; i < count; i++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var reply = await ping.SendPingAsync(ip, 1000).WaitAsync(token);
                    if (reply != null && reply.Status == IPStatus.Success)
                        rtts.Add(reply.RoundtripTime);
                    else
                        perdidos++;
                }
                catch (OperationCanceledException) { throw; }
                catch { perdidos++; }

                await Task.Delay(150, token);
            }
        }

        var res = new LatencyResult
        {
            Destino = label,
            Host = target,
            Enviados = count,
            Perdidos = perdidos,
            PerdidaPct = Math.Round((double)perdidos / count * 100, 1)
        };

        if (rtts.Count == 0)
        {
            res.Estado = Severity.Warn;
            AppLog.Write($"{label} ({target}): no responde a ICMP; puede estar filtrado, no demuestra que el servicio esté caído.", "WARN");
            return res;
        }

        // Jitter como media de las diferencias absolutas entre muestras
        // consecutivas: es lo que se percibe como tirón, es fácil de explicar
        // y coincide con lo que muestran las herramientas conocidas.
        double jitterSum = 0;
        for (int i = 1; i < rtts.Count; i++) jitterSum += Math.Abs(rtts[i] - rtts[i - 1]);

        res.Min = Math.Round(rtts.Min(), 1);
        res.Max = Math.Round(rtts.Max(), 1);
        res.Media = Math.Round(rtts.Average(), 1);
        res.Jitter = rtts.Count > 1 ? Math.Round(jitterSum / (rtts.Count - 1), 1) : 0;

        // Media y máximo no alcanzan: un destino con promedio de 25 ms y p95 de
        // 180 ms se siente mal aunque la media diga que está bien. Sin estas dos
        // columnas, la tabla no puede explicar esa diferencia.
        res.P95 = Math.Round(Stats.Percentil(rtts, 0.95), 1);
        res.Desvio = Math.Round(Stats.Desviacion(rtts), 1);

        if (res.Media > 120 || res.Jitter > 30 || res.PerdidaPct > 2) res.Estado = Severity.Bad;
        else if (res.Media > 70 || res.Jitter > 15 || res.PerdidaPct > 0) res.Estado = Severity.Warn;
        else res.Estado = Severity.Ok;

        string nivel = res.Estado == Severity.Ok ? "OK" : res.Estado == Severity.Warn ? "WARN" : "ERROR";
        AppLog.Write(
            $"{label,-24} media {res.Media,6} ms   p95 {res.P95,6} ms   jitter {res.Jitter,5} ms   " +
            $"dispersión {res.Desvio,5} ms   pérdida {res.PerdidaPct,5}%",
            nivel);

        // Un p95 muy por encima de la media es la firma de una red con
        // microcortes: el promedio aprueba y la experiencia reprueba. Se dice
        // acá porque es el único sitio donde las dos cifras están juntas.
        if (res.Media > 0 && res.P95 > res.Media * 2.5 && res.P95 - res.Media > 30)
            AppLog.Write($"{label}: p95 muy por encima de la media ({res.Media} vs {res.P95} ms): " +
                         "picos de latencia que el promedio no muestra.", "WARN");

        return res;
    }

    /// <summary>
    /// Traceroute: ICMP con TTL creciente para ver en qué salto responde cada
    /// router intermedio. Los saltos se lanzan todos a la vez —igual que mtr—
    /// en vez de esperar uno por uno: con saltos que no responden, en serie el
    /// peor caso eran más de ochenta segundos.
    /// </summary>
    public static async Task<List<TraceHop>> TracerouteAsync(
        string target, CancellationToken token, int maxHops = 24)
    {
        if (maxHops is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(maxHops));
        token.ThrowIfCancellationRequested();
        var vacio = new List<TraceHop>();

        IPAddress destino = await ResolveAsync(target, token);
        if (destino == null) return vacio;

        AppLog.Write($"Traceroute hacia {target} ({destino})", "STEP");

        var saltos = await Task.WhenAll(
            Enumerable.Range(1, maxHops).Select(ttl => HopAsync(destino, ttl, token)));

        // Se corta en el primer salto que alcanzó el destino: lo que venga
        // después son respuestas del mismo host con TTL de sobra.
        var ruta = new List<TraceHop>();
        foreach (var (hop, llego) in saltos)
        {
            ruta.Add(hop);
            if (llego) break;
        }

        // Resolución inversa en paralelo y con tope de tiempo: una IP sin
        // registro PTR puede tener al DNS esperando varios segundos.
        await Task.WhenAll(ruta.Where(h => h.Direccion != "*").Select(h => ResolveNameAsync(h, token)));

        foreach (var h in ruta)
            AppLog.Write($"  {h.Hop,2}  {h.Direccion,-16}  {h.Media,-14}  {h.Nombre}");

        return ruta;
    }

    private static async Task<(TraceHop Hop, bool Llego)> HopAsync(
        IPAddress destino, int ttl, CancellationToken token)
    {
        var opciones = new PingOptions(ttl, true);
        byte[] buffer = new byte[32];

        var tiempos = new List<double>();
        IPAddress origen = null;
        bool llego = false;

        using (var ping = new Ping())
        {
            for (int intento = 0; intento < 2; intento++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    // RoundtripTime que reporta .NET no es confiable para
                    // respuestas de "TTL agotado" en Windows —el motivo de
                    // los "0 ms" en cada salto que se veían en el informe—,
                    // así que el tiempo se mide acá con un cronómetro propio
                    // en vez de leer ese campo.
                    var cronometro = System.Diagnostics.Stopwatch.StartNew();
                    var reply = await ping.SendPingAsync(destino, 1200, buffer, opciones).WaitAsync(token);
                    cronometro.Stop();
                    if (reply == null) continue;

                    if (reply.Status == IPStatus.TtlExpired || reply.Status == IPStatus.Success)
                    {
                        origen ??= reply.Address;
                        tiempos.Add(cronometro.Elapsed.TotalMilliseconds);
                        if (reply.Status == IPStatus.Success) llego = true;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { /* salto sin respuesta */ }
            }
        }

        if (origen == null)
        {
            return (new TraceHop
            {
                Hop = ttl,
                Direccion = "*",
                Nombre = "sin respuesta",
                Media = "-",
                Estado = Severity.Warn
            }, false);
        }

        double media = tiempos.Count > 0 ? Math.Round(tiempos.Average(), 1) : 0;

        return (new TraceHop
        {
            Hop = ttl,
            Direccion = origen.ToString(),
            Media = tiempos.Count > 0 ? $"{media} ms" : "sin respuesta",
            Estado = media > 150 ? Severity.Bad : media > 80 ? Severity.Warn : Severity.Ok
        }, llego);
    }

    private static async Task<IPAddress> ResolveAsync(string host, CancellationToken token)
    {
        if (IPAddress.TryParse(host, out var directa)) return directa;

        try
        {
            var direcciones = await Dns.GetHostAddressesAsync(host, token)
                                       .WaitAsync(TimeSpan.FromSeconds(4), token);
            return direcciones.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return null;
        }
    }

    private static async Task ResolveNameAsync(TraceHop hop, CancellationToken token)
    {
        try
        {
            var entry = await Dns.GetHostEntryAsync(hop.Direccion, token)
                                 .WaitAsync(TimeSpan.FromSeconds(2), token);
            hop.Nombre = entry.HostName;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            hop.Nombre = "";
        }
    }

    private static void Analyze(DiagnosticReport r, List<LatencyResult> resultados)
    {
        foreach (var result in resultados.Where(x => x.PerdidaPct >= 100))
            r.Add(Severity.Warn, "Red", $"{result.Destino} no respondió a ICMP.",
                "Puede bloquear ping. No equivale por sí solo a una caída de internet; contrasta con otra prueba y otro destino.");
        var local = resultados.FirstOrDefault(x => x.Destino.StartsWith("Router"));
        var remoto = resultados.FirstOrDefault(x => x.Destino == "Salida a internet");

        if (local != null && local.Media > 10)
        {
            r.Add(Severity.Bad, "Red", $"El salto hasta el router ya son {local.Media} ms.",
                "El problema está dentro de tu casa, no en el proveedor: enlace inalámbrico débil, saturación del router o interferencia.");
        }
        else if (local != null && local.Jitter > 8)
        {
            r.Add(Severity.Warn, "Red", $"Jitter de {local.Jitter} ms hacia el propio router.",
                "Inestabilidad en el último tramo. Revisa banda, canal y distancia al punto de acceso.");
        }
        else if (remoto != null && remoto.Media > 80)
        {
            r.Add(Severity.Warn, "Red", "La red local está bien pero la latencia a internet es alta.",
                "El cuello de botella está aguas arriba. Ninguna optimización en el equipo va a cambiarlo.");
        }

        var riot = resultados
            .Where(x => x.Destino.StartsWith("Riot") && x.Media > 0)
            .OrderBy(x => x.Media)
            .FirstOrDefault();

        if (riot != null)
        {
            AppLog.Write($"Servidor Riot más cercano: {riot.Destino} ({riot.Media} ms)");
            if (riot.Jitter > 15)
                r.Add(Severity.Bad, "Juego", $"Jitter de {riot.Jitter} ms hacia {riot.Destino}.",
                    "El jitter alto es lo que produce los tirones y el retroceso de personaje, incluso con ping medio bajo.");
            else if (riot.Estado == Severity.Ok)
                r.Add(Severity.Ok, "Juego", $"Enlace estable hacia {riot.Destino} ({riot.Media} ms).");
        }
    }

    /// <summary>
    /// Carpeta de instalación estándar del cliente. Es una comprobación
    /// simple a propósito: no vale la pena consultar el registro de
    /// programas instalados solo para esto cuando la ruta de instalación es
    /// consistente y ya se referencia en otro lado de la app (limpieza de
    /// registros de este mismo juego).
    /// </summary>
    private static bool LeagueOfLegendsInstalado()
    {
        try
        {
            return System.IO.Directory.Exists(@"C:\Riot Games\League of Legends");
        }
        catch
        {
            return false;
        }
    }

    private static string DefaultGateway()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var g in ni.GetIPProperties().GatewayAddresses)
                {
                    if (g?.Address == null) continue;
                    if (g.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (g.Address.ToString() == "0.0.0.0") continue;
                    return g.Address.ToString();
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Write($"No se pudo determinar la puerta de enlace: {ex.Message}", "WARN");
        }
        return null;
    }

    /// <summary>
    /// netsh devuelve texto localizado, así que normalizamos las etiquetas
    /// (sin acentos, en minúsculas) y aceptamos español e inglés.
    /// </summary>
    private static void ReadWifi(DiagnosticReport r, CancellationToken token)
    {
        string raw = AppEnv.RunConsole("netsh", "wlan show interfaces", token: token);
        if (string.IsNullOrWhiteSpace(raw))
        {
            AppLog.Write("Sin adaptador Wi-Fi activo (o conexión por cable).");
            return;
        }

        var mapa = new Dictionary<string, string>
        {
            ["ssid"] = "SSID",
            ["bssid"] = "BSSID",
            ["estado"] = "Estado",
            ["state"] = "Estado",
            ["senal"] = "Señal",
            ["signal"] = "Señal",
            ["canal"] = "Canal",
            ["channel"] = "Canal",
            ["tipo de radio"] = "Tipo de radio",
            ["radio type"] = "Tipo de radio",
            ["banda"] = "Banda",
            ["band"] = "Banda",
            ["autenticacion"] = "Autenticación",
            ["authentication"] = "Autenticación",
            ["velocidad de recepcion (mbps)"] = "Recepción (Mbps)",
            ["receive rate (mbps)"] = "Recepción (Mbps)",
            ["velocidad de transmision (mbps)"] = "Transmisión (Mbps)",
            ["transmit rate (mbps)"] = "Transmisión (Mbps)"
        };

        var datos = new List<KeyValueRow>();
        var vistos = new HashSet<string>();

        foreach (string linea in raw.Split('\n'))
        {
            int idx = linea.IndexOf(':');
            if (idx <= 0) continue;

            string clave = Normalize(linea.Substring(0, idx));
            string valor = linea.Substring(idx + 1).Trim();

            if (mapa.TryGetValue(clave, out string nombre) && vistos.Add(nombre))
                datos.Add(new KeyValueRow(nombre, valor));
        }

        if (datos.Count == 0) return;

        int canal = ParseInt(datos.FirstOrDefault(x => x.Clave == "Canal")?.Valor);
        int senal = ParseInt(datos.FirstOrDefault(x => x.Clave == "Señal")?.Valor);

        if (!vistos.Contains("Banda") && canal > 0)
            datos.Add(new KeyValueRow("Banda", canal <= 14 ? "2.4 GHz" : "5 GHz"));

        if (senal > 0)
        {
            // Windows publica calidad 0-100%; la escala equivale linealmente a -100..-50 dBm.
            datos.Add(new KeyValueRow("RSSI aproximado", $"{Math.Round(senal / 2.0 - 100)} dBm"));

            if (senal < 40)
                r.Add(Severity.Bad, "Wi-Fi", $"Señal baja ({senal}%).",
                    "Por debajo del 40% el enlace renegocia constantemente y eso se traduce en picos de ping. Acércate al punto de acceso o pasa a cable.");
            else if (senal < 65)
                r.Add(Severity.Warn, "Wi-Fi", $"Señal media ({senal}%).",
                    "Suficiente para navegar, justo para juego competitivo.");
        }

        string banda = datos.FirstOrDefault(d => d.Clave == "Banda")?.Valor?.Replace(',', '.') ?? "";
        if (canal > 0 && banda.Contains("2.4", StringComparison.OrdinalIgnoreCase))
            r.Add(Severity.Warn, "Wi-Fi", $"Conectado en 2.4 GHz (canal {canal}).",
                "Esa banda comparte espectro con microondas, Bluetooth y las redes vecinas. Si el equipo y el router soportan 5 GHz, cambia de banda.");

        foreach (var d in datos) AppLog.Write($"{d.Clave,-20}: {d.Valor}");
        r.WiFi = datos;
    }

    /// <summary>
    /// Escaneo de las redes vecinas para medir congestión de canal. En 5 GHz
    /// los canales no se solapan, así que basta contar cuántas comparten el
    /// mismo; en 2.4 GHz además se solapan entre sí, y por eso ahí cualquier
    /// vecino cuenta como interferencia.
    /// </summary>
    private static void ScanChannels(DiagnosticReport r, CancellationToken token)
    {
        string raw = AppEnv.RunConsole("netsh", "wlan show networks mode=bssid", token: token);
        if (string.IsNullOrWhiteSpace(raw)) return;

        var redes = ParseRedesCercanas(raw);
        if (redes.Count == 0) return;

        r.RedesCercanas = redes.OrderByDescending(x => x.SenalPct).ToList();
        AppLog.Write($"Redes detectadas alrededor: {redes.Count}");

        int propio = ParseInt(r.WiFi.FirstOrDefault(x => x.Clave == "Canal")?.Valor);
        if (propio <= 0) return;

        var comparten = redes.Where(x => x.Canal == propio).ToList();
        // La red propia también aparece en el escaneo: no cuenta como vecina.
        string miSsid = r.WiFi.FirstOrDefault(x => x.Clave == "SSID")?.Valor;
        int vecinas = comparten.Count(x => x.Ssid != miSsid);

        AppLog.Write($"Canal {propio}: {vecinas} red(es) vecina(s) compartiendo");

        if (vecinas >= 4)
            r.Add(Severity.Bad, "Wi-Fi", $"{vecinas} redes comparten tu canal ({propio}).",
                "Todas se turnan para transmitir en el mismo espectro, y eso se traduce en picos de latencia. Cambia el canal del router a uno libre — en Datos ▸ Redes cercanas puedes ver cuáles están menos ocupados.");
        else if (vecinas >= 2)
            r.Add(Severity.Warn, "Wi-Fi", $"{vecinas} redes comparten tu canal ({propio}).",
                "Congestión moderada. Si notas picos de ping, probar otro canal es lo más barato que puedes hacer.");
    }

    /// <summary>
    /// Lee la salida de «netsh wlan show networks mode=bssid». Cada BSSID se arma con sus propias líneas
    /// de señal y canal: netsh no las imprime siempre en el mismo orden, y leerlas en secuencia asignaba a
    /// un punto de acceso la señal del anterior.
    /// </summary>
    public static List<WifiNetworkRow> ParseRedesCercanas(string raw)
    {
        var redes = new List<WifiNetworkRow>();
        if (string.IsNullOrWhiteSpace(raw)) return redes;

        string ssid = null;
        bool enBssid = false;
        int senal = 0, canal = 0;

        // Cierra el BSSID en curso: solo se publica si tiene canal conocido.
        void Cerrar()
        {
            if (enBssid && ssid != null && canal > 0)
            {
                redes.Add(new WifiNetworkRow
                {
                    Ssid = ssid,
                    Canal = canal,
                    Banda = canal <= 14 ? "2.4 GHz" : "5 GHz",
                    Senal = senal > 0 ? $"{senal} %" : "n/d",
                    SenalPct = senal
                });
            }
            enBssid = false;
            senal = 0;
            canal = 0;
        }

        foreach (string linea in raw.Split('\n'))
        {
            int idx = linea.IndexOf(':');
            if (idx <= 0) continue;

            string clave = Normalize(linea.Substring(0, idx));
            string valor = linea.Substring(idx + 1).Trim();

            if (clave.StartsWith("ssid", StringComparison.Ordinal) && !clave.StartsWith("bssid", StringComparison.Ordinal))
            {
                Cerrar();
                ssid = string.IsNullOrWhiteSpace(valor) ? "(oculta)" : valor;
            }
            else if (clave.StartsWith("bssid", StringComparison.Ordinal))
            {
                Cerrar();
                enBssid = true;
            }
            else if (enBssid && (clave == "senal" || clave == "signal"))
            {
                senal = ParseInt(valor);
            }
            else if (enBssid && (clave == "canal" || clave == "channel"))
            {
                canal = ParseInt(valor);
            }
        }

        Cerrar();
        return redes;
    }

    private static int ParseInt(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        var m = Regex.Match(s, @"\d+");
        return m.Success && int.TryParse(m.Value, out int v) ? v : 0;
    }

    private static string Normalize(string s)
    {
        s = s.Trim().ToLowerInvariant();
        s = s.Replace('á', 'a').Replace('é', 'e').Replace('í', 'i')
             .Replace('ó', 'o').Replace('ú', 'u').Replace('ñ', 'n');
        return s;
    }
}
