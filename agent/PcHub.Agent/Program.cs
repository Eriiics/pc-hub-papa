using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace PcHub.Agent;

internal static class Program
{
    /// <summary>Se inició solo con Windows (sin mostrar avisos al arrancar).</summary>
    public static bool StartedWithWindows { get; private set; }

    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Log.Init();

        // Si PC Hub se abrió desde otra app que define esta variable (p. ej. Claude), los programas que lanza
        // (cmd, gradlew, scripts) no encontrarían archivos de su propia carpeta. Se quita para todo lo que se ejecute.
        Environment.SetEnvironmentVariable("NoDefaultCurrentDirectoryInExePath", null);

        if (args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase))
        {
            Installer.RunUninstall();
            return;
        }
        StartedWithWindows = args.Contains("--startup", StringComparer.OrdinalIgnoreCase);

        // Instalar o actualizar desde la carpeta descargada va ANTES del candado: la copia vieja puede estar corriendo
        if (!StartedWithWindows && Installer.OfferInstallOrUpdate()) return;

        // Al reiniciar tras una actualización, la copia anterior puede tardar un momento en cerrarse
        using var mutex = new Mutex(false, "PcHub.Agent.Singleton");
        bool acquired;
        try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(StartedWithWindows ? 1 : 8)); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired)
        {
            if (!StartedWithWindows)
                MessageBox.Show("PC Hub ya está corriendo (revisa la bandeja del sistema).", "PC Hub");
            return;
        }

        try
        {
            Log.Info($"Iniciando PC Hub Agent {Installer.VersionLabel}" + (Installer.RunningFromInstall ? " (instalado)" : " (desde el código)"));

            AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Excepción no controlada", e.ExceptionObject as Exception);
            Application.ThreadException += (_, e) => Log.Error("Excepción en UI", e.Exception);

            Installer.EnsureRegistered();

            var config = ConfigStore.Load();
            Application.Run(new TrayApp(config));
        }
        finally
        {
            mutex.ReleaseMutex();
        }
        // Termina el proceso aunque quede algún hilo en primer plano: así suelta los archivos al actualizarse
        Environment.Exit(0);
    }
}
