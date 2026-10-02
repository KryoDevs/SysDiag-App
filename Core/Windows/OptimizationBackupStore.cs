using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace SysDiag.Core.Windows;

public sealed class RegistrySettingSnapshot
{
    public bool Existia { get; set; }
    public int Valor { get; set; }
}

public sealed class PowerPlanSnapshot
{
    public Guid Plan { get; set; }
    public uint? WifiPowerIndex { get; set; }
    public uint? CpuMaxPercent { get; set; }
}

public sealed class DnsSnapshot
{
    public Guid Interfaz { get; set; }
    public bool Automatico { get; set; }
    public List<string> Servidores { get; set; } = new();
}

public sealed class SavedState
{
    // 0 por defecto permite identificar y rechazar respaldos antiguos sin este campo.
    public int SchemaVersion { get; set; }
    public string Fecha { get; set; } = "";
    public string Equipo { get; set; } = "";
    public string UsuarioSid { get; set; } = "";
    public Guid PlanEnergia { get; set; }
    public bool RestaurarPlanEnergia { get; set; }
    public List<PowerPlanSnapshot> Planes { get; set; } = new();
    public DnsSnapshot Dns { get; set; }
    public RegistrySettingSnapshot EfectosVisuales { get; set; }
    public RegistrySettingSnapshot GameMode { get; set; }
    public bool ReinicioRedNoReversible { get; set; }

    public void Validate(string machine, string userSid)
    {
        if (SchemaVersion != 2) throw new InvalidDataException("El respaldo es de una versión antigua o incompatible. No se aplicará automáticamente.");
        if (!string.Equals(Equipo, machine, StringComparison.OrdinalIgnoreCase) || UsuarioSid != userSid)
            throw new InvalidDataException("El respaldo pertenece a otro equipo o usuario.");
        if (PlanEnergia == Guid.Empty || !DateTime.TryParse(Fecha, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out _) || Planes == null || Planes.Count > 100)
            throw new InvalidDataException("El respaldo contiene datos de energía o fecha inválidos.");
        if (Planes.Any(p => p == null || p.Plan == Guid.Empty || p.WifiPowerIndex is > 3
                    || p.CpuMaxPercent is 0 or > 100) || Planes.Select(p => p.Plan).Distinct().Count() != Planes.Count)
            throw new InvalidDataException("El respaldo contiene índices de energía inválidos o planes duplicados.");
        if (Dns != null && (Dns.Interfaz == Guid.Empty || Dns.Servidores == null || Dns.Servidores.Count > 20
            || (!Dns.Automatico && Dns.Servidores.Count == 0)
            || (Dns.Automatico && Dns.Servidores.Count > 0)
            || Dns.Servidores.Any(s => !IPAddress.TryParse(s, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)))
            throw new InvalidDataException("El respaldo contiene una configuración DNS inválida.");
        if (EfectosVisuales is { Valor: < 0 or > 3 } || GameMode is { Valor: < 0 or > 1 })
            throw new InvalidDataException("El respaldo contiene valores de registro fuera de rango.");
    }

    public void CapturePowerPlan(Guid plan, bool wifi, bool cpu, Func<uint> readWifi, Func<uint> readCpu)
    {
        var previous = Planes.FirstOrDefault(p => p.Plan == plan);
        if (previous == null) { previous = new PowerPlanSnapshot { Plan = plan }; Planes.Add(previous); }
        // Preservar el PRIMER valor hasta que se restaure: las siguientes optimizaciones no lo pisan.
        if (wifi && !previous.WifiPowerIndex.HasValue) previous.WifiPowerIndex = readWifi();
        if (cpu && !previous.CpuMaxPercent.HasValue) previous.CpuMaxPercent = readCpu();
    }
}

/// <summary>Sin operaciones de sistema: permite probar el contrato de respaldo sin tocar el registro ni la red.</summary>
public sealed class OptimizationBackupStore
{
    private readonly string _file, _machine, _userSid;
    public OptimizationBackupStore(string file, string machine, string userSid)
    { _file = file; _machine = machine; _userSid = userSid; }

    public SavedState Read()
    {
        if (new FileInfo(_file).Length > 256 * 1024) throw new InvalidDataException("El respaldo excede el tamaño permitido.");
        var state = JsonSerializer.Deserialize<SavedState>(File.ReadAllText(_file))
            ?? throw new InvalidDataException("El respaldo está vacío.");
        state.Validate(_machine, _userSid);
        return state;
    }

    public SavedState ReadOrCreate(Guid activePlan) => File.Exists(_file) ? Read() : new SavedState
    {
        SchemaVersion = 2, Fecha = DateTime.UtcNow.ToString("O"), Equipo = _machine,
        UsuarioSid = _userSid, PlanEnergia = activePlan
    };

    public void Write(SavedState state)
    {
        state.Validate(_machine, _userSid);
        AtomicFile.WriteAllText(_file, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void CompleteRestore() => File.Move(_file, _file + ".restaurado.json", overwrite: true);
}
