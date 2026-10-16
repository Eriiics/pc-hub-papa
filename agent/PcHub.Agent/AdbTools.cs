using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PcHub.Agent;

/// <summary>
/// Instala la app en el teléfono por «Depuración inalámbrica» (adb por Wi-Fi), sin cable y sin tocar nada en el teléfono:
/// el teléfono enciende la depuración solo, el PC se conecta, instala la APK encima y vuelve a abrir la app.
/// Se vincula una sola vez (código de 6 dígitos de Opciones de desarrollador → Depuración inalámbrica).
/// adb viene dentro del paquete (herramientas\adb); si no, se usa el del SDK de Android.
/// </summary>
public static class AdbTools
{
    public static string Package => Program.PruebaMode ? "com.chacho.pchub.prueba" : "com.chacho.pchub";

    public static event Action<string>? Output;
    private static void Emit(string line) { Log.Info("adb: " + line); Output?.Invoke(line); }

    private static readonly SemaphoreSlim Lock = new(1, 1);

    /// <summary>adb.exe: el del paquete (junto al programa), el del SDK de Android o el del PATH.</summary>
    public static string? FindAdb()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
        {
            var p = Path.Combine(dir.FullName, "herramientas", "adb", "adb.exe");
            if (File.Exists(p)) return p;
        }
        foreach (var root in new[]
                 {
                     Environment.GetEnvironmentVariable("ANDROID_HOME"),
                     Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk"),
                 })
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            var p = Path.Combine(root, "platform-tools", "adb.exe");
            if (File.Exists(p)) return p;
        }
        foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try { var p = Path.Combine(d.Trim(), "adb.exe"); if (File.Exists(p)) return p; } catch { }
        }
        return null;
    }

    private sealed record Res(int Code, string Out);

    private static async Task<Res> RunAsync(string adb, IEnumerable<string> args, int timeoutMs = 30_000)
    {
        var psi = new ProcessStartInfo(adb)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = new Process { StartInfo = psi };
        var sb = new StringBuilder();
        p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
        try { p.Start(); }
        catch (Exception ex) { return new Res(-1, ex.Message); }
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        using var cts = new CancellationTokenSource(timeoutMs);
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch { } return new Res(-1, "adb tardó demasiado."); }
        p.WaitForExit();
        lock (sb) return new Res(p.ExitCode, sb.ToString());
    }

    /// <summary>Teléfonos listos por Wi-Fi: (serial, modelo).</summary>
    private static async Task<List<(string serial, string model)>> DevicesAsync(string adb)
    {
        var r = await RunAsync(adb, new[] { "devices", "-l" }, 15_000);
        var list = new List<(string, string)>();
        foreach (var line in r.Out.Split('\n').Select(l => l.Trim()))
        {
            if (line.Length == 0 || line.StartsWith("List of") || line.StartsWith("*")) continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[1] != "device") continue;
            var model = parts.FirstOrDefault(x => x.StartsWith("model:"))?["model:".Length..] ?? "";
            list.Add((parts[0], model));
        }
        return list;
    }

    /// <summary>Busca teléfonos con la depuración inalámbrica encendida (ya vinculados) y se conecta.</summary>
    private static async Task<bool> ConnectAsync(string adb, string? phoneIp)
    {
        // Si ya hay uno conectado, listo
        var current = await DevicesAsync(adb);
        if (current.Any(d => phoneIp == null || d.serial.StartsWith(phoneIp + ":") || d.serial.Contains("_adb-tls-connect"))) return true;

        Emit("Buscando el teléfono por Wi-Fi…");
        var services = await RunAsync(adb, new[] { "mdns", "services" }, 20_000);
        var targets = Regex.Matches(services.Out, @"_adb-tls-connect\._tcp\.?\s+(\d+\.\d+\.\d+\.\d+:\d+)")
            .Select(m => m.Groups[1].Value).Distinct().ToList();
        if (!string.IsNullOrWhiteSpace(phoneIp) && targets.Any(t => t.StartsWith(phoneIp + ":")))
            targets = targets.Where(t => t.StartsWith(phoneIp + ":")).ToList();
        foreach (var t in targets)
        {
            var r = await RunAsync(adb, new[] { "connect", t }, 20_000);
            if (r.Out.Contains("connected to", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>Vincula el teléfono (una sola vez): «Depuración inalámbrica → Vincular dispositivo con código».</summary>
    public static async Task<(bool ok, string message)> PairAsync(string hostPort, string code)
    {
        var adb = FindAdb();
        if (adb == null) return (false, "No encuentro adb. Vuelve a abrir PcHub.Agent.exe desde la carpeta del paquete y acepta actualizar.");
        var r = await RunAsync(adb, new[] { "pair", hostPort.Trim(), code.Trim() }, 40_000);
        var ok = r.Out.Contains("Successfully paired", StringComparison.OrdinalIgnoreCase);
        return ok ? (true, "Vinculado. Ahora puedes instalar la app en el teléfono por Wi-Fi.")
                  : (false, "No se pudo vincular. Revisa que el código y el puerto sean los que muestra el teléfono ahora mismo (cambian cada vez). " + r.Out.Trim());
    }

    /// <summary>
    /// Instala (encima) la APK que hay junto al programa en el teléfono y vuelve a abrir la app.
    /// <paramref name="phoneIp"/> es la IP del teléfono que lo pidió (si se sabe).
    /// </summary>
    public static async Task<(bool ok, string message)> InstallAsync(string? phoneIp)
    {
        if (!await Lock.WaitAsync(0)) return (false, "Ya se está instalando.");
        try
        {
            var apk = ApkShare.Find();
            if (apk == null) return (false, "No encuentro la app (" + ApkShare.FileName + ") junto al programa.");
            var adb = FindAdb();
            if (adb == null) return (false, "No encuentro adb. Vuelve a abrir PcHub.Agent.exe desde la carpeta del paquete y acepta actualizar.");

            if (!await ConnectAsync(adb, phoneIp))
                return (false, "El PC no encontró el teléfono por Wi-Fi. Revisa que estén en la misma red y que «Depuración inalámbrica» esté activa en Opciones de desarrollador (la primera vez hay que vincularlo desde el PC).");
            var dev = (await DevicesAsync(adb)).FirstOrDefault(d => phoneIp == null || d.serial.StartsWith(phoneIp + ":"));
            if (dev.serial == null) dev = (await DevicesAsync(adb)).FirstOrDefault();
            if (dev.serial == null) return (false, "El teléfono se desconectó. Inténtalo de nuevo.");

            Emit("Instalando la app en " + (dev.model.Length > 0 ? dev.model : dev.serial) + "…");
            var inst = await RunAsync(adb, new[] { "-s", dev.serial, "install", "-r", apk }, 180_000);
            if (!inst.Out.Contains("Success", StringComparison.OrdinalIgnoreCase))
            {
                if (inst.Out.Contains("UPDATE_INCOMPATIBLE", StringComparison.OrdinalIgnoreCase) || inst.Out.Contains("signatures do not match", StringComparison.OrdinalIgnoreCase))
                    return (false, "El teléfono tiene una copia firmada de otra forma. Desinstala PC Hub del teléfono una vez y vuelve a intentar.");
                if (inst.Out.Contains("DOWNGRADE", StringComparison.OrdinalIgnoreCase))
                    return (false, "El teléfono ya tiene una versión más nueva que la del PC.");
                return (false, "No se pudo instalar: " + Regex.Replace(inst.Out.Trim(), @"\s+", " "));
            }
            // Para que la app pueda encender sola la depuración inalámbrica la próxima vez
            await RunAsync(adb, new[] { "-s", dev.serial, "shell", "pm", "grant", Package, "android.permission.WRITE_SECURE_SETTINGS" }, 20_000);
            await RunAsync(adb, new[] { "-s", dev.serial, "shell", "monkey", "-p", Package, "-c", "android.intent.category.LAUNCHER", "1" }, 20_000);
            Emit("Listo: la app se actualizó y se volvió a abrir.");
            return (true, "La app se actualizó y se volvió a abrir.");
        }
        finally { Lock.Release(); }
    }
}
