using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using SysDiag.Models;

namespace SysDiag.Core.Hardware;

public static class SystemModule
{
    /// <summary>
    /// Cada módulo levanta el inventario antes de su propio trabajo, así que en
    /// un diagnóstico completo se pedía cuatro veces lo mismo: son unas quince
    /// consultas WMI de 50-200 ms cada una. Se guarda por unos minutos y se
    /// reutiliza; el hardware no cambia entre módulos de la misma sesión.
    /// </summary>
    private sealed class Snapshot
    {
        public DateTime Momento;
        public string Equipo;
        public List<KeyValueRow> Info;
        public List<DiskRow> Discos;
        public List<MemoryRow> Memoria;
        public List<Finding> Hallazgos;
    }

    private static Snapshot _cache;
    private static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(3);

    private static readonly object Gate = new();
    public static void Run(DiagnosticReport r, bool forzar = false)
    {
        lock (Gate) RunCore(r, forzar);
    }

    private static void RunCore(DiagnosticReport r, bool forzar)
    {
        if (!forzar && _cache != null && DateTime.Now - _cache.Momento < Vigencia)
        {
            r.Equipo = _cache.Equipo;
            r.Sistema = new(_cache.Info);
            r.Discos = new(_cache.Discos);
            r.Memoria = new(_cache.Memoria);

            // Copias, no las mismas instancias: quien las reciba les asigna
            // módulo de origen y no debe alterar lo guardado en caché.
            foreach (var f in _cache.Hallazgos)
                r.Add(f.Severity, f.Area, f.Message, f.Action, f.AccionId, "equipo");

            AppLog.Write("Inventario del equipo (en caché)", "STEP");
            return;
        }

        AppLog.Write("Inventario del equipo", "STEP");
        int hallazgosPrevios = r.Hallazgos.Count;

        var info = new List<KeyValueRow>();

        var os = Wmi.First("Win32_OperatingSystem");
        var cs = Wmi.First("Win32_ComputerSystem");
        var cpu = Wmi.First("Win32_Processor");
        var bios = Wmi.First("Win32_BIOS");

        string equipo = $"{Wmi.Str(cs, "Manufacturer")} {Wmi.Str(cs, "Model")}".Trim();
        r.Equipo = string.IsNullOrWhiteSpace(equipo) ? Environment.MachineName : equipo;

        info.Add(new KeyValueRow("Equipo", r.Equipo));
        info.Add(new KeyValueRow("Nombre de red", Environment.MachineName));
        info.Add(new KeyValueRow("Sistema", $"{Wmi.Str(os, "Caption")} {Wmi.Str(os, "OSArchitecture")}"));
        info.Add(new KeyValueRow("Compilación", Wmi.Str(os, "BuildNumber")));
        info.Add(new KeyValueRow("BIOS", Wmi.Str(bios, "SMBIOSBIOSVersion")));
        info.Add(new KeyValueRow("Procesador", Wmi.Str(cpu, "Name").Trim()));
        info.Add(new KeyValueRow("Núcleos",
            $"{Wmi.Num(cpu, "NumberOfCores")} físicos / {Wmi.Num(cpu, "NumberOfLogicalProcessors")} lógicos"));
        info.Add(new KeyValueRow("RAM total", AppEnv.FormatBytes(Wmi.Num(cs, "TotalPhysicalMemory"))));

        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        info.Add(new KeyValueRow("Tiempo encendido",
            $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m"));
        info.Add(new KeyValueRow("Último arranque",
            DateTime.Now.Subtract(uptime).ToString("yyyy-MM-dd HH:mm")));

        r.Sistema = info;

        // ---- Discos --------------------------------------------------------
        var discos = new List<DiskRow>();
        foreach (var d in Wmi.Query("SELECT * FROM Win32_LogicalDisk WHERE DriveType=3"))
        {
            double size = Wmi.Num(d, "Size");
            double free = Wmi.Num(d, "FreeSpace");
            double pct = size > 0 ? Math.Round(free / size * 100, 1) : 0;
            string unidad = Wmi.Str(d, "DeviceID");

            discos.Add(new DiskRow
            {
                Unidad = unidad,
                Etiqueta = Wmi.Str(d, "VolumeName"),
                Tamano = AppEnv.FormatBytes(size),
                Libre = AppEnv.FormatBytes(free),
                LibrePct = pct
            });

            if (pct < 10)
                r.Add(Severity.Bad, "Disco", $"La unidad {unidad} tiene solo {pct}% libre.",
                    "Windows necesita espacio para el archivo de paginación y las actualizaciones. Libera espacio cuanto antes.", modulo: "equipo");
            else if (pct < 20)
                r.Add(Severity.Warn, "Disco", $"La unidad {unidad} tiene {pct}% libre.",
                    "Conviene mantener al menos un 20% libre en el disco del sistema.", modulo: "equipo");
        }
        r.Discos = discos;

        // ---- Módulos de memoria -------------------------------------------
        var memoria = new List<MemoryRow>();
        var velocidades = new HashSet<string>();
        foreach (var m in Wmi.Query("SELECT * FROM Win32_PhysicalMemory"))
        {
            string vel = Wmi.Num(m, "ConfiguredClockSpeed").ToString("0");
            velocidades.Add(vel);
            memoria.Add(new MemoryRow
            {
                Ranura = Wmi.Str(m, "DeviceLocator"),
                Capacidad = AppEnv.FormatBytes(Wmi.Num(m, "Capacity")),
                Velocidad = $"{vel} MHz",
                Fabricante = Wmi.Str(m, "Manufacturer"),
                Parte = Wmi.Str(m, "PartNumber").Trim()
            });
        }
        r.Memoria = memoria;

        if (velocidades.Count > 1)
            r.Add(Severity.Warn, "Memoria", "Los módulos de RAM no corren a la misma frecuencia.",
                "Con módulos mixtos el sistema iguala hacia abajo. Revisa el perfil XMP/DOCP en la BIOS.", modulo: "equipo");

        foreach (var row in info)
            AppLog.Write($"{row.Clave,-18}: {row.Valor}");

        _cache = new Snapshot
        {
            Momento = DateTime.Now,
            Equipo = r.Equipo,
            Info = info,
            Discos = discos,
            Memoria = memoria,
            Hallazgos = r.Hallazgos.Skip(hallazgosPrevios).ToList()
        };
    }
}
