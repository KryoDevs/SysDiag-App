using System.Globalization;
using System.Management;

namespace SysDiag.Core;

/// <summary>Valores desconectados de WMI: no se mantienen objetos COM/ManagementObject vivos entre módulos.</summary>
public sealed class WmiRow
{
    private readonly IReadOnlyDictionary<string, object> _values;
    public WmiRow(IReadOnlyDictionary<string, object> values) => _values = values;
    public object this[string property] => _values.TryGetValue(property, out var value) ? value : null;
}

public static class Wmi
{
    private static int _accessDenied;
    public static bool LastAccessDenied => Volatile.Read(ref _accessDenied) != 0;
    public static void ResetAccessState() => Interlocked.Exchange(ref _accessDenied, 0);
    public static void MarcarAccesoDenegado() => Interlocked.Exchange(ref _accessDenied, 1);

    public static bool EsAccesoDenegado(Exception ex) => ex is UnauthorizedAccessException
        || ex is ManagementException { ErrorCode: ManagementStatus.AccessDenied }
        || ex is System.Runtime.InteropServices.COMException { HResult: unchecked((int)0x80070005) }
        || (ex?.ToString() ?? "").Contains("access denied", StringComparison.OrdinalIgnoreCase);

    // ---- Caché por corrida -------------------------------------------------
    //
    // Un diagnóstico completo consulta inventario que no cambia en segundos
    // (procesador, placa, sistema operativo) desde módulos distintos:
    // `Win32_Processor` se pide en Equipo y otra vez en Térmicas, y cada
    // `ManagementObjectSearcher` es ida y vuelta a COM. Guardar el resultado
    // con vigencia corta acorta la corrida sin cambiar ningún contrato.
    //
    // Lo que NO se cachea nunca son los contadores de rendimiento. El módulo
    // de rendimiento mide por diferencia: toma dos muestras separadas por un
    // intervalo y divide. Servirle dos veces la misma fila congelada no da un
    // valor viejo, da un cero, y un cero se lee como «disco sin actividad» en
    // el peor momento. De ahí `EsVolatil`.

    private sealed class EntradaCache
    {
        public DateTime Expira;
        public IReadOnlyList<WmiRow> Filas;
    }

    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, EntradaCache> Cache = new(StringComparer.Ordinal);
    private static TimeSpan _vigencia = TimeSpan.FromSeconds(20);
    private static bool _cacheActiva = true;

    /// <summary>Número de consultas servidas desde caché en la corrida actual. El pie lo muestra.</summary>
    public static int ConsultasReutilizadas { get; private set; }
    public static int ConsultasEjecutadas { get; private set; }

    /// <summary>
    /// Activa o desactiva la caché y fija su vigencia. Desactivada deja el
    /// comportamiento anterior, consulta a consulta, que es lo que necesitan
    /// las pruebas que comparan dos muestras tomadas a propósito.
    /// </summary>
    public static void ConfigurarCache(bool activa, TimeSpan? vigencia = null)
    {
        lock (CacheLock)
        {
            _cacheActiva = activa;
            if (vigencia.HasValue) _vigencia = vigencia.Value;
            if (!activa) Cache.Clear();
        }
    }

    /// <summary>
    /// Vacía la caché. Se llama al empezar cada corrida: la vigencia protege
    /// dentro de una corrida, pero entre dos corridas seguidas no hay nada que
    /// garantice que el inventario no cambió (un disco USB, una RAM que se
    /// reconecta).
    /// </summary>
    public static void NuevaCorrida()
    {
        lock (CacheLock)
        {
            Cache.Clear();
            ConsultasReutilizadas = 0;
            ConsultasEjecutadas = 0;
        }
    }

    private static bool EsVolatil(string query) =>
        query.Contains("Win32_Perf", StringComparison.OrdinalIgnoreCase)
        || query.Contains("PerfFormattedData", StringComparison.OrdinalIgnoreCase)
        || query.Contains("PerfRawData", StringComparison.OrdinalIgnoreCase)
        || query.Contains("MSFT_StorageReliability", StringComparison.OrdinalIgnoreCase);

    private static string ClaveCache(string query, string scope) => scope == null ? query : scope + "::" + query;

    private static bool IntentarCache(string clave, out IReadOnlyList<WmiRow> filas)
    {
        filas = null;
        if (!_cacheActiva) return false;
        lock (CacheLock)
        {
            if (!Cache.TryGetValue(clave, out var entrada)) return false;
            if (DateTime.UtcNow >= entrada.Expira) { Cache.Remove(clave); return false; }
            ConsultasReutilizadas++;
            filas = entrada.Filas;
            return true;
        }
    }

    private static void GuardarCache(string clave, IReadOnlyList<WmiRow> filas)
    {
        if (!_cacheActiva) return;
        lock (CacheLock)
        {
            // Tope duro: una caché sin límite en un proceso que corre horas
            // termina siendo una fuga con forma de optimización.
            if (Cache.Count >= 256) Cache.Clear();
            Cache[clave] = new EntradaCache { Expira = DateTime.UtcNow + _vigencia, Filas = filas };
        }
    }

    public static IReadOnlyList<WmiRow> Query(string query, string scope = null, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();

        string clave = ClaveCache(query, scope);
        if (_cacheActiva && !EsVolatil(query) && IntentarCache(clave, out var cacheadas)) return cacheadas;

        lock (CacheLock) ConsultasEjecutadas++;

        var results = new List<WmiRow>();
        try
        {
            using var searcher = scope == null ? new ManagementObjectSearcher(query)
                : new ManagementObjectSearcher(scope, query);
            searcher.Options.Timeout = TimeSpan.FromSeconds(30);
            using var collection = searcher.Get();
            foreach (ManagementObject item in collection)
            {
                using (item)
                {
                    token.ThrowIfCancellationRequested();
                    var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    foreach (PropertyData property in item.Properties)
                        values[property.Name] = property.Value;
                    results.Add(new WmiRow(values));
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Un fallo no se cachea: guardar «sin resultados» durante veinte
            // segundos convertiría un error momentáneo de WMI en la respuesta
            // oficial del resto de la corrida.
            if (EsAccesoDenegado(ex)) MarcarAccesoDenegado();
            AppLog.Write($"Consulta WMI fallida ({query}): {ex.Message}", "WARN");
            return results;
        }

        if (_cacheActiva && !EsVolatil(query)) GuardarCache(clave, results);
        return results;
    }

    public static WmiRow First(string className, CancellationToken token = default) => Query($"SELECT * FROM {className}", token: token).FirstOrDefault();
    public static string Str(WmiRow row, string property) => row?[property]?.ToString() ?? "";
    public static bool TryNum(WmiRow row, string property, out double value)
    {
        value = 0;
        if (row?[property] is not { } raw) return false;
        try { value = Convert.ToDouble(raw, CultureInfo.InvariantCulture); return double.IsFinite(value); }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) { return false; }
    }
    public static double Num(WmiRow row, string property) => TryNum(row, property, out double value) ? value : 0;
    public static bool TryBool(WmiRow row, string property, out bool value)
    {
        value = false;
        if (row?[property] is not { } raw) return false;
        try { value = Convert.ToBoolean(raw, CultureInfo.InvariantCulture); return true; }
        catch (Exception ex) when (ex is FormatException or InvalidCastException) { return false; }
    }
}
