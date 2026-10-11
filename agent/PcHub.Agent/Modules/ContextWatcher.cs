using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;

namespace PcHub.Agent.Modules;

/// <summary>
/// Revisa la ventana activa cada 700 ms (proceso + título) y aplica las reglas de contexto.
/// Se usa polling y no un hook porque el título cambia al cambiar de pestaña sin que cambie la ventana.
/// </summary>
public sealed class ContextWatcher : IDisposable
{
    private readonly AgentConfig _config;
    private readonly Action<JsonObject> _onChange;
    private Timer? _timer;
    private string _lastKey = "";

    public JsonObject Current { get; private set; } = new();

    public ContextWatcher(AgentConfig config, Action<JsonObject> onChange)
    {
        _config = config;
        _onChange = onChange;
    }

    public void Start() => _timer = new Timer(_ => Tick(), null, 0, 700);

    private void Tick()
    {
        try
        {
            var (process, title) = WindowModule.Foreground();
            if (process.Length == 0) return;
            var module = Match(process, title);
            var ctx = new JsonObject { ["process"] = process, ["title"] = title, ["module"] = module };
            Current = ctx;
            // Se avisa al teléfono cuando cambia el proceso o el módulo (no en cada cambio de título)
            var key = $"{process}|{module}";
            if (key == _lastKey) return;
            _lastKey = key;
            _onChange(ctx);
        }
        catch (Exception ex)
        {
            Log.Error("ContextWatcher", ex);
        }
    }

    private string? Match(string process, string title)
    {
        foreach (var r in _config.ContextRules.ToList())
        {
            bool procOk = string.IsNullOrWhiteSpace(r.Process) || process.Contains(r.Process, StringComparison.OrdinalIgnoreCase);
            bool titleOk = string.IsNullOrWhiteSpace(r.Title) || title.Contains(r.Title, StringComparison.OrdinalIgnoreCase);
            if (procOk && titleOk) return r.Module;
        }
        return null;
    }

    public void Dispose() => _timer?.Dispose();
}
