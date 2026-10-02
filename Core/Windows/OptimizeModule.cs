using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using SysDiag.Models;

namespace SysDiag.Core.Windows;

public static class OptimizeModule
{
    public class Options
    {
        // Solo aplicar lo seleccionado; las reparaciones puntuales no deben activar otros defaults.
        public bool FlushDns, FlushArp, FixWlanAutoconfig, WifiMaxPerformance, WifiPowerSave;
        public bool PublicDns, VisualEffects, HighPerformancePlan, ResetTcpStack, GameMode;
        public int? CpuMaxPercent;

        public void Validate()
        {
            if (WifiMaxPerformance && WifiPowerSave)
                throw new ArgumentException("Máximo rendimiento y ahorro Wi-Fi son opciones excluyentes.");
            if (CpuMaxPercent is < 1 or > 100)
                throw new ArgumentOutOfRangeException(nameof(CpuMaxPercent), "El máximo de CPU debe estar entre 1 y 100.");
        }
    }

    private const string VisualEffectsKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";
    private const string GameModeKey = @"Software\Microsoft\GameBar";
    private static string UserSid
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User?.Value ?? throw new InvalidOperationException("No se pudo identificar al usuario del respaldo.");
        }
    }
    private static OptimizationBackupStore Store
    {
        get
        {
            SecureBackupDirectory.Ensure();
            return new(AppEnv.BackupFile, Environment.MachineName, UserSid,
                SecureBackupDirectory.ProtectTemporaryFile, SecureBackupDirectory.VerifyFile);
        }
    }
    private static void RequireAdmin()
    {
        if (!AppEnv.IsAdmin) throw new InvalidOperationException("Esta operación necesita privilegios de administrador.");
    }

    public static string ReadWlanAutoconfig()
    {
        string raw = AppEnv.RunConsole("netsh", "wlan show settings");
        if (string.IsNullOrWhiteSpace(raw)) return "desconocido";
        if (Regex.IsMatch(raw, "deshabilitad|disabled", RegexOptions.IgnoreCase)) return "deshabilitado";
        if (Regex.IsMatch(raw, "habilitad|enabled", RegexOptions.IgnoreCase)) return "habilitado";
        return "desconocido";
    }

    public static void SaveState()
    {
        RequireAdmin();
        var store = Store;
        store.Write(store.ReadOrCreate(PowerSettings.ActivePlan()));
    }

    private static SavedState Backup(Options options, Guid active, Guid target)
    {
        var store = Store;
        var state = store.ReadOrCreate(active);
        bool wifi = options.WifiMaxPerformance || options.WifiPowerSave;
        if (wifi || options.CpuMaxPercent.HasValue)
            state.CapturePowerPlan(target, wifi, options.CpuMaxPercent.HasValue,
                () => PowerSettings.ReadAc(target, PowerSettings.WirelessSubgroup, PowerSettings.WirelessSaving),
                () => PowerSettings.ReadAc(target, PowerSettings.ProcessorSubgroup, PowerSettings.ProcessorMaximum));
        if (options.HighPerformancePlan) state.RestaurarPlanEnergia = true;
        if (options.PublicDns)
        {
            var adapter = MainInterface();
            if (state.Dns != null && state.Dns.Interfaz != Guid.Parse(adapter.Id))
                throw new InvalidOperationException("Hay DNS pendiente de restaurar de otra interfaz. Restaura primero el estado anterior.");
            state.Dns ??= ReadDns(adapter);
        }
        if (options.VisualEffects && state.EfectosVisuales == null)
            state.EfectosVisuales = ReadRegistry(VisualEffectsKey, "VisualFXSetting");
        if (options.GameMode && state.GameMode == null)
            state.GameMode = ReadRegistry(GameModeKey, "AutoGameModeEnabled");
        state.ReinicioRedNoReversible |= options.ResetTcpStack;
        // Si capturar o escribir falla, la excepción aborta ANTES del primer cambio.
        store.Write(state);
        AppLog.Write($"Estado original conservado en {AppEnv.BackupFile}", "OK");
        return state;
    }

    public static void Run(DiagnosticReport report, Options options, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        RequireAdmin();
        token.ThrowIfCancellationRequested();
        AppLog.Write("Optimización de las opciones seleccionadas", "STEP");
        Guid active = PowerSettings.ActivePlan();
        Guid target = options.HighPerformancePlan ? PowerSettings.HighPerformance : active;
        if (options.HighPerformancePlan && !PowerSettings.Plans().Contains(target))
            throw new InvalidOperationException("El plan de alto rendimiento no está disponible; no se aplicaron los ajustes seleccionados.");
        if (options.ResetTcpStack)
        {
            var point = RestorePointModule.Crear("Antes de reiniciar la pila de red");
            if (!point.Exito) throw new InvalidOperationException("No se reinició la red porque no se pudo crear un punto de restauración. " + point.Mensaje);
        }
        var previous = Backup(options, active, target);
        token.ThrowIfCancellationRequested();

        if (options.FlushDns)
        {
            AppEnv.RunRequired("ipconfig", new[] { "/flushdns" }, token: token);
            AppLog.Write("Caché DNS vaciada.", "OK");
        }
        if (options.FlushArp)
        {
            AppEnv.RunRequired("netsh", new[] { "interface", "ip", "delete", "arpcache" }, token: token);
            AppLog.Write("Caché ARP vaciada.", "OK");
        }
        if (options.FixWlanAutoconfig)
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211).ToList();
            foreach (var adapter in interfaces)
                AppEnv.RunRequired("netsh", new[] { "wlan", "set", "autoconfig", "enabled=yes", "interface=" + adapter.Name }, token: token);
            AppLog.Write(interfaces.Count == 0 ? "No hay interfaz WLAN que rehabilitar." : "Configuración automática WLAN habilitada.",
                interfaces.Count == 0 ? "WARN" : "OK");
        }
        if (options.HighPerformancePlan)
        {
            PowerSettings.Activate(target);
            AppLog.Write("Plan de alto rendimiento activado.", "OK");
        }
        if (options.WifiMaxPerformance || options.WifiPowerSave)
        {
            PowerSettings.WriteAc(target, PowerSettings.WirelessSubgroup, PowerSettings.WirelessSaving,
                options.WifiMaxPerformance ? 0u : 3u);
            AppLog.Write("Política de energía Wi-Fi aplicada para corriente alterna.", "OK");
        }
        if (options.CpuMaxPercent is int max)
        {
            PowerSettings.WriteAc(target, PowerSettings.ProcessorSubgroup, PowerSettings.ProcessorMaximum, (uint)max);
            AppLog.Write($"Estado máximo del procesador (con corriente) ajustado a {max}%.", "OK");
        }
        if (options.HighPerformancePlan || options.WifiMaxPerformance || options.WifiPowerSave || options.CpuMaxPercent.HasValue)
            PowerSettings.Activate(target);
        if (options.GameMode) WriteRegistry(GameModeKey, "AutoGameModeEnabled", new() { Existia = true, Valor = 1 });
        if (options.VisualEffects) WriteRegistry(VisualEffectsKey, "VisualFXSetting", new() { Existia = true, Valor = 2 });
        if (options.PublicDns)
        {
            var adapter = FindInterface(previous.Dns.Interfaz);
            SetDns(adapter, new() { Interfaz = Guid.Parse(adapter.Id), Servidores = new() { "1.1.1.1", "1.0.0.1" } }, token);
            AppLog.Write("DNS público aplicado. Se conserva el origen DHCP/estático y los servidores previos.", "OK");
        }
        if (options.ResetTcpStack)
        {
            AppEnv.RunRequired("netsh", new[] { "winsock", "reset" }, token: token);
            AppEnv.RunRequired("netsh", new[] { "int", "ip", "reset" }, token: token);
            report.Add(Severity.Warn, "Red", "Se reinició la pila de red. Es necesario reiniciar el equipo.",
                "El respaldo de SysDiag NO restaura IP fija, rutas ni VPN. Reconfigúralas manualmente si corresponde.");
        }
        AppLog.Write("Opciones seleccionadas aplicadas. Si hay ajustes de registro, puede hacer falta cerrar sesión.", "OK");
    }

    public static string Restore()
    {
        RequireAdmin();
        if (!File.Exists(AppEnv.BackupFile)) return "No hay respaldo protegido pendiente. Los antiguos respaldos en Documentos no se importan automáticamente; revisa los ajustes de aquella versión manualmente.";
        var store = Store;
        var state = store.Read();
        var available = PowerSettings.Plans();
        if ((state.RestaurarPlanEnergia && !available.Contains(state.PlanEnergia)) || state.Planes.Any(p => !available.Contains(p.Plan)))
            throw new InvalidDataException("Un plan respaldado ya no existe. No se aplicará una restauración incompleta.");
        var dnsAdapter = state.Dns == null ? null : FindInterface(state.Dns.Interfaz);
        Guid current = PowerSettings.ActivePlan();
        AppLog.Write($"Restaurando ajustes originales del {state.Fecha}", "STEP");
        foreach (var plan in state.Planes)
        {
            if (plan.WifiPowerIndex is uint wifi)
                PowerSettings.WriteAc(plan.Plan, PowerSettings.WirelessSubgroup, PowerSettings.WirelessSaving, wifi);
            if (plan.CpuMaxPercent is uint cpu)
                PowerSettings.WriteAc(plan.Plan, PowerSettings.ProcessorSubgroup, PowerSettings.ProcessorMaximum, cpu);
        }
        if (state.Dns != null) SetDns(dnsAdapter!, state.Dns);
        if (state.EfectosVisuales != null) WriteRegistry(VisualEffectsKey, "VisualFXSetting", state.EfectosVisuales);
        if (state.GameMode != null) WriteRegistry(GameModeKey, "AutoGameModeEnabled", state.GameMode);
        // Activar al FINAL; nunca escribir índices de otro plan sobre SCHEME_CURRENT.
        PowerSettings.Activate(state.RestaurarPlanEnergia ? state.PlanEnergia : current);
        store.CompleteRestore();
        string warning = state.ReinicioRedNoReversible
            ? "\n\nEl reinicio de TCP/IP, las IP fijas, rutas y VPN NO se revierten mediante este respaldo."
            : "";
        return $"Ajustes respaldados del {state.Fecha} restaurados. WLAN automático se mantiene habilitado por seguridad." + warning;
    }

    private static NetworkInterface MainInterface() => NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n =>
        n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
        && n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork
            && g.Address.ToString() != "0.0.0.0"))
        ?? throw new InvalidOperationException("No se identificó una interfaz IPv4 principal; no se cambiará DNS.");

    private static NetworkInterface FindInterface(Guid id) => NetworkInterface.GetAllNetworkInterfaces()
        .FirstOrDefault(n => Guid.TryParse(n.Id, out var value) && value == id)
        ?? throw new InvalidDataException("La interfaz DNS del respaldo ya no existe.");

    private static DnsSnapshot ReadDns(NetworkInterface adapter)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\" + Guid.Parse(adapter.Id).ToString("B"))
            ?? throw new IOException("No se pudo leer el origen DHCP/estático del DNS; el cambio se abortó.");
        string configured = key.GetValue("NameServer") as string ?? "";
        return new DnsSnapshot
        {
            Interfaz = Guid.Parse(adapter.Id), Automatico = string.IsNullOrWhiteSpace(configured),
            Servidores = configured.Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToList()
        };
    }

    private static void SetDns(NetworkInterface adapter, DnsSnapshot state, CancellationToken token = default)
    {
        string name = "name=" + (adapter.GetIPProperties().GetIPv4Properties()?.Index
            ?? throw new InvalidDataException("La interfaz no tiene configuración IPv4."));
        if (state.Automatico)
            AppEnv.RunRequired("netsh", new[] { "interface", "ipv4", "set", "dnsservers", name, "source=dhcp" }, token: token);
        else
        {
            AppEnv.RunRequired("netsh", new[] { "interface", "ipv4", "set", "dnsservers", name, "source=static", "address=" + state.Servidores[0], "validate=no" }, token: token);
            for (int index = 1; index < state.Servidores.Count; index++)
                AppEnv.RunRequired("netsh", new[] { "interface", "ipv4", "add", "dnsservers", name, "address=" + state.Servidores[index], "index=" + (index + 1), "validate=no" }, token: token);
        }
        AppEnv.RunRequired("ipconfig", new[] { "/flushdns" }, token: token);
    }

    private static RegistrySettingSnapshot ReadRegistry(string keyPath, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        object value = key?.GetValue(name);
        if (value != null && value is not int) throw new InvalidDataException($"El valor {name} no es un DWORD válido.");
        return new() { Existia = value != null, Valor = value is int number ? number : 0 };
    }

    private static void WriteRegistry(string keyPath, string name, RegistrySettingSnapshot snapshot)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath)
            ?? throw new IOException($"No se pudo abrir {keyPath} para escribir.");
        if (snapshot.Existia) key.SetValue(name, snapshot.Valor, RegistryValueKind.DWord);
        else key.DeleteValue(name, throwOnMissingValue: false);
    }
}
