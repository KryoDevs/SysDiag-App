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

    public static IReadOnlyList<WmiRow> Query(string query, string scope = null, CancellationToken token = default)
    {
        var results = new List<WmiRow>();
        token.ThrowIfCancellationRequested();
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
            if (EsAccesoDenegado(ex)) MarcarAccesoDenegado();
            AppLog.Write($"Consulta WMI fallida ({query}): {ex.Message}", "WARN");
        }
        return results;
    }

    public static WmiRow First(string className) => Query($"SELECT * FROM {className}").FirstOrDefault();
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
