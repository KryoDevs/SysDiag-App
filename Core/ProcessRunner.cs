using System.Diagnostics;
using System.Text;

namespace SysDiag.Core;

/// <summary>El código de salida, stderr y el timeout forman parte del resultado, no solo stdout.</summary>
public sealed record ConsoleResult(int? ExitCode, string StandardOutput, string StandardError,
    bool TimedOut = false, string Error = "")
{
    public bool Success => !TimedOut && Error.Length == 0 && ExitCode == 0;

    public string Describe(string command)
    {
        if (TimedOut) return $"{command} superó el tiempo de espera y se detuvo.";
        if (Error.Length > 0) return $"No se pudo ejecutar {command}: {Error}";
        string detail = string.IsNullOrWhiteSpace(StandardError) ? StandardOutput : StandardError;
        detail = detail.Trim();
        if (detail.Length > 1000) detail = detail[..1000] + "…";
        return $"{command} terminó con código {ExitCode}. {detail}".Trim();
    }
}

/// <summary>
/// Drena ambas tuberías en paralelo, limita memoria y aplica el timeout también a la lectura.
/// Al cancelar o agotar el plazo mata el árbol del proceso y espera su terminación.
/// </summary>
public static class ProcessRunner
{
    private const int MaxOutputCharacters = 1024 * 1024;

    public static ProcessStartInfo CreateStartInfo(string file, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(file);
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }

    public static ConsoleResult Run(ProcessStartInfo info, int timeoutMs = 15000,
        CancellationToken token = default) =>
        RunAsync(info, TimeSpan.FromMilliseconds(timeoutMs), token).GetAwaiter().GetResult();

    public static async Task<ConsoleResult> RunAsync(ProcessStartInfo info, TimeSpan timeout,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        token.ThrowIfCancellationRequested();

        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.RedirectStandardInput = true;
        info.StandardOutputEncoding ??= Encoding.UTF8;
        info.StandardErrorEncoding ??= info.StandardOutputEncoding;

        using var process = new Process { StartInfo = info };
        try
        {
            if (!process.Start()) return new(null, "", "", Error: "El proceso no se inició.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new(null, "", "", Error: ex.Message);
        }
        // Una utilidad que solicite entrada debe recibir EOF, no quedar esperando una consola inexistente.
        process.StandardInput.Close();

        using var deadline = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        var stdout = DrainAsync(process.StandardOutput, linked.Token);
        var stderr = DrainAsync(process.StandardError, linked.Token);
        bool timedOut = false;

        try
        {
            await Task.WhenAll(process.WaitForExitAsync(linked.Token), stdout, stderr)
                .WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            timedOut = !token.IsCancellationRequested;
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* ya terminó */ }
            catch (System.ComponentModel.Win32Exception) { /* ya terminó o no permite Kill */ }

            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (TimeoutException) { /* no esperar indefinidamente durante el cierre */ }
        }

        string output = await stdout.ConfigureAwait(false);
        string errorOutput = await stderr.ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return new(process.HasExited ? process.ExitCode : null, output, errorOutput, timedOut);
    }

    private static async Task<string> DrainAsync(StreamReader reader, CancellationToken token)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
            {
                // Seguir drenando aunque ya no guardemos más: dejar de leer bloquearía al hijo.
                int keep = Math.Min(count, MaxOutputCharacters - text.Length);
                if (keep > 0) text.Append(buffer, 0, keep);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        return text.ToString();
    }
}
