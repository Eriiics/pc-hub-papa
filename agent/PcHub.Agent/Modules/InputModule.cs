using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using PcHub.Agent.Native;
using static PcHub.Agent.Native.Win32;

namespace PcHub.Agent.Modules;

/// <summary>Mouse y teclado mediante SendInput.</summary>
public static class InputModule
{
    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    // ---------------- Mouse ----------------

    public static void MouseMove(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return;
        Send(MouseInput(MOUSEEVENTF_MOVE, dx, dy));
    }

    /// <summary>Posición absoluta en la pantalla principal: x, y de 0 a 65535 (modo puntero tipo Wii).</summary>
    public static void MouseAbsolute(int x, int y)
    {
        Send(MouseInput(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE, Math.Clamp(x, 0, 65535), Math.Clamp(y, 0, 65535)));
    }

    public static void MouseButton(string button, bool down)
    {
        uint flag = (button, down) switch
        {
            ("right", true) => MOUSEEVENTF_RIGHTDOWN,
            ("right", false) => MOUSEEVENTF_RIGHTUP,
            ("middle", true) => MOUSEEVENTF_MIDDLEDOWN,
            ("middle", false) => MOUSEEVENTF_MIDDLEUP,
            (_, true) => MOUSEEVENTF_LEFTDOWN,
            _ => MOUSEEVENTF_LEFTUP,
        };
        Send(MouseInput(flag));
    }

    public static void MouseClick(string button, bool isDouble)
    {
        int times = isDouble ? 2 : 1;
        for (int i = 0; i < times; i++)
        {
            MouseButton(button, true);
            MouseButton(button, false);
        }
    }

    /// <summary>120 = un "clic" de la rueda. Positivo = hacia arriba / derecha.</summary>
    public static void MouseScroll(int dy, int dx)
    {
        var list = new List<INPUT>();
        if (dy != 0) list.Add(MouseInput(MOUSEEVENTF_WHEEL, 0, 0, unchecked((uint)dy)));
        if (dx != 0) list.Add(MouseInput(MOUSEEVENTF_HWHEEL, 0, 0, unchecked((uint)dx)));
        if (list.Count > 0) Send(list.ToArray());
    }

    private static INPUT MouseInput(uint flags, int dx = 0, int dy = 0, uint data = 0) => new()
    {
        type = INPUT_MOUSE,
        U = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = data, dwFlags = flags } }
    };

    // ---------------- Teclado ----------------

    /// <summary>Escribe texto Unicode tal cual (acentos, ñ y emojis incluidos).</summary>
    public static void TypeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var list = new List<INPUT>(text.Length * 2);
        foreach (char c in text.Replace("\r\n", "\n"))
        {
            if (c == '\n')
            {
                list.Add(KeyInput(0x0D, false));
                list.Add(KeyInput(0x0D, true));
                continue;
            }
            if (c == '\t')
            {
                list.Add(KeyInput(0x09, false));
                list.Add(KeyInput(0x09, true));
                continue;
            }
            list.Add(new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE } } });
            list.Add(new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP } } });
        }
        Send(list.ToArray());
    }

    /// <summary>Presiona una combinación como "ctrl+shift+esc", "win", "alt+tab", "f5".</summary>
    public static void KeyCombo(string combo, int repeat = 1)
    {
        var keys = ParseCombo(combo);
        repeat = Math.Clamp(repeat, 1, 200);
        var list = new List<INPUT>();
        for (int r = 0; r < repeat; r++)
        {
            foreach (var k in keys) list.Add(KeyInput(k, false));
            for (int i = keys.Count - 1; i >= 0; i--) list.Add(KeyInput(keys[i], true));
        }
        Send(list.ToArray());
    }

    public static void KeyDown(string key) => Send(KeyInput(ParseKey(key), false));
    public static void KeyUp(string key) => Send(KeyInput(ParseKey(key), true));

    public static void KeyDown(ushort vk) => Send(KeyInput(vk, false));
    public static void KeyUp(ushort vk) => Send(KeyInput(vk, true));

    public static List<ushort> ParseCombo(string combo)
    {
        var parts = combo.ToLowerInvariant().Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) throw new ArgumentException("Combinación vacía");
        return parts.Select(ParseKey).ToList();
    }

    public static ushort ParseKey(string name)
    {
        name = name.Trim().ToLowerInvariant();
        if (Named.TryGetValue(name, out var vk)) return vk;
        if (name.Length == 1)
        {
            char c = name[0];
            if (c is >= 'a' and <= 'z') return (ushort)char.ToUpperInvariant(c);
            if (c is >= '0' and <= '9') return c;
        }
        if (name.Length >= 2 && name[0] == 'f' && int.TryParse(name[1..], out int f) && f is >= 1 and <= 24)
            return (ushort)(0x70 + f - 1);
        throw new ArgumentException($"Tecla desconocida: {name}");
    }

    private static readonly Dictionary<string, ushort> Named = new()
    {
        ["ctrl"] = 0x11, ["control"] = 0x11, ["shift"] = 0x10, ["alt"] = 0x12, ["altgr"] = 0xA5,
        ["win"] = 0x5B, ["windows"] = 0x5B, ["super"] = 0x5B, ["menu"] = 0x5D, ["apps"] = 0x5D,
        ["enter"] = 0x0D, ["return"] = 0x0D, ["esc"] = 0x1B, ["escape"] = 0x1B, ["tab"] = 0x09,
        ["backspace"] = 0x08, ["bksp"] = 0x08, ["delete"] = 0x2E, ["del"] = 0x2E, ["supr"] = 0x2E,
        ["insert"] = 0x2D, ["ins"] = 0x2D, ["space"] = 0x20, ["espacio"] = 0x20,
        ["up"] = 0x26, ["down"] = 0x28, ["left"] = 0x25, ["right"] = 0x27,
        ["home"] = 0x24, ["end"] = 0x23, ["pgup"] = 0x21, ["pageup"] = 0x21, ["pgdn"] = 0x22, ["pagedown"] = 0x22,
        ["capslock"] = 0x14, ["printscreen"] = 0x2C, ["prtsc"] = 0x2C, ["pause"] = 0x13,
        ["volup"] = 0xAF, ["voldown"] = 0xAE, ["volmute"] = 0xAD,
        ["playpause"] = 0xB3, ["next"] = 0xB0, ["prev"] = 0xB1, ["stop"] = 0xB2,
        ["comma"] = 0xBC, ["period"] = 0xBE, ["minus"] = 0xBD, ["plus"] = 0xBB,
    };

    // Teclas que necesitan el flag "extended" para no comportarse como teclado numérico
    private static readonly HashSet<ushort> Extended = new()
    {
        0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E, 0x5B, 0x5D, 0xA5, 0x2C,
        0xAD, 0xAE, 0xAF, 0xB0, 0xB1, 0xB2, 0xB3,
    };

    private static INPUT KeyInput(ushort vk, bool up)
    {
        uint flags = up ? KEYEVENTF_KEYUP : 0;
        if (Extended.Contains(vk)) flags |= KEYEVENTF_EXTENDEDKEY;
        return new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = flags } } };
    }

    private static void Send(params INPUT[] inputs)
    {
        if (inputs.Length == 0) return;
        uint sent = Win32.SendInput((uint)inputs.Length, inputs, InputSize);
        if (sent != inputs.Length)
            Log.Error($"SendInput envió {sent}/{inputs.Length} (¿ventana de administrador en primer plano?)");
    }
}
