using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;

namespace SysDiag.Core.Performance;

/// <summary>Una lectura de los contadores de E/S de un proceso.</summary>
public sealed class MuestraIo
{
    public ulong Leido { get; init; }
    public ulong Escrito { get; init; }
    public string Nombre { get; init; } = "";
}

/// <summary>Lo que un proceso le movió al disco durante la medición.</summary>
public sealed class ProcesoIo
{
    public int Pid { get; init; }
    public string Nombre { get; init; } = "";
    public ulong LeidoBytes { get; init; }
    public ulong EscritoBytes { get; init; }
    public double Segundos { get; init; }

    /// <summary>
    /// Conexiones de red abiertas. No son bytes: Windows no expone un contador
    /// de red por proceso sin ETW, y dibujar uno a partir de otra cosa sería
    /// inventar la medición más difícil de todas.
    /// </summary>
    public int Conexiones { get; init; }
    public string Destinos { get; init; } = "";

    public ulong TotalBytes => LeidoBytes + EscritoBytes;
    public double LecturaBytesS => Segundos > 0 ? LeidoBytes / Segundos : 0;
    public double EscrituraBytesS => Segundos > 0 ? EscritoBytes / Segundos : 0;
    public double TotalBytesS => Segundos > 0 ? TotalBytes / Segundos : 0;
}

/// <summary>
/// Las partes puras: se prueban sin Windows, que es la única forma de probarlas.
/// </summary>
public static class ProcesoIoMath
{
    /// <summary>
    /// Escala una tasa en bytes por segundo a su unidad legible. Devuelve el
    /// número y la unidad por separado —sin formatear— para que la vista elija
    /// la precisión y la cultura; mezclar las dos cosas acá haría que una
    /// prueba dependiera del idioma de la máquina.
    /// </summary>
    public static (double Valor, string Unidad) Velocidad(double bytesPorSegundo)
    {
        if (bytesPorSegundo <= 0 || double.IsNaN(bytesPorSegundo)) return (0, "B/s");

        string[] unidades = { "B/s", "KB/s", "MB/s", "GB/s" };
        double valor = bytesPorSegundo;
        int indice = 0;
        while (valor >= 1024 && indice < unidades.Length - 1)
        {
            valor /= 1024;
            indice++;
        }
        return (valor, unidades[indice]);
    }

    /// <summary>
    /// Diferencia dos muestras. Los casos que no se pueden medir se descartan en
    /// vez de reportarse: un número inventado en esta pantalla manda a matar el
    /// proceso equivocado.
    /// </summary>
    public static List<ProcesoIo> Calcular(
        IReadOnlyDictionary<int, MuestraIo> antes,
        IReadOnlyDictionary<int, MuestraIo> despues,
        IReadOnlyDictionary<int, List<string>> conexiones,
        double segundos)
    {
        var filas = new List<ProcesoIo>();
        if (segundos <= 0 || antes == null || despues == null) return filas;

        foreach (var par in despues)
        {
            // Un proceso que no existía en la primera muestra no tiene
            // referencia: medirlo daría su acumulado total como si fuera de
            // estos segundos, y siempre sería el más grande de la lista.
            if (!antes.TryGetValue(par.Key, out var inicial)) continue;

            // Un contador que bajó significa que el PID se reutilizó o que el
            // proceso se reinició. Restar igual daría un número enorme y
            // negativo envuelto: no es una medición.
            if (par.Value.Leido < inicial.Leido || par.Value.Escrito < inicial.Escrito) continue;

            ulong leido = par.Value.Leido - inicial.Leido;
            ulong escrito = par.Value.Escrito - inicial.Escrito;

            var remotas = conexiones != null && conexiones.TryGetValue(par.Key, out var lista)
                ? lista : new List<string>();
            if (leido == 0 && escrito == 0 && remotas.Count == 0) continue;

            filas.Add(new ProcesoIo
            {
                Pid = par.Key,
                Nombre = string.IsNullOrWhiteSpace(par.Value.Nombre) ? inicial.Nombre : par.Value.Nombre,
                LeidoBytes = leido,
                EscritoBytes = escrito,
                Segundos = segundos,
                Conexiones = remotas.Count,
                Destinos = ResumirDestinos(remotas)
            });
        }

        // Por movimiento total y no por operaciones: mil lecturas de 4 KB no
        // son lo mismo que mil de 4 MB, y ordenar por cantidad de operaciones
        // pondría primero al proceso más inofensivo.
        return filas.OrderByDescending(f => f.TotalBytes)
                    .ThenBy(f => f.Nombre, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
    }

    /// <summary>
    /// Nombra hasta tres destinos y cuenta el resto. Listar los cuarenta
    /// extremos de un navegador no ayuda a saber con quién habla.
    /// </summary>
    public static string ResumirDestinos(IReadOnlyList<string> remotos)
    {
        if (remotos == null || remotos.Count == 0) return "sin conexiones";

        var distintos = remotos
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (distintos.Count == 0) return "sin conexiones";
        if (distintos.Count <= 3) return string.Join(", ", distintos);
        return $"{string.Join(", ", distintos.Take(3))} y {distintos.Count - 3} más";
    }

    /// <summary>
    /// Los primeros `cuantos`, pero conservando siempre lo que se mueve: recortar
    /// la lista sin mirar dejaría afuera justo al proceso que se está buscando.
    /// </summary>
    public static List<ProcesoIo> Top(List<ProcesoIo> filas, int cuantos)
    {
        if (filas == null || cuantos <= 0) return new List<ProcesoIo>();
        var activos = filas.Where(f => f.TotalBytes > 0).ToList();
        return (activos.Count >= cuantos ? activos : filas).Take(cuantos).ToList();
    }
}

/// <summary>
/// Quién le está moviendo el disco al equipo, proceso por proceso.
///
/// CPU por proceso ya existía; sin esto, la pregunta «¿quién tiene el disco al
/// 100 %?» no tenía respuesta dentro de la app y exigía abrir el Monitor de
/// recursos. Se mide por diferencia entre dos muestras de `GetProcessIoCounters`,
/// no leyendo el acumulado: el acumulado solo premia a los procesos más
/// antiguos.
///
/// De la red se cuentan las conexiones abiertas y sus destinos, no los bytes.
/// Windows no expone un contador de red por proceso sin ETW, y esta pantalla no
/// finge tenerlo.
/// </summary>
public static class ProcessIoModule
{
    public static int SegundosMuestreo = 5;

    // ---- Contadores de E/S -------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessIoCounters(IntPtr proceso, out IoCounters contadores);

    private static Dictionary<int, MuestraIo> LeerMuestra()
    {
        var mapa = new Dictionary<int, MuestraIo>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                // Los procesos protegidos lanzan al pedir el manipulador; es
                // esperable y no se registra: uno por corrida ensuciaría el log
                // sin decir nada accionable.
                if (GetProcessIoCounters(p.Handle, out var io))
                    mapa[p.Id] = new MuestraIo
                    {
                        Leido = io.ReadTransferCount + io.OtherTransferCount,
                        Escrito = io.WriteTransferCount,
                        Nombre = p.ProcessName
                    };
            }
            catch { }
            finally { p.Dispose(); }
        }
        return mapa;
    }

    // ---- Tabla de conexiones -----------------------------------------------

    private const int AfInet = 2;
    private const int AfInet6 = 23;
    private const int ErrorInsufficientBuffer = 122;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr tabla, ref int largo, bool ordenar,
        int familia, int clase, uint reservado);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(IntPtr tabla, ref int largo, bool ordenar,
        int familia, int clase, uint reservado);

    /// <summary>
    /// Conexiones TCP activas por PID, con el extremo remoto de cada una. Es lo
    /// que permite decir «este proceso habla con este equipo de allá».
    ///
    /// Solo TCP y no UDP: un datagrama UDP no tiene extremo remoto en la tabla,
    /// así que contarlo sumaría conexiones sin poder decir hacia dónde. Prefiero
    /// una lista más corta y verdadera que una completa y a medias.
    /// </summary>
    private static Dictionary<int, List<string>> LeerConexiones()
    {
        var mapa = new Dictionary<int, List<string>>();

        //  MIB_TCPROW_OWNER_PID (24 bytes y, en este orden: estado, local,
        //  remoto y PID) y MIB_TCP6ROW_OWNER_PID (56 bytes, con el estado y el
        //  PID al final y direcciones de 16). Los desplazamientos se pasan
        //  como parámetro para que el manejo del búfer sea uno solo.
        Agregar(mapa, LeerTabla(AfInet, TcpOwnerPidAll, 24, 20, 12, 16, false));
        Agregar(mapa, LeerTabla(AfInet6, TcpOwnerPidAll, 56, 52, 24, 44, true));
        return mapa;
    }

    private const int TcpOwnerPidAll = 5;

    private static void Agregar(Dictionary<int, List<string>> destino, Dictionary<int, List<string>> origen)
    {
        foreach (var par in origen)
        {
            if (!destino.TryGetValue(par.Key, out var lista)) destino[par.Key] = lista = new List<string>();
            lista.AddRange(par.Value);
        }
    }

    /// <summary>
    /// Lee una tabla TCP con propietario. La primera llamada pregunta el tamaño
    /// del búfer y la segunda lo llena; entre medio, la tabla puede crecer, y por
    /// eso el tamaño se vuelve a pasar y se vuelve a comprobar.
    /// </summary>
    private static Dictionary<int, List<string>> LeerTabla(int familia, int clase, int filaBytes,
        int posicionPid, int posicionRemota, int posicionPuerto, bool remotoV6)
    {
        var mapa = new Dictionary<int, List<string>>();

        int largo = 0;
        uint estado = GetExtendedTcpTable(IntPtr.Zero, ref largo, false, familia, clase, 0);
        if (estado != 0 && estado != ErrorInsufficientBuffer) return mapa;
        if (largo <= 0) return mapa;

        IntPtr bufer = Marshal.AllocHGlobal(largo);
        try
        {
            if (GetExtendedTcpTable(bufer, ref largo, false, familia, clase, 0) != 0) return mapa;

            int filas = Marshal.ReadInt32(bufer);
            for (int i = 0; i < filas && (4 + (i + 1) * filaBytes) <= largo; i++)
            {
                IntPtr fila = IntPtr.Add(bufer, 4 + i * filaBytes);
                int pid = Marshal.ReadInt32(fila, posicionPid);
                int puerto = Puerto((uint)Marshal.ReadInt32(fila, posicionPuerto));

                string remoto;
                if (remotoV6)
                {
                    var bytes = new byte[16];
                    Marshal.Copy(IntPtr.Add(fila, posicionRemota), bytes, 0, 16);
                    remoto = $"[{new IPAddress(bytes)}]" + (puerto > 0 ? ":" + puerto : "");
                }
                else
                {
                    var ip = new IPAddress((long)(uint)Marshal.ReadInt32(fila, posicionRemota));
                    remoto = puerto > 0 ? $"{ip}:{puerto}" : ip.ToString();
                }

                if (!mapa.TryGetValue(pid, out var lista)) mapa[pid] = lista = new List<string>();
                lista.Add(remoto);
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("No se pudo leer la tabla de conexiones: " + ex.Message, "WARN");
        }
        finally { Marshal.FreeHGlobal(bufer); }
        return mapa;
    }

    /// <summary>Los puertos vienen en orden de red; sin esto, el 443 se lee como 49153.</summary>
    private static int Puerto(uint enOrdenDeRed) =>
        (ushort)IPAddress.NetworkToHostOrder((short)(ushort)enOrdenDeRed);

    // ---- Medición ----------------------------------------------------------

    /// <summary>
    /// Mide durante `segundos` y devuelve los procesos ordenados por movimiento
    /// de disco. Tarda lo que dura el muestreo por diseño: una diferencia de
    /// contadores instantánea no dice nada.
    /// </summary>
    public static List<ProcesoIo> Medir(int segundos = 0, CancellationToken token = default)
    {
        int espera = Math.Clamp(segundos > 0 ? segundos : SegundosMuestreo, 2, 30);
        AppLog.Write($"Consumo por proceso (muestreo de {espera} s)", "STEP");

        var antes = LeerMuestra();
        var reloj = Stopwatch.StartNew();
        if (token.WaitHandle.WaitOne(TimeSpan.FromSeconds(espera))) token.ThrowIfCancellationRequested();
        reloj.Stop();

        var despues = LeerMuestra();
        var conexiones = LeerConexiones();

        var filas = ProcesoIoMath.Calcular(antes, despues, conexiones, reloj.Elapsed.TotalSeconds);

        foreach (var f in filas.Take(8))
        {
            var (valor, unidad) = ProcesoIoMath.Velocidad(f.TotalBytesS);
            AppLog.Write($"{f.Nombre,-30} {valor.ToString("0.0", CultureInfo.CurrentCulture)} {unidad,-5} " +
                         $"{(f.Conexiones > 0 ? f.Conexiones + " conexión(es)" : "sin conexiones")}");
        }

        return filas;
    }
}
