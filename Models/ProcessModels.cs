using System.ComponentModel;

namespace SysDiag.Models;

public class ProcessRow
{
    [DisplayName("Proceso")] public string Proceso { get; set; } = "";
    [DisplayName("PID")] public int Pid { get; set; }
    [DisplayName("CPU %")] public double CpuPct { get; set; }
    [DisplayName("RAM (MB)")] public double RamMb { get; set; }
    /// <summary>
    /// RAM como porcentaje de la memoria instalada. «1 200 MB» no dice si es
    /// mucho; «18 % del equipo» sí, y en un equipo de 8 GB y en uno de 64 GB
    /// el mismo número absoluto significa cosas opuestas.
    /// </summary>
    [DisplayName("RAM %")] public double RamPct { get; set; }
}
