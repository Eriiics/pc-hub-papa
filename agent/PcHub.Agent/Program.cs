using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace PcHub.Agent;

internal static class Program
{
    /// <summary>Se inició solo con Windows (sin mostrar avisos al arrancar).</summary>
    public static bool StartedWithWindows { get; private set; }

    /// <summary>
    /// Sesión de prueba (<c>--prueba</c>): usa su propia carpeta de datos (%APPDATA%\PcHub-Prueba), no se instala,
    /// no toca "Iniciar con Windows" y no choca con la copia instalada. Ver PLAN-PRUEBAS.md.
    /// </summary>
    public static bool PruebaMode { get; private set; }

    [STAThread]
    private static void Main(string[] args)
    {
        // Antes que nada: la carpeta de datos y el log dependen de este modo
        PruebaMode = args.Contains("--prueba", StringComparer.OrdinalIgnoreCase);

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Log.Init();

        // Autoprueba del módulo de Excel contra un Excel real (para quien arma versiones; no abre la bandeja)
        if (args.Contains("--autoprueba-excel", StringComparer.OrdinalIgnoreCase))
        {
            Environment.ExitCode = ExcelSelfTest.Run();
            return;
        }

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
        using var mutex = new Mutex(false, PruebaMode ? "PcHub.Agent.Singleton.Prueba" : "PcHub.Agent.Singleton");
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
