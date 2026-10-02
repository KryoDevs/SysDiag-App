using System.Xml.Linq;

namespace SysDiag.Core.Diagnostics;

/// <summary>Un EventID no es globalmente único: la identidad siempre incluye su proveedor.</summary>
public static class SystemEventCatalog
{
    private sealed record Definition(int Id, string[] Providers, string Description);
    private static readonly Definition[] Definitions =
    {
        new(41, new[] { "Microsoft-Windows-Kernel-Power" }, "Kernel-Power 41 — reinicio sin apagado correcto"),
        new(1001, new[] { "Microsoft-Windows-WER-SystemErrorReporting", "BugCheck" }, "BugCheck — pantalla azul registrada"),
        new(6008, new[] { "EventLog" }, "Apagado inesperado"),
        new(18, new[] { "Microsoft-Windows-WHEA-Logger" }, "WHEA — error de hardware"),
        new(19, new[] { "Microsoft-Windows-WHEA-Logger" }, "WHEA — error de hardware corregido"),
        new(7, new[] { "disk", "Disk", "Microsoft-Windows-Disk" }, "Error de disco — bloque defectuoso"),
        new(11, new[] { "disk", "Disk", "Microsoft-Windows-Disk" }, "Controladora de disco — error"),
        new(51, new[] { "disk", "Disk", "Microsoft-Windows-Disk" }, "Error de paginación en disco"),
        new(129, new[] { "storahci", "stornvme", "iaStorA", "iaStorAC", "iaStorAVC", "iaStorVD", "UASPStor" }, "Reinicio de controladora de almacenamiento")
    };

    public static bool IsKnownEvent(int id, string provider) => Definitions.Any(d => d.Id == id
        && d.Providers.Contains(provider, StringComparer.OrdinalIgnoreCase));
    public static string Description(int id, string provider) => Definitions.FirstOrDefault(d => d.Id == id
        && d.Providers.Contains(provider, StringComparer.OrdinalIgnoreCase))?.Description ?? "Evento del sistema";

    public static string QueryXml(int days)
    {
        long milliseconds = (long)Math.Clamp(days, 1, 90) * 24 * 60 * 60 * 1000;
        // Windows limita la complejidad de cada XPath; dividir en Select independientes.
        var query = new XElement("Query", new XAttribute("Id", 0), new XAttribute("Path", "System"));
        foreach (var definition in Definitions)
        {
            string providers = string.Join(" or ", definition.Providers.Select(p => $"Provider[@Name='{p}']"));
            string xpath = $"*[System[EventID={definition.Id} and ({providers}) and TimeCreated[timediff(@SystemTime) <= {milliseconds}]]]";
            query.Add(new XElement("Select", new XAttribute("Path", "System"), xpath));
        }
        return new XElement("QueryList", query).ToString(SaveOptions.DisableFormatting);
    }
}
