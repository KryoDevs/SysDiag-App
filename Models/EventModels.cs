using System.ComponentModel;

namespace SysDiag.Models;

public class EventSummaryRow
{
    [DisplayName("Proveedor")] public string Origen { get; set; } = "";
    [DisplayName("ID")] public int Id { get; set; }
    [DisplayName("Significado")] public string Descripcion { get; set; } = "";
    [DisplayName("Veces")] public int Ocurrencias { get; set; }
    [DisplayName("Última vez")] public string Ultimo { get; set; } = "";
}
public class EventRow
{
    [DisplayName("Fecha")] public string Fecha { get; set; } = "";
    [DisplayName("ID")] public int Id { get; set; }
    [DisplayName("Origen")] public string Origen { get; set; } = "";
    [DisplayName("Detalle")] public string Detalle { get; set; } = "";
}
public class DumpRow
{
    [DisplayName("Archivo")] public string Archivo { get; set; } = "";
    [DisplayName("Fecha")] public string Fecha { get; set; } = "";
    [DisplayName("Tamaño")] public string Tamano { get; set; } = "";
}
