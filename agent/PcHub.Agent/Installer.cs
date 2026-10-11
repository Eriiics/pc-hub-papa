using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PcHub.Agent;

/// <summary>
/// Instalación como programa normal de Windows, sin permisos de administrador:
///  - Archivos en %LOCALAPPDATA%\Programs\PcHub
///  - Acceso directo en el menú Inicio
///  - Entrada en Configuración → Aplicaciones (para desinstalar)
///  - Opción de iniciar con Windows (HKCU\...\Run)
/// La configuración (%APPDATA%\PcHub) no se toca al actualizar ni al desinstalar.
/// </summary>
public static class Installer
{
    public const string AppName = "PC Hub";
    private const string ExeName = "PcHub.Agent.exe";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\PcHub";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "PcHub";

    public static string InstallDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "PcHub");

    public static string InstalledExe => Path.Combine(InstallDir, ExeName);

    private static string StartMenuShortcut =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");

    /// <summary>¿Es una carpeta publicada (con todo lo necesario) y no una compilación de desarrollo?</summary>
    public static bool IsPublishedBuild =>
        !AppContext.BaseDirectory.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase)
        && !AppContext.BaseDirectory.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase);

    public static bool IsInstalled => File.Exists(InstalledExe);

    /// <summary>Versión de la copia ya instalada, o null si no hay.</summary>
    public static Version? InstalledVersion
    {
        get
        {
            try { return IsInstalled && Version.TryParse(FileVersionInfo.GetVersionInfo(InstalledExe).FileVersion, out var v) ? v : null; }
            catch { return null; }
        }
    }

    /// <summary>
    /// Primer uso o actualización al abrir el .exe de la carpeta descargada: pregunta una sola vez y reemplaza la
    /// copia anterior (la configuración se conserva). Devuelve true si el programa debe terminar aquí.
    /// </summary>
    public static bool OfferInstallOrUpdate()
    {
        if (RunningFromInstall || !IsPublishedBuild) return false;
        var installed = InstalledVersion;
        if (installed == null)
        {
            if (MessageBox.Show("¡Bienvenido a PC Hub!\n\n¿Quieres instalarlo en este PC? Quedará en el menú Inicio y se abrirá solo al encender el PC.",
                    AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return false;
            InstallFromCurrent();
            return true;
        }
        var current = typeof(Installer).Assembly.GetName().Version ?? new Version(0, 0, 0);
        var cur = new Version(current.Major, current.Minor, Math.Max(0, current.Build));
        var inst = new Version(installed.Major, installed.Minor, Math.Max(0, installed.Build));
        if (cur > inst)
        {
            if (MessageBox.Show($"Hay una versión nueva de PC Hub ({cur}). Tienes la {inst}.\n\n¿Actualizar ahora? Tu PIN y tus teléfonos vinculados se conservan.",
                    AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                InstallFromCurrent();
                return true;
            }
            return false;
        }
        // Ya está instalado (misma versión o más nueva): se abre la copia instalada en vez de duplicarla
        Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = true, WorkingDirectory = InstallDir });
        return true;
    }

    /// <summary>¿Este proceso es la copia instalada (y no "dotnet run" desde el código)?</summary>
    public static bool RunningFromInstall =>
        Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\')
            .Equals(Path.GetFullPath(InstallDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    /// <summary>Número de versión (archivo VERSION del repositorio al compilar), ej. "0.3.0".</summary>
    public static string Number
    {
        get
        {
            var v = typeof(Installer).Assembly.GetName().Version;
            return v == null ? "0.0.0" : $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
        }
    }

    public static string VersionLabel
    {
        get
        {
            var v = AgentVersion.Current;
            var short_ = v == null ? "desarrollo" : v.StartsWith("local-") ? v : v[..Math.Min(7, v.Length)];
            return $"{Number} ({short_})";
        }
    }

    /// <summary>
    /// Copia esta carpeta (el programa y sus archivos) a la carpeta de instalación, activa "Iniciar con Windows"
    /// y abre la copia instalada. Es lo que hace el doble clic en PcHub.Agent.exe la primera vez.
    /// </summary>
    public static void InstallFromCurrent()
    {
        try
        {
            // Cerrar la copia instalada si está corriendo
            foreach (var p in Process.GetProcessesByName("PcHub.Agent"))
            {
                if (p.Id == Environment.ProcessId) continue;
                try { p.Kill(true); p.WaitForExit(5000); } catch { }
            }
            Directory.CreateDirectory(InstallDir);
            var source = AppContext.BaseDirectory;
            foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(InstallDir, Path.GetRelativePath(source, dir)));
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(source, file);
                if (rel.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) continue;
                CopyWithRetry(file, Path.Combine(InstallDir, rel));
            }
            CreateShortcut(StartMenuShortcut, InstalledExe, "Controla tu PC desde el teléfono");
            SetStartup(true);
            Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = true, WorkingDirectory = InstallDir });
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo instalar", ex);
            MessageBox.Show("No se pudo instalar PC Hub:\n\n" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // Tras cerrar la copia vieja, Windows puede tardar un instante en soltar sus archivos
    private static void CopyWithRetry(string from, string to)
    {
        for (int i = 0; ; i++)
        {
            try { File.Copy(from, to, overwrite: true); return; }
            catch (IOException) when (i < 10) { System.Threading.Thread.Sleep(500); }
        }
    }

    /// <summary>Al arrancar la copia instalada: crea/actualiza el acceso directo y la entrada de desinstalación.</summary>
    public static void EnsureRegistered()
    {
        if (!RunningFromInstall) return;
        try
        {
            CreateShortcut(StartMenuShortcut, InstalledExe, "Controla tu PC desde el teléfono");
            using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
            key.SetValue("DisplayName", AppName);
            key.SetValue("DisplayVersion", VersionLabel);
            key.SetValue("Publisher", "PC Hub");
            key.SetValue("InstallLocation", InstallDir);
            key.SetValue("DisplayIcon", InstalledExe);
            key.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", (int)(DirSize(InstallDir) / 1024), RegistryValueKind.DWord);

            // Si estaba configurado para iniciar con Windows, que apunte a esta copia
            if (StartupEnabled) SetStartup(true);
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo registrar la instalación", ex);
        }
    }

    // ------------------------------------------------------------------ inicio con Windows

    public static bool StartupEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(RunValue) is string;
            }
            catch { return false; }
        }
    }

    /// <summary>Solo funciona con la copia instalada (la de "dotnet run" no sirve para arrancar con Windows).</summary>
    public static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            if (!IsInstalled) throw new InvalidOperationException("Primero instala PC Hub (clic derecho en el ícono de la bandeja → Instalar PC Hub en este PC).");
            key.SetValue(RunValue, $"\"{InstalledExe}\" --startup");
        }
        else
        {
            key.DeleteValue(RunValue, false);
        }
        Log.Info($"Iniciar con Windows: {enabled}");
    }

    // ------------------------------------------------------------------ desinstalar

    /// <summary>"PcHub.Agent.exe --uninstall" (lo llama Configuración → Aplicaciones).</summary>
    public static void RunUninstall()
    {
        if (MessageBox.Show("¿Desinstalar PC Hub de este PC?\n\nTu configuración y dispositivos vinculados se conservan en %APPDATA%\\PcHub por si lo vuelves a instalar.",
                AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        // Cerrar la copia que esté corriendo
        foreach (var p in Process.GetProcessesByName("PcHub.Agent"))
        {
            if (p.Id == Environment.ProcessId) continue;
            try { p.Kill(true); p.WaitForExit(5000); } catch { }
        }

        try { SetStartup(false); } catch { }
        try { File.Delete(StartMenuShortcut); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }

        // Los archivos se borran después de que este proceso termine (están en uso)
        var script = $"""
            Start-Sleep -Seconds 2
            Remove-Item -LiteralPath '{InstallDir.Replace("'", "''")}' -Recurse -Force -ErrorAction SilentlyContinue
            """;
        StartHiddenPowerShell(script);
        MessageBox.Show("PC Hub se desinstaló.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ------------------------------------------------------------------ utilidades

    public static void StartHiddenPowerShell(string script)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        Process.Start(new ProcessStartInfo("powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand {encoded}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });
    }

    private static void CreateShortcut(string lnkPath, string target, string description)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell no disponible");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic link = shell.CreateShortcut(lnkPath);
            link.TargetPath = target;
            link.WorkingDirectory = Path.GetDirectoryName(target);
            link.Description = description;
            link.IconLocation = target + ",0";
            link.Save();
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }

    private static long DirSize(string dir)
    {
        try
        {
            long total = 0;
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) total += new FileInfo(f).Length;
            return total;
        }
        catch { return 0; }
    }
}
