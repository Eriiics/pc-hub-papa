using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Windows.Automation;

namespace PcHub.Agent.Modules;

/// <summary>
/// Usa UI Automation para saber cuándo el foco del teclado cae en un campo de texto,
/// y así abrir el teclado del teléfono automáticamente.
/// Limitaciones: juegos y algunas apps con controles propios no lo informan; para eso está el botón manual.
/// </summary>
public sealed class FocusWatcher : IDisposable
{
    private readonly Action<JsonObject> _onChange;
    private readonly ManualResetEvent _stop = new(false);
    private Thread? _thread;
    private bool _lastEditable;
    private string _lastId = "";

    public FocusWatcher(Action<JsonObject> onChange) => _onChange = onChange;

    public void Start()
    {
        // UI Automation debe correr en un hilo MTA sin ventanas propias para no bloquearse
        _thread = new Thread(Run) { IsBackground = true, Name = "FocusWatcher" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    private void Run()
    {
        try
        {
            Automation.AddAutomationFocusChangedEventHandler(OnFocusChanged);
            Log.Info("FocusWatcher activo");
            _stop.WaitOne();
            Automation.RemoveAllEventHandlers();
        }
        catch (Exception ex)
        {
            Log.Error("FocusWatcher no pudo iniciar", ex);
        }
    }

    private void OnFocusChanged(object sender, AutomationFocusChangedEventArgs e)
    {
        try
        {
            if (sender is not AutomationElement el) return;
            var info = el.Current;
            if (info.ProcessId == Environment.ProcessId) return;

            bool editable = IsEditable(el, info);
            string id = string.Join(".", el.GetRuntimeId() ?? Array.Empty<int>());

            // Avisar solo si cambia el estado o si se pasó a OTRO campo editable
            if (editable == _lastEditable && (!editable || id == _lastId)) return;
            _lastEditable = editable;
            _lastId = id;

            _onChange(new JsonObject
            {
                ["editable"] = editable,
                ["password"] = editable && info.IsPassword,
                ["name"] = editable ? info.Name ?? "" : "",
            });
        }
        catch (ElementNotAvailableException) { /* el elemento desapareció */ }
        catch (Exception ex)
        {
            Log.Error("FocusWatcher", ex);
        }
    }

    private static bool IsEditable(AutomationElement el, AutomationElement.AutomationElementInformation info)
    {
        var ct = info.ControlType;
        if (ct != ControlType.Edit && ct != ControlType.Document && ct != ControlType.ComboBox) return false;

        if (el.TryGetCurrentPattern(ValuePattern.Pattern, out var vp))
            return !((ValuePattern)vp).Current.IsReadOnly;

        // Edit sin ValuePattern (ej. editores de texto enriquecido en apps Electron): se asume editable.
        // Document/ComboBox sin ValuePattern suelen ser páginas web o listas: no editable.
        return ct == ControlType.Edit;
    }

    public void Dispose() => _stop.Set();
}
