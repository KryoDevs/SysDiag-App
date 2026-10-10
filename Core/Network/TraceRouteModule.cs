using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SysDiag.Models;

namespace SysDiag.Core.Network;

/// <summary>
/// Conclusión de la lectura de una ruta completa.
///
/// Va separada de las filas porque no pertenece a un salto: es la forma de la
/// serie lo que dice si hay pérdida real, no el número de un salto aislado.
/// </summary>
public sealed class ConclusionRuta
{
    public Severity Estado { get; init; }
    public string Texto { get; init; } = "";
}

/// <summary>
/// Estadística e interpretación de la ruta. Todo lo que se puede equivocar sin
/// una red real está acá, y por eso es lo único que se prueba.
/// </summary>
public static class TraceMath
{
    /// <summary>Umbral a partir del cual la pérdida de un salto se toma en serio por sí sola.</summary>
    public const double PerdidaGrave = 25;

    /// <summary>Un jitter por encima de esto en un salto indica cola, no distancia.</summary>
    public const double JitterConGestion = 30;

    public static HopLossRow Calcular(int salto, string direccion, IReadOnlyList<double> tiempos, int enviados)
    {
        var muestras = tiempos ?? new List<double>();
        int respondidos = muestras.Count;
        int total = enviados > 0 ? enviados : Math.Max(respondidos, 1);

        double perdida = total > 0 ? (double)(total - respondidos) / total * 100 : 0;
        double minimo = respondidos > 0 ? muestras.Min() : 0;
        double maximo = respondidos > 0 ? muestras.Max() : 0;
        double media = respondidos > 0 ? muestras.Average() : 0;
        // Con una sola muestra no hay variabilidad posible: reportar jitter 0
        // daría a entender que el enlace es estable, cuando lo que pasa es que
        // no alcanzó la medición para saberlo.
        double jitter = respondidos > 1 ? Stats.JitterRfc(muestras) : 0;

        Severity estado;
        string nota;
        if (respondidos == 0)
        {
            // Un salto que no responde no es un salto que pierde paquetes: la
            // mayoría de los routers descartan el ICMP en silencio. Acusarlo
            // sería la conclusión más injusta y la más frecuente.
            estado = Severity.Warn;
            nota = "sin respuesta: no se puede medir este salto";
        }
        else
        {
            estado = perdida >= PerdidaGrave || media > 150 ? Severity.Bad
                : perdida > 0 || media > 80 || jitter > JitterConGestion ? Severity.Warn
                : Severity.Ok;
            nota = jitter > JitterConGestion ? "jitter alto: hay cola en este tramo" : "";
        }

        return new HopLossRow
        {
            Salto = salto,
            Direccion = direccion ?? "",
            Enviados = total,
            Respondidos = respondidos,
            PerdidaPct = Math.Round(perdida, 1),
            Minimo = Math.Round(minimo, 1),
            Media = Math.Round(media, 1),
            Maximo = Math.Round(maximo, 1),
            Jitter = Math.Round(jitter, 1),
            Estado = estado,
            Nota = nota
        };
    }

    /// <summary>
    /// Lee la ruta completa y dice dónde está el problema.
    ///
    /// La regla que ordena todo lo demás: **la pérdida de un salto intermedio
    /// no es pérdida de ese tramo.** Un router que limita los ICMP que responde
    /// aparece como un salto con pérdida seguido de saltos limpios. La pérdida
    /// real se arrastra: si el salto 4 pierde, del 5 en adelante también,
    /// porque los paquetes perdidos nunca llegan más lejos.
    ///
    /// Sin esta distinción, la conclusión siempre es la cómoda —«es el
    /// proveedor»— y casi siempre es falsa.
    /// </summary>
    public static List<ConclusionRuta> Interpretar(IReadOnlyList<HopLossRow> ruta)
    {
        var conclusiones = new List<ConclusionRuta>();
        if (ruta == null || ruta.Count == 0) return conclusiones;

        var medibles = ruta.Where(h => h.Respondidos > 0).ToList();
        if (medibles.Count == 0)
        {
            conclusiones.Add(new ConclusionRuta
            {
                Estado = Severity.Warn,
                Texto = "Ningún salto respondió. Sin una sola respuesta no se puede medir pérdida: " +
                        "o la red bloquea los ICMP de camino, o el destino no responde a este tipo de tráfico."
            });
            return conclusiones;
        }

        // --- Pérdida --------------------------------------------------------
        int primeroConPerdida = ruta.FindIndex(h => h.Respondidos > 0 && h.PerdidaPct > 0);
        if (primeroConPerdida >= 0)
        {
            var siguientes = ruta.Skip(primeroConPerdida + 1).Where(h => h.Respondidos > 0).ToList();
            bool seArrastra = siguientes.Any(h => h.PerdidaPct > 0);

            if (seArrastra)
            {
                conclusiones.Add(new ConclusionRuta
                {
                    Estado = Severity.Bad,
                    Texto = $"La pérdida empieza en el salto {ruta[primeroConPerdida].Salto} " +
                            $"({ruta[primeroConPerdida].Direccion}) y sigue en los saltos siguientes: " +
                            "los paquetes que se pierden ahí no llegan más lejos, así que el problema es real y está en ese tramo o antes."
                });
                // Solo acá se dice de quién es el tramo. Con pérdida aislada no
                // hay nada que atribuir: la conclusión sería una acusación
                // contra un router que simplemente no contesta ICMP.
                conclusiones.Add(Responsable(primeroConPerdida + 1));
            }
            else
            {
                conclusiones.Add(new ConclusionRuta
                {
                    Estado = Severity.Warn,
                    Texto = $"Hay pérdida en el salto {ruta[primeroConPerdida].Salto} " +
                            $"({ruta[primeroConPerdida].Direccion}), pero los saltos siguientes responden completos. " +
                            "Eso casi nunca es pérdida de ese enlace: es el router limitando los ICMP que contesta. " +
                            "Si no hay pérdida más adelante, el camino está sano."
                });
            }
        }
        else
        {
            conclusiones.Add(new ConclusionRuta
            {
                Estado = Severity.Ok,
                Texto = $"Sin pérdida en ninguno de los {medibles.Count} saltos que respondieron."
            });
        }

        // --- Dónde se va el tiempo ------------------------------------------
        var salto = SaltoMasLento(ruta);
        if (salto != null)
            conclusiones.Add(new ConclusionRuta
            {
                Estado = salto.Value.Suma > 60 ? Severity.Warn : Severity.Ok,
                Texto = salto.Value.Suma > 60
                    ? $"El tiempo se acumula en el salto {salto.Value.Hacia}: suma {salto.Value.Suma:0} ms respecto del anterior. Ahí está el costo del enlace, no en el total."
                    : $"El tramo más caro suma {salto.Value.Suma:0} ms (hacia el salto {salto.Value.Hacia})."
            });

        // --- Cola ------------------------------------------------------------
        var conCola = ruta.FirstOrDefault(h => h.Respondidos > 1 && h.Jitter > JitterConGestion);
        if (conCola != null)
            conclusiones.Add(new ConclusionRuta
            {
                Estado = Severity.Warn,
                Texto = $"El salto {conCola.Salto} tiene {conCola.Jitter:0} ms de jitter. Eso es cola, no distancia: " +
                        "es lo que se siente como tirones en una llamada o en un juego aunque el promedio esté bien."
            });

        return conclusiones;
    }

    /// <summary>
    /// A quién corresponde el salto. El uno es el router de la casa y el dos
    /// suele ser el enlace con el proveedor: sin esta nota, cualquier pérdida
    /// «más allá del salto dos» se lee como culpa del proveedor, cuando puede
    /// estar en la red de tránsito o en el destino.
    /// </summary>
    public static ConclusionRuta Responsable(int numeroSalto) => numeroSalto switch
    {
        1 => new ConclusionRuta
        {
            Estado = Severity.Bad,
            Texto = "El problema empieza en el primer salto: es tu router o el enlace entre el equipo y él. " +
                    "Revisa el cable, el Wi-Fi y el router antes de mirar hacia afuera."
        },
        2 => new ConclusionRuta
        {
            Estado = Severity.Bad,
            Texto = "El problema empieza en el segundo salto, que suele ser el equipo de tu proveedor o el enlace hasta él. " +
                    "Con eso se puede reclamar con un dato concreto, no con una sensación."
        },
        _ => new ConclusionRuta
        {
            Estado = Severity.Warn,
            Texto = $"El problema empieza en el salto {numeroSalto}, más allá del equipo de tu proveedor: " +
                    "es la red de tránsito o el destino. Eso el proveedor no lo controla, y conviene decirlo antes de reclamar."
        }
    };

    /// <summary>El salto que más tiempo suma respecto del anterior.</summary>
    public static (int Hacia, double Suma)? SaltoMasLento(IReadOnlyList<HopLossRow> ruta)
    {
        var medibles = ruta?.Where(h => h.Respondidos > 0).ToList() ?? new List<HopLossRow>();
        // Con un solo salto medible no hay diferencia que sacar.
        if (medibles.Count < 2) return null;

        int hacia = medibles[1].Salto;
        double mayor = double.MinValue;
        for (int i = 1; i < medibles.Count; i++)
        {
            double suma = medibles[i].Media - medibles[i - 1].Media;
            if (suma > mayor) { mayor = suma; hacia = medibles[i].Salto; }
        }
        // Un salto que no suma nada no es «el más caro»: decir «suma 0 ms» no
        // es información, es ruido.
        if (mayor <= 0) return null;
        return (hacia, Math.Round(mayor, 1));
    }
}

/// <summary>
/// Ruta con pérdida y jitter por salto.
///
/// El traceroute que ya existía decía por dónde pasan los paquetes, no dónde se
/// pierden: un solo ping por salto no alcanza para medir pérdida, y medir
/// pérdida es justamente lo que localiza el tramo.
///
/// Se mide en dos fases —descubrir la ruta con un par de pings por salto y
/// después mandar la tanda completa a los saltos que responden— porque mandar
/// diez pings a un salto mudo cuesta diez segundos para descubrir que no
/// contesta.
/// </summary>
public static class TraceRouteModule
{
    public const int SaltosMaximos = 20;
    public const int MuestrasPorDefecto = 10;

    private static readonly byte[] Buffer = new byte[32];

    /// <summary>
    /// Mide la ruta hacia `objetivo`. Informa el avance por `progreso` con el
    /// número de salto que se está midiendo: sin eso, la ventana parece
    /// colgada durante los veinte segundos que puede tardar.
    /// </summary>
    public static async Task<List<HopLossRow>> MedirAsync(
        string objetivo,
        IProgress<int> progreso = null,
        CancellationToken token = default,
        int muestras = MuestrasPorDefecto,
        int saltosMaximos = SaltosMaximos)
    {
        if (string.IsNullOrWhiteSpace(objetivo)) throw new ArgumentException("Falta el destino.", nameof(objetivo));
        muestras = Math.Clamp(muestras, 3, 30);
        saltosMaximos = Math.Clamp(saltosMaximos, 1, 64);

        IPAddress destino = await ResolverAsync(objetivo, token);
        if (destino == null) throw new InvalidOperationException($"No se pudo resolver «{objetivo}».");

        AppLog.Write($"Pérdida por salto hacia {objetivo} ({destino})", "STEP");

        // --- Fase 1: descubrir la ruta --------------------------------------
        var descubiertos = await Task.WhenAll(
            Enumerable.Range(1, saltosMaximos).Select(ttl => DescubrirAsync(destino, ttl, token)));

        // Los saltos que no contestan se conservan como hueco: borrarlos haría
        // creer que la ruta tiene menos tramos de los que tiene.
        var ruta = new List<(int Salto, IPAddress Direccion)>();
        bool llego = false;
        foreach (var (ttl, direccion, alcanzo) in descubiertos)
        {
            ruta.Add((ttl, direccion));
            if (alcanzo) { llego = true; break; }
        }

        if (ruta.All(h => h.Direccion == null))
        {
            AppLog.Write("Ningún salto respondió; no hay ruta que medir.", "WARN");
            return new List<HopLossRow>();
        }

        AppLog.Write($"Ruta descubierta: {ruta.Count} salto(s) · {(llego ? "llegó al destino" : "no llegó al destino")}");

        // --- Fase 2: medir cada salto ---------------------------------------
        int hechos = 0;
        var filas = new List<HopLossRow>();
        foreach (var (salto, direccion) in ruta)
        {
            token.ThrowIfCancellationRequested();
            progreso?.Report(salto);

            if (direccion == null)
            {
                // No se mide: mandar diez pings a un salto mudo cuesta diez
                // segundos para descubrir que no contesta. Y no se le pone
                // «100 % de pérdida», porque eso no se midió.
                filas.Add(new HopLossRow
                {
                    Salto = salto,
                    Direccion = "*",
                    Nota = "sin respuesta: no se pudo medir",
                    Estado = Severity.Warn
                });
                continue;
            }

            var tiempos = await MedirSaltoAsync(destino, salto, muestras, token);
            var fila = TraceMath.Calcular(salto, direccion.ToString(), tiempos, muestras);
            filas.Add(fila);
            hechos++;

            AppLog.Write($"  {salto,2}  {direccion,-16}  {fila.PerdidaPct,5} % perdido  " +
                         $"{fila.Media,7} ms  jitter {fila.Jitter,6} ms");
        }

        // Resolución inversa, con tope: una IP sin registro PTR puede dejar al
        // DNS esperando varios segundos por salto.
        await ResolverNombresAsync(filas, token);

        AppLog.Write($"Medición terminada: {hechos} salto(s)", "OK");
        return filas;
    }

    private static async Task<IPAddress> ResolverAsync(string objetivo, CancellationToken token)
    {
        if (IPAddress.TryParse(objetivo, out var directa)) return directa;
        try
        {
            var todas = await Dns.GetHostAddressesAsync(objetivo).WaitAsync(token);
            return todas.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                ?? todas.FirstOrDefault();
        }
        catch (Exception ex)
        {
            AppLog.Write($"No se pudo resolver «{objetivo}»: {ex.Message}", "WARN");
            return null;
        }
    }

    private static async Task<(int Ttl, IPAddress Direccion, bool Llego)> DescubrirAsync(
        IPAddress destino, int ttl, CancellationToken token)
    {
        var opciones = new PingOptions(ttl, true);
        using var ping = new Ping();

        for (int intento = 0; intento < 2; intento++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var respuesta = await ping.SendPingAsync(destino, 800, Buffer, opciones).WaitAsync(token);
                if (respuesta == null) continue;
                if (respuesta.Status == IPStatus.TtlExpired)
                    return (ttl, respuesta.Address, false);
                if (respuesta.Status == IPStatus.Success)
                    return (ttl, respuesta.Address, true);
            }
            catch (OperationCanceledException) { throw; }
            catch { /* salto que no contesta: se lo deja atrás */ }
        }
        return (ttl, null, false);
    }

    private static async Task<List<double>> MedirSaltoAsync(
        IPAddress destino, int ttl, int muestras, CancellationToken token)
    {
        var opciones = new PingOptions(ttl, true);
        var tiempos = new List<double>();
        using var ping = new Ping();

        for (int i = 0; i < muestras; i++)
        {
            token.ThrowIfCancellationRequested();

            // El tiempo se mide con cronómetro propio y no con el
            // RoundtripTime que reporta .NET: ese campo no es confiable para
            // las respuestas de «TTL agotado» en Windows, y daba los «0 ms»
            // que se veían en cada salto del informe.
            var reloj = Stopwatch.StartNew();
            try
            {
                var respuesta = await ping.SendPingAsync(destino, 1000, Buffer, opciones).WaitAsync(token);
                reloj.Stop();
                if (respuesta != null
                    && (respuesta.Status == IPStatus.TtlExpired || respuesta.Status == IPStatus.Success))
                    tiempos.Add(reloj.Elapsed.TotalMilliseconds);
            }
            catch (OperationCanceledException) { throw; }
            catch { reloj.Stop(); }

            // Un intervalo corto entre muestras: sin él, diez pings salen en
            // ráfaga y miden la cola de ese instante en vez del enlace.
            try { await Task.Delay(60, token); }
            catch (OperationCanceledException) { throw; }
        }
        return tiempos;
    }

    private static async Task ResolverNombresAsync(List<HopLossRow> filas, CancellationToken token)
    {
        var tareas = filas.Select(async fila =>
        {
            try
            {
                var entrada = await Dns.GetHostEntryAsync(fila.Direccion).WaitAsync(token);
                if (!string.IsNullOrWhiteSpace(entrada.HostName)) fila.Nombre = entrada.HostName;
            }
            catch { /* sin registro PTR: se muestra la IP y ya está */ }
        });
        await Task.WhenAll(tareas);
    }
}
