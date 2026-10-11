using System;
using System.Diagnostics;
using System.Windows.Forms;
using static PcHub.Agent.Native.Win32;

namespace PcHub.Agent.Modules;

public static class SystemModule
{
    public static void Action(string action)
    {
        switch (action)
        {
            case "lock":
                LockWorkStation();
                break;
            case "sleep":
                SetSuspendState(false, false, false);
                break;
            case "monitor_off":
                PostMessage(HWND_BROADCAST, WM_SYSCOMMAND, (IntPtr)SC_MONITORPOWER, (IntPtr)2);
                break;
            case "shutdown":
                RunShutdown("/s /t 5");
                break;
            case "restart":
                RunShutdown("/r /t 5");
                break;
            case "cancel_shutdown":
                RunShutdown("/a");
                break;
            default:
                throw new ArgumentException($"Acción de sistema desconocida: {action}");
        }
    }

    private static void RunShutdown(string args) =>
        Process.Start(new ProcessStartInfo("shutdown.exe", args) { CreateNoWindow = true, UseShellExecute = false });
}

public static class ClipboardModule
{
    public static string GetText() => UiThread.Invoke(() =>
    {
        for (int i = 0; i < 5; i++)
        {
            try { return Clipboard.ContainsText() ? Clipboard.GetText() : ""; }
            catch { System.Threading.Thread.Sleep(50); } // portapapeles bloqueado por otra app
        }
        return "";
    });

    public static void SetText(string text) => UiThread.Invoke(() =>
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                if (string.IsNullOrEmpty(text)) Clipboard.Clear();
                else Clipboard.SetText(text);
                return;
            }
            catch { System.Threading.Thread.Sleep(50); }
        }
        throw new InvalidOperationException("El portapapeles está ocupado por otra aplicación");
    });
}
