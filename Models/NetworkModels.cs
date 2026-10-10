using System.ComponentModel;

namespace SysDiag.Models;

public class LatencyResult
{
    [DisplayName("Destino")] public string Destino { get; set; } = "";
    [DisplayName("Host")] public string Host { get; set; } = "";
    [DisplayName("Enviados")] public int Enviados { get; set; }
    [DisplayName("Perdidos")] public int Perdidos { get; set; }
    [DisplayName("Pérdida %")] public double PerdidaPct { get; set; }
    [DisplayName("Mín (ms)")] public double Min { get; set; }
    [DisplayName("Media (ms)")] public double Media { get; set; }
    [DisplayName("Máx (ms)")] public double Max { get; set; }
    [DisplayName("Jitter (ms)")] public double Jitter { get; set; }
    /// <summary>
    /// Percentil 95 del retardo. Es la cifra que describe la experiencia real:
    /// un promedio de 25 ms con p95 de 180 ms se siente como lag, aunque el
    /// promedio diga que está bien. El promedio esconde los picos; el p95 los
    /// pone en la tabla.
    /// </summary>
    [DisplayName("P95 (ms)")] public double P95 { get; set; }
    /// <summary>Desviación estándar de las muestras: cuánto se dispersan alrededor del promedio.</summary>
    [DisplayName("Dispersión (ms)")] public double Desvio { get; set; }
    [Browsable(false)] public Severity Estado { get; set; }
}
public class TraceHop
{
    [DisplayName("Salto")] public int Hop { get; set; }
    [DisplayName("Dirección")] public string Direccion { get; set; } = "";
    [DisplayName("Nombre")] public string Nombre { get; set; } = "";
    [DisplayName("Media (ms)")] public string Media { get; set; } = "";
    [Browsable(false)] public Severity Estado { get; set; }
}
/// <summary>
/// Un salto medido con varias muestras, para saber dónde se pierden los
/// paquetes y no solo por dónde pasan.
///
/// `PerdidaPct` es la pérdida medida en este salto. No alcanza para culpar al
/// tramo: un router intermedio con pérdida y los saltos siguientes limpios
/// suele ser el router limitando los ICMP que responde, no un enlace que
/// pierde paquetes. La lectura correcta la hace `TraceMath.Interpretar`, que
/// mira la serie completa.
/// </summary>
public class HopLossRow
{
    [DisplayName("Salto")] public int Salto { get; set; }
    [DisplayName("Dirección")] public string Direccion { get; set; } = "";
    [DisplayName("Nombre")] public string Nombre { get; set; } = "";
    [DisplayName("Enviados")] public int Enviados { get; set; }
    [DisplayName("Respondidos")] public int Respondidos { get; set; }
    [DisplayName("Pérdida %")] public double PerdidaPct { get; set; }
    [DisplayName("Mín (ms)")] public double Minimo { get; set; }
    [DisplayName("Media (ms)")] public double Media { get; set; }
    [DisplayName("Máx (ms)")] public double Maximo { get; set; }
    [DisplayName("Jitter (ms)")] public double Jitter { get; set; }
    [DisplayName("Nota")] public string Nota { get; set; } = "";
    [Browsable(false)] public Severity Estado { get; set; }
}
public class WifiNetworkRow
{
    [DisplayName("Red")] public string Ssid { get; set; } = "";
    [DisplayName("Canal")] public int Canal { get; set; }
    [DisplayName("Banda")] public string Banda { get; set; } = "";
    [DisplayName("Señal")] public string Senal { get; set; } = "";
    [Browsable(false)] public int SenalPct { get; set; }
}
