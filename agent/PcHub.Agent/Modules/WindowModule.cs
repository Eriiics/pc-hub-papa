using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using PcHub.Agent.Native;
using static PcHub.Agent.Native.Win32;

namespace PcHub.Agent.Modules;

public sealed record WindowInfo(IntPtr Handle, string Title, string Process, string? ExePath);

/// <summary>Lista y controla las ventanas que aparecerían en Alt+Tab.</summary>
public static class WindowModule
{
    private static readonly uint OwnPid = (uint)Environment.ProcessId;

    public static List<WindowInfo> List()
    {
        var result = new List<WindowInfo>();
        EnumWindows((h, _) =>
        {
            if (IsAltTabWindow(h))
            {
                GetWindowThreadProcessId(h, out uint pid);
                if (pid != OwnPid)
                {
                    var exe = GetProcessPath(pid);
                    var proc = exe != null ? Path.GetFileNameWithoutExtension(exe) : "?";
                    result.Add(new WindowInfo(h, GetTitle(h), proc, exe));
                }
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static bool IsAltTabWindow(IntPtr h)
    {
        if (!IsWindowVisible(h)) return false;
        if (GetWindowTextLength(h) == 0) return false;
        long ex = GetWindowLong(h, GWL_EXSTYLE);
        if ((ex & WS_EX_TOOLWINDOW) != 0 && (ex & WS_EX_APPWINDOW) == 0) return false;
        if (GetWindow(h, GW_OWNER) != IntPtr.Zero && (ex & WS_EX_APPWINDOW) == 0) return false;
        // Ventanas "ocultas" de apps UWP / escritorios virtuales
        if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        var title = GetTitle(h);
        if (title == "Program Manager") return false;
        return true;
    }

    public static JsonObject ListJson()
    {
        var fg = GetForegroundWindow();
        var arr = new JsonArray();
        foreach (var w in List())
        {
            arr.Add(new JsonObject
            {
                ["h"] = w.Handle.ToInt64(),
                ["title"] = w.Title,
                ["proc"] = w.Process,
                ["icon"] = IconHelper.FromFile(w.ExePath),
                ["active"] = w.Handle == fg,
            });
        }
        return new JsonObject { ["windows"] = arr };
    }

    public static void Focus(IntPtr h)
    {
        if (!IsWindow(h)) throw new ArgumentException("La ventana ya no existe");
        if (IsIconic(h)) ShowWindow(h, SW_RESTORE);
        // Truco: Windows solo deja cambiar el primer plano si "hubo input"; presionar Alt lo habilita.
        InputModule.KeyDown(0x12);
        SetForegroundWindow(h);
        InputModule.KeyUp(0x12);
    }

    public static void Close(IntPtr h) => PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

    public static void Minimize(IntPtr h) => ShowWindow(h, SW_MINIMIZE);

    /// <summary>Busca una ventana cuyo título o proceso contenga el texto (para la IA).</summary>
    public static WindowInfo? FindByText(string text)
    {
        var t = text.ToLowerInvariant();
        var list = List();
        return list.FirstOrDefault(w => w.Title.ToLowerInvariant().Contains(t))
            ?? list.FirstOrDefault(w => w.Process.ToLowerInvariant().Contains(t));
    }

    public static (string process, string title) Foreground()
    {
        var h = GetForegroundWindow();
        if (h == IntPtr.Zero) return ("", "");
        GetWindowThreadProcessId(h, out uint pid);
        var exe = GetProcessPath(pid);
        return (exe != null ? Path.GetFileNameWithoutExtension(exe) : "", GetTitle(h));
    }
}
