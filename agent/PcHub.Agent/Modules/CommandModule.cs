using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace PcHub.Agent.Modules;

/// <summary>Comandos personalizados definidos en config.json (los botones del hub).</summary>
public sealed class CommandModule
{
    private readonly AgentConfig _config;

    public CommandModule(AgentConfig config) => _config = config;

    public JsonObject ListJson()
    {
        var arr = new JsonArray();
        foreach (var c in _config.Commands)
            arr.Add(new JsonObject
            {
                ["id"] = c.Id,
                ["name"] = c.Name,
                ["icon"] = c.Icon,
                ["type"] = c.Type,
                ["confirm"] = c.Confirm,
            });
        return new JsonObject { ["commands"] = arr };
    }

    public CustomCommand? Find(string id) => _config.Commands.FirstOrDefault(c => c.Id == id);

    public async Task<string> RunAsync(string id)
    {
        var cmd = Find(id) ?? throw new ArgumentException($"Comando no encontrado: {id}");
        return await RunAsync(cmd);
    }

    public static async Task<string> RunAsync(CustomCommand cmd)
    {
        Log.Info($"Ejecutando comando '{cmd.Name}' ({cmd.Type})");
        switch (cmd.Type.ToLowerInvariant())
        {
            case "keys":
                InputModule.KeyCombo(cmd.Value);
                return "";
            case "text":
                InputModule.TypeText(cmd.Value);
                return "";
            case "launch":
                AppsModule.Start(Environment.ExpandEnvironmentVariables(cmd.Value), cmd.Args);
                return "";
            case "url":
                AppsModule.Start(cmd.Value);
                return "";
            case "powershell":
                return await PowerShellRunner.RunAsync(cmd.Value);
            case "sequence":
                await RunSequenceAsync(cmd.Value);
                return "";
            default:
                throw new ArgumentException($"Tipo de comando desconocido: {cmd.Type}");
        }
    }

    /// <summary>Líneas "tipo:valor". Tipos: keys, text, wait, launch, url.</summary>
    public static async Task RunSequenceAsync(string script)
    {
        foreach (var raw in script.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            int i = line.IndexOf(':');
            if (i < 0) throw new ArgumentException($"Línea inválida en la secuencia: {line}");
            var kind = line[..i].Trim().ToLowerInvariant();
            var value = line[(i + 1)..];
            switch (kind)
            {
                case "keys": InputModule.KeyCombo(value.Trim()); break;
                case "text": InputModule.TypeText(value); break;
                case "wait": await Task.Delay(Math.Clamp(int.Parse(value.Trim()), 0, 10_000)); break;
                case "launch": AppsModule.Start(Environment.ExpandEnvironmentVariables(value.Trim())); break;
                case "url": AppsModule.Start(value.Trim()); break;
                default: throw new ArgumentException($"Paso desconocido en la secuencia: {kind}");
            }
            await Task.Delay(40); // pequeña pausa para que Windows procese cada paso
        }
    }
}
