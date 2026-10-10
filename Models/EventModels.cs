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
/// <summary>
/// Un volcado de memoria con su código de detención ya decodificado. Antes la
/// única recomendación era «abrilo con WinDbg», que es correcto y casi nadie
/// sigue: instalar las herramientas de depuración para leer un número convierte
/// un dato disponible en un dato perdido.
/// </summary>
public class BugcheckRow
{
    [DisplayName("Volcado")] public string Archivo { get; set; } = "";
    [DisplayName("Fecha")] public string Fecha { get; set; } = "";
    [DisplayName("Código")] public string Codigo { get; set; } = "";
    [DisplayName("Nombre")] public string Nombre { get; set; } = "";
    [DisplayName("Qué significa")] public string Significado { get; set; } = "";
    [DisplayName("Windows")] public string Sistema { get; set; } = "";
    [DisplayName("Módulos a revisar")] public string Sospechosos { get; set; } = "";
}

public class DumpRow
{
    [DisplayName("Archivo")] public string Archivo { get; set; } = "";
    [DisplayName("Fecha")] public string Fecha { get; set; } = "";
    [DisplayName("Tamaño")] public string Tamano { get; set; } = "";
}
