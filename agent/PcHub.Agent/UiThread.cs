using System;
using System.Windows.Forms;

namespace PcHub.Agent;

/// <summary>Ejecuta código en el hilo de la UI (necesario para el portapapeles).</summary>
internal static class UiThread
{
    private static Control? _control;

    public static void Init()
    {
        _control = new Control();
        _ = _control.Handle; // fuerza la creación del handle en el hilo actual (UI)
    }

    public static T Invoke<T>(Func<T> func)
    {
        if (_control == null || !_control.InvokeRequired) return func();
        return (T)_control.Invoke(func)!;
    }

    public static void Invoke(Action action) => Invoke(() => { action(); return true; });
}
