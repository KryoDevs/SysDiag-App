using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using System.Text;

namespace SysDiag.Core;

public static class AppEnv
{
    public static string Version => typeof(AppEnv).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Documentos\SysDiag; si Documentos está bloqueado, LocalAppData\SysDiag.</summary>
    public static string OutputPath { get; } = ResolveOutputPath();
    public static string LogPath { get; } = Path.Combine(OutputPath, "logs");
    public static string BackupFile { get; } = Path.Combine(OutputPath, "estado-previo.json");
    public static int LogsMaximo = 30;

    static AppEnv() => Directory.CreateDirectory(LogPath);

    private static string ResolveOutputPath()
    {
        foreach (var folder in new[] { Environment.SpecialFolder.MyDocuments,
                     Environment.SpecialFolder.LocalApplicationData })
        {
            string root = Environment.GetFolderPath(folder);
            if (string.IsNullOrWhiteSpace(root)) continue;
            string path = Path.Combine(root, "SysDiag");
            try { Directory.CreateDirectory(path); return path; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { Debug.WriteLine($"No se puede usar {path}: {ex.Message}"); }
        }
        throw new IOException("No hay una carpeta de usuario accesible donde guardar los datos de SysDiag.");
    }

    /// <summary>Se llama DESPUÉS de cargar los ajustes y nunca elimina el registro de la sesión activa.</summary>
    public static void RotarLogs()
    {
        try
        {
            int keep = Math.Clamp(LogsMaximo, 5, 200);
            foreach (var file in new DirectoryInfo(LogPath).GetFiles("sysdiag_*.log")
                         .Where(f => !string.Equals(f.FullName, AppLog.File, StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(f => f.LastWriteTimeUtc).Skip(keep - 1))
            {
                try { file.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Debug.WriteLine($"No se pudieron rotar los registros: {ex.Message}"); }
    }

    public static bool IsAdmin
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }

    public static bool RelaunchElevated()
    {
        try
        {
            var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
            // El hijo espera a que el padre suelte el mutex antes de arrancar.
            info.ArgumentList.Add("--wait-for-parent");
            info.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            using var process = Process.Start(info);
            return process != null;
        }
        catch (Exception ex)
        {
            AppLog.Write($"No se pudo reiniciar con elevación (o se canceló UAC): {ex.Message}", "WARN");
            return false;
        }
    }

    public static string FormatBytes(double bytes)
    {
        if (!double.IsFinite(bytes) || bytes < 0) return "n/d";
        if (bytes >= 1024d * 1024 * 1024) return $"{bytes / (1024d * 1024 * 1024):N2} GB";
        if (bytes >= 1024d * 1024) return $"{bytes / (1024d * 1024):N1} MB";
        if (bytes >= 1024d) return $"{bytes / 1024d:N0} KB";
        return $"{bytes:0} B";
    }

    /// <summary>No buscar herramientas privilegiadas del sistema en el directorio de trabajo ni en PATH.</summary>
    public static string SystemTool(string name)
    {
        if (!OperatingSystem.IsWindows() || Path.IsPathRooted(name)) return name;
        string exe = Path.GetFileNameWithoutExtension(name).ToLowerInvariant();
        if (new[] { "netsh", "powercfg", "ipconfig", "net", "pnputil", "cmd", "taskmgr", "rundll32", "msiexec" }.Contains(exe))
            return Path.Combine(Environment.SystemDirectory, exe + ".exe");
        if (exe == "winget")
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WindowsApps", "winget.exe");
        return name;
    }

    public static ConsoleResult RunCommand(string file, IEnumerable<string> args, int timeoutMs = 15000,
        CancellationToken token = default)
    {
        var info = ProcessRunner.CreateStartInfo(SystemTool(file), args);
        return ProcessRunner.Run(info, timeoutMs, token);
    }

    public static void RunRequired(string file, IEnumerable<string> args, int timeoutMs = 15000,
        CancellationToken token = default)
    {
        var result = RunCommand(file, args, timeoutMs, token);
        if (result.Success) return;
        string message = result.Describe(file);
        AppLog.Write(message, "ERROR");
        throw new InvalidOperationException(message);
    }

    /// <summary>Compatibilidad para consultas de solo lectura. Un fallo nunca se entrega como datos válidos.</summary>
    public static string RunConsole(string file, string args, int timeoutMs = 15000,
        CancellationToken token = default)
    {
        var result = ProcessRunner.Run(new ProcessStartInfo(SystemTool(file), args), timeoutMs, token);
        if (result.Success) return result.StandardOutput;
        AppLog.Write(result.Describe(file), "WARN");
        return "";
    }
}

public static class AppLog
{
    private static readonly object Gate = new();
    private static readonly string LogFile = Path.Combine(AppEnv.LogPath,
        $"sysdiag_{DateTime.Now:yyyyMMdd_HHmmss_fffffff}_{Environment.ProcessId}.log");
    public static string File => LogFile;
    public static event Action<string, string> Line;

    public static void Write(string message, string level = "INFO")
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] [{level}] {message}";
        lock (Gate)
        {
            try { System.IO.File.AppendAllText(LogFile, line + Environment.NewLine, new UTF8Encoding(false)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { Debug.WriteLine($"No se pudo escribir el registro: {ex.Message}"); }
        }
        // Un suscriptor de la UI no debe romper una operación del motor ni la escritura del registro.
        if (Line is not { } handlers) return;
        foreach (Action<string, string> handler in handlers.GetInvocationList())
        {
            try { handler(line, level); }
            catch (Exception ex) { Debug.WriteLine($"Suscriptor de registro fallido: {ex.Message}"); }
        }
    }
}
