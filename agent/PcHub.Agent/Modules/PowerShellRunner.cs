using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PcHub.Agent.Modules;

public static class PowerShellRunner
{
    /// <summary>Si es true, se carga tu perfil de PowerShell (tus funciones y alias). Viene de config.json.</summary>
    public static bool UseProfile { get; set; }

    public static async Task<string> RunAsync(string script, int timeoutMs = 60_000)
    {
        // Forzar salida UTF-8 para que tildes y ñ lleguen bien al teléfono
        var full = "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8\n$ProgressPreference = 'SilentlyContinue'\n" + script;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));

        var psi = new ProcessStartInfo("powershell.exe",
            $"{(UseProfile ? "" : "-NoProfile ")}-NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("No se pudo iniciar PowerShell");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await p.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch { }
            return $"(PowerShell superó el tiempo límite de {timeoutMs / 1000} s y se detuvo)";
        }

        var output = (await stdout).Trim();
        var error = (await stderr).Trim();
        var result = output;
        if (error.Length > 0) result += (result.Length > 0 ? "\n" : "") + "ERROR: " + error;
        if (result.Length == 0) result = $"(sin salida, código {p.ExitCode})";
        return result.Length > 4000 ? result[..4000] + "\n…(recortado)" : result;
    }
}
