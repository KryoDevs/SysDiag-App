using System.ComponentModel;

namespace SysDiag.Models;

public class StartupRow
{
    [DisplayName("Programa")] public string Nombre { get; set; } = "";
    [DisplayName("Origen")] public string Origen { get; set; } = "";
    [DisplayName("Comando")] public string Comando { get; set; } = "";
}
public class ServiceRow
{
    [DisplayName("Servicio")] public string Nombre { get; set; } = "";
    [DisplayName("Nombre interno")] public string Descripcion { get; set; } = "";
    [DisplayName("Inicio")] public string Inicio { get; set; } = "";
    [DisplayName("Estado")] public string Estado { get; set; } = "";
}
public class ProgramRow
{
    [DisplayName("Programa")] public string Nombre { get; set; } = "";
    [DisplayName("Editor")] public string Editor { get; set; } = "";
    [DisplayName("Versión")] public string Version { get; set; } = "";
    [DisplayName("Instalado")] public string Instalado { get; set; } = "";
}
