using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using PcHub.Agent.Server;

namespace PcHub.Agent.Modules;

/// <summary>
/// Comandos en lenguaje natural con Claude. Dos formas de conectarse:
///  - "claude-code": usa Claude Code (CLI) con tu suscripción de claude.ai, sin API key (ver AiModule.ClaudeCode.cs).
///  - "api": la API de Claude con API key (tool use directo, este archivo).
/// La IA no toca el PC directamente: elige herramientas y el agente las ejecuta.
/// PowerShell, apagar, reiniciar y suspender siempre piden confirmación en el teléfono.
/// </summary>
public sealed partial class AiModule
{
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const int MaxSteps = 12;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };

    private readonly Hub _hub;

    public AiModule(Hub hub) => _hub = hub;

    private string ApiKey => _hub.Config.Ai.ResolveApiKey();

    /// <summary>Prueba una API key y un modelo con una petición mínima. Devuelve (ok, mensaje).</summary>
    public static async Task<(bool ok, string message)> TestAsync(string key, string model)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = 16,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "Responde solo: ok" }),
        };
        try
        {
            var resp = await PostAsync(body, key.Trim());
            var text = (resp["content"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault()?["text"]?.ToString() ?? "";
            return (true, $"Funciona. Modelo {resp["model"]}, respuesta: \"{text.Trim()}\"");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<JsonObject> AskAsync(Session s, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Mensaje vacío");
        if (UseClaudeCode) return await AskViaClaudeCodeAsync(s, text);

        var key = ApiKey;
        if (key.Length == 0)
            throw new InvalidOperationException("Falta la API key de Claude. En el PC: menú de la bandeja → Configurar asistente IA…");

        await s.AiLock.WaitAsync();
        int rollback = s.AiHistory.Count;
        try
        {
            var history = s.AiHistory;
            history.Add(new JsonObject { ["role"] = "user", ["content"] = text });
            var actions = new JsonArray();

            for (int step = 0; step < MaxSteps; step++)
            {
                var body = new JsonObject
                {
                    ["model"] = _hub.Config.Ai.Model,
                    ["max_tokens"] = _hub.Config.Ai.MaxTokens,
                    ["system"] = BuildSystemPrompt(),
                    ["tools"] = Tools.DeepClone(),
                    ["messages"] = new JsonArray(history.Select(h => (JsonNode?)h.DeepClone()).ToArray()),
                };

                var resp = await PostAsync(body, key);
                var content = resp["content"] as JsonArray ?? new JsonArray();
                var stop = resp["stop_reason"]?.ToString();
                history.Add(new JsonObject { ["role"] = "assistant", ["content"] = content.DeepClone() });

                var toolUses = content.OfType<JsonObject>().Where(b => b["type"]?.ToString() == "tool_use").ToList();
                if (stop != "tool_use" || toolUses.Count == 0)
                {
                    var reply = string.Join("\n", content.OfType<JsonObject>()
                        .Where(b => b["type"]?.ToString() == "text")
                        .Select(b => b["text"]?.ToString() ?? "")).Trim();
                    TrimHistory(history);
                    return new JsonObject { ["reply"] = reply.Length > 0 ? reply : "Listo.", ["actions"] = actions };
                }

                var results = new JsonArray();
                foreach (var tu in toolUses)
                {
                    var name = tu["name"]?.ToString() ?? "";
                    var input = tu["input"] as JsonObject ?? new JsonObject();
                    var desc = Describe(name, input);
                    actions.Add(desc);
                    await s.SendEventAsync("ai.step", new JsonObject { ["text"] = desc });

                    string result;
                    bool isError = false;
                    try
                    {
                        result = await ExecuteToolAsync(s, name, input);
                    }
                    catch (Exception ex)
                    {
                        result = "Error: " + ex.Message;
                        isError = true;
                    }
                    Log.Info($"IA → {name}: {Short(result)}");
                    results.Add(new JsonObject
                    {
                        ["type"] = "tool_result",
                        ["tool_use_id"] = tu["id"]?.ToString(),
                        ["content"] = result.Length > 0 ? result : "ok",
                        ["is_error"] = isError,
                    });
                }
                history.Add(new JsonObject { ["role"] = "user", ["content"] = results });
            }

            TrimHistory(history);
            return new JsonObject { ["reply"] = "Me detuve porque la tarea requería demasiados pasos.", ["actions"] = actions };
        }
        catch
        {
            // Deshacer el turno incompleto para que el historial no quede inválido
            if (s.AiHistory.Count > rollback) s.AiHistory.RemoveRange(rollback, s.AiHistory.Count - rollback);
            throw;
        }
        finally
        {
            s.AiLock.Release();
        }
    }

    private static void TrimHistory(List<JsonObject> history)
    {
        if (history.Count <= 30) return;
        while (history.Count > 20) history.RemoveAt(0);
        // El historial debe empezar con un mensaje del usuario que sea texto (no un tool_result)
        while (history.Count > 0 && !(history[0]["role"]?.ToString() == "user" && history[0]["content"] is JsonValue))
            history.RemoveAt(0);
    }

    private static async Task<JsonObject> PostAsync(JsonObject body, string key)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
        req.Headers.Add("x-api-key", key);
        req.Headers.Add("anthropic-version", "2023-06-01");
        req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var resp = await Http.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            string msg = text;
            try { msg = JsonNode.Parse(text)?["error"]?["message"]?.ToString() ?? text; } catch { }
            throw new InvalidOperationException($"API de Claude ({(int)resp.StatusCode}): {Short(msg, 300)}");
        }
        return JsonNode.Parse(text) as JsonObject ?? throw new InvalidOperationException("Respuesta inválida de la API");
    }

    // ------------------------------------------------------------------ prompt

    private string BuildSystemPrompt() => SystemRules() + "\n\n" + StateBlock();

    /// <summary>Reglas fijas del asistente (no cambian entre peticiones).</summary>
    private string SystemRules()
    {
        var cmds = string.Join("\n", _hub.Config.Commands.Select(c => $"- {c.Id}: {c.Name} ({c.Type})"));
        var extra = _hub.Config.Ai.ExtraInstructions;
        return $"""
            Eres el asistente de "PC Hub", una app con la que el usuario controla su PC con Windows desde el teléfono.
            El usuario te habla en español (a veces por voz, así que puede haber errores de transcripción).
            Tu trabajo es ejecutar lo que pide usando las herramientas disponibles, y luego responder MUY breve
            (una o dos frases), porque la respuesta se lee en la pantalla del teléfono.

            Reglas:
            - Prefiere las herramientas específicas (launch_app, media_control, focus_window, press_keys...) antes que PowerShell.
            - Usa run_powershell solo cuando no haya otra forma; el usuario debe aprobarlo en el teléfono.
            - Nunca hagas acciones destructivas (borrar archivos, cerrar sin guardar, desinstalar) sin que el usuario lo pida explícitamente.
            - Si necesitas saber qué hay abierto, usa get_pc_state.
            - Para Excel usa excel_read, excel_write y excel_action (no teclees en la hoja). Lee antes de calcular, y para sumar escribe una fórmula (=SUMA) en una celda vacía junto a los datos.
            - Después de abrir o enfocar una app, si vas a escribir en ella, espera un momento con wait (300-1500 ms).
            - Si algo falla, explica en una frase qué pasó.
            - Responde en texto plano, sin Markdown (nada de **negritas** ni listas): se muestra tal cual en el teléfono.

            Comandos personalizados disponibles (run_command):
            {(cmds.Length > 0 ? cmds : "(ninguno)")}
            {(string.IsNullOrWhiteSpace(extra) ? "" : "\nInstrucciones del usuario:\n" + extra)}
            """;
    }

    /// <summary>Estado del PC en este momento (cambia en cada petición).</summary>
    private string StateBlock()
    {
        var (proc, title) = WindowModule.Foreground();
        return $"""
            Estado actual:
            - Fecha y hora: {DateTime.Now:dddd d 'de' MMMM yyyy, HH:mm}
            - Nombre del PC: {_hub.Config.PcName}
            - Ventana activa: {title} ({proc})
            """;
    }

    private static readonly JsonArray Tools = (JsonArray)JsonNode.Parse("""
    [
      {"name":"get_pc_state","description":"Devuelve la ventana activa, las ventanas abiertas (con su id), lo que está sonando y el volumen.","input_schema":{"type":"object","properties":{}}},
      {"name":"focus_window","description":"Trae una ventana al frente. Usa window_id (de get_pc_state) o title_contains.","input_schema":{"type":"object","properties":{"window_id":{"type":"integer"},"title_contains":{"type":"string"}}}},
      {"name":"close_window","description":"Cierra una ventana (como hacer clic en la X). Usa window_id o title_contains.","input_schema":{"type":"object","properties":{"window_id":{"type":"integer"},"title_contains":{"type":"string"}}}},
      {"name":"launch_app","description":"Abre una aplicación instalada por su nombre (busca en el menú Inicio, incluye juegos de Steam).","input_schema":{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}},
      {"name":"open_url","description":"Abre una URL http(s) en el navegador predeterminado.","input_schema":{"type":"object","properties":{"url":{"type":"string"}},"required":["url"]}},
      {"name":"media_control","description":"Controla lo que suena en el PC (YouTube Music, Spotify, etc.).","input_schema":{"type":"object","properties":{"action":{"type":"string","enum":["playpause","play","pause","next","prev"]}},"required":["action"]}},
      {"name":"set_volume","description":"Cambia el volumen general del PC (0-100) y/o lo silencia.","input_schema":{"type":"object","properties":{"level":{"type":"integer","minimum":0,"maximum":100},"mute":{"type":"boolean"}}}},
      {"name":"type_text","description":"Escribe texto en la ventana activa, como si se tecleara.","input_schema":{"type":"object","properties":{"text":{"type":"string"}},"required":["text"]}},
      {"name":"press_keys","description":"Presiona una combinación de teclas, ej: 'ctrl+t', 'alt+f4', 'win+d', 'enter', 'ctrl+shift+esc', 'f5'.","input_schema":{"type":"object","properties":{"combo":{"type":"string"},"repeat":{"type":"integer","minimum":1,"maximum":50}},"required":["combo"]}},
      {"name":"run_command","description":"Ejecuta uno de los comandos personalizados del usuario por su id.","input_schema":{"type":"object","properties":{"id":{"type":"string"}},"required":["id"]}},
      {"name":"system_action","description":"Acciones del sistema. shutdown, restart y sleep piden confirmación al usuario.","input_schema":{"type":"object","properties":{"action":{"type":"string","enum":["lock","sleep","monitor_off","shutdown","restart","cancel_shutdown"]}},"required":["action"]}},
      {"name":"run_powershell","description":"Ejecuta un script de PowerShell y devuelve su salida. Requiere aprobación del usuario en el teléfono. Explica el motivo en 'reason'.","input_schema":{"type":"object","properties":{"script":{"type":"string"},"reason":{"type":"string"}},"required":["script","reason"]}},
      {"name":"wait","description":"Espera unos milisegundos (máx. 5000) entre pasos.","input_schema":{"type":"object","properties":{"ms":{"type":"integer","minimum":0,"maximum":5000}},"required":["ms"]}},
      {"name":"play_youtube","description":"Busca en YouTube (o YouTube Music si music=true) y reproduce el primer resultado en la pestaña de YouTube abierta (o una nueva). Úsalo para 'pon X en YouTube' o 'pon la canción X'.","input_schema":{"type":"object","properties":{"query":{"type":"string"},"music":{"type":"boolean"}},"required":["query"]}},
      {"name":"excel_read","description":"Lee celdas del Excel abierto (hoja activa). range como 'A1:C10'; vacío = toda la zona usada (máx. 500 celdas). Las fórmulas vienen entre paréntesis. Úsalo antes de sumar, buscar o editar.","input_schema":{"type":"object","properties":{"range":{"type":"string"}}}},
      {"name":"excel_write","description":"Escribe en el Excel abierto desde la celda 'cell'. 'values' es un texto/número o una lista de filas (cada fila, una lista de valores). Los que empiezan con '=' son fórmulas, p. ej. '=SUMA(B2:B20)'.","input_schema":{"type":"object","properties":{"cell":{"type":"string"},"values":{}},"required":["cell","values"]}},
      {"name":"excel_action","description":"Acción en Excel sobre 'range' (opcional, se selecciona primero): save, undo, clear, bold, autosum, filter, copy, cut, paste, insert_row, delete_row, autofit, percent, money, number, next_sheet, prev_sheet, new_sheet.","input_schema":{"type":"object","properties":{"action":{"type":"string"},"range":{"type":"string"}},"required":["action"]}}
    ]
    """)!;

    // ------------------------------------------------------------------ herramientas

    private async Task<string> ExecuteToolAsync(Session s, string name, JsonObject input)
    {
        switch (name)
        {
            case "get_pc_state":
            {
                var (proc, title) = WindowModule.Foreground();
                var wins = WindowModule.List().Select(w => new JsonObject
                {
                    ["window_id"] = w.Handle.ToInt64(),
                    ["title"] = w.Title,
                    ["process"] = w.Process,
                });
                var media = await _hub.Media.ReadStateAsync();
                media.Remove("art");
                var vol = AudioModule.Get();
                return new JsonObject
                {
                    ["active_window"] = new JsonObject { ["title"] = title, ["process"] = proc },
                    ["windows"] = new JsonArray(wins.Cast<JsonNode?>().ToArray()),
                    ["media"] = media,
                    ["volume"] = vol,
                }.ToJsonString();
            }

            case "focus_window":
            case "close_window":
            {
                IntPtr h;
                if (input["window_id"] != null) h = new IntPtr(Hub.L(input, "window_id"));
                else
                {
                    var w = WindowModule.FindByText(Hub.S(input, "title_contains"))
                        ?? throw new ArgumentException("No encontré una ventana con ese título");
                    h = w.Handle;
                }
                if (name == "focus_window")
                {
                    WindowModule.Focus(h);
                    await Task.Delay(250);
                    return "Ventana enfocada";
                }
                WindowModule.Close(h);
                return "Se pidió cerrar la ventana";
            }

            case "launch_app":
            {
                var (match, suggestions) = AppsModule.Find(Hub.S(input, "name"));
                if (match == null)
                    return suggestions.Count > 0
                        ? $"No hubo coincidencia exacta. Candidatas: {string.Join(", ", suggestions)}"
                        : "No encontré esa aplicación en el menú Inicio.";
                AppsModule.Start(match.Path);
                await Task.Delay(400);
                var others = suggestions.Where(x => x != match.Name).ToList();
                return $"Abriendo '{match.Name}'" + (others.Count > 0 ? $". Otras parecidas: {string.Join(", ", others)}" : "");
            }

            case "open_url":
            {
                var url = Hub.S(input, "url");
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
                    throw new ArgumentException("Solo se permiten URLs http/https");
                AppsModule.Start(uri.ToString());
                return "URL abierta";
            }

            case "media_control":
                await _hub.Media.CommandAsync(Hub.S(input, "action"), null);
                return "ok";

            case "set_volume":
                if (input["level"] != null) AudioModule.SetLevel(Hub.D(input, "level") / 100.0);
                if (input["mute"] != null) AudioModule.SetMute(Hub.B(input, "mute"));
                return AudioModule.Get().ToJsonString();

            case "type_text":
                InputModule.TypeText(Hub.S(input, "text"));
                return "ok";

            case "press_keys":
                InputModule.KeyCombo(Hub.S(input, "combo"), Hub.I(input, "repeat", 1));
                return "ok";

            case "run_command":
            {
                var cmd = _hub.Commands.Find(Hub.S(input, "id")) ?? throw new ArgumentException("Ese comando no existe");
                if (cmd.Confirm || cmd.Type == "powershell")
                {
                    if (!await s.RequestConfirmAsync($"Ejecutar \"{cmd.Name}\"", cmd.Type == "powershell" ? cmd.Value : cmd.Name))
                        return "El usuario rechazó la ejecución.";
                }
                var output = await CommandModule.RunAsync(cmd);
                return output.Length > 0 ? output : "ok";
            }

            case "system_action":
            {
                var action = Hub.S(input, "action");
                if (action is "shutdown" or "restart" or "sleep")
                {
                    var label = action switch { "shutdown" => "Apagar el PC", "restart" => "Reiniciar el PC", _ => "Suspender el PC" };
                    if (!await s.RequestConfirmAsync(label, "La IA quiere ejecutar esta acción."))
                        return "El usuario rechazó la acción.";
                }
                SystemModule.Action(action);
                return "ok";
            }

            case "run_powershell":
            {
                if (!_hub.Config.Ai.AllowPowerShell)
                    return "PowerShell está deshabilitado en la configuración (ai.allowPowerShell = false).";
                var script = Hub.S(input, "script");
                var reason = Hub.S(input, "reason");
                if (!await s.RequestConfirmAsync("Ejecutar PowerShell", $"{reason}\n\n{script}"))
                    return "El usuario rechazó ejecutar el script.";
                return await PowerShellRunner.RunAsync(script);
            }

            case "wait":
                await Task.Delay(Math.Clamp(Hub.I(input, "ms", 500), 0, 5000));
                return "ok";

            case "play_youtube":
            {
                bool music = Hub.B(input, "music");
                var found = await YouTubeModule.SearchAsync(Hub.S(input, "query"), music);
                var first = (found["results"] as JsonArray)?.FirstOrDefault() as JsonObject;
                if (first == null) return "No encontré resultados.";
                var msg = await YouTubeModule.PlayAsync(first["id"]!.ToString(), music);
                return $"{msg} \"{first["title"]}\" de {first["channel"]}";
            }

            case "excel_read":
                return ExcelModule.ReadRange(Hub.S(input, "range")).ToJsonString();

            case "excel_write":
                ExcelModule.WriteRange(Hub.S(input, "cell"), input["values"] ?? "");
                return "ok";

            case "excel_action":
            {
                var range = Hub.S(input, "range");
                if (range.Length > 0) ExcelModule.Select(range, 0, 0);
                ExcelModule.Action(Hub.S(input, "action"));
                return "ok";
            }

            default:
                throw new ArgumentException($"Herramienta desconocida: {name}");
        }
    }

    private static string Describe(string name, JsonObject input) => name switch
    {
        "get_pc_state" => "Revisando el estado del PC",
        "focus_window" => $"Enfocando ventana {Hub.S(input, "title_contains")}".TrimEnd(),
        "close_window" => $"Cerrando ventana {Hub.S(input, "title_contains")}".TrimEnd(),
        "launch_app" => $"Abriendo {Hub.S(input, "name")}",
        "open_url" => $"Abriendo {Hub.S(input, "url")}",
        "media_control" => $"Media: {Hub.S(input, "action")}",
        "set_volume" => input["level"] != null ? $"Volumen al {Hub.I(input, "level")}%" : "Cambiando el silencio",
        "type_text" => $"Escribiendo \"{Short(Hub.S(input, "text"), 40)}\"",
        "press_keys" => $"Teclas {Hub.S(input, "combo")}",
        "run_command" => $"Comando {Hub.S(input, "id")}",
        "system_action" => $"Sistema: {Hub.S(input, "action")}",
        "run_powershell" => "PowerShell (esperando tu aprobación)",
        "wait" => "Esperando…",
        "excel_read" => $"Leyendo Excel {Hub.S(input, "range")}".TrimEnd(),
        "excel_write" => $"Escribiendo en Excel ({Hub.S(input, "cell")})",
        "excel_action" => $"Excel: {Hub.S(input, "action")}",
        "play_youtube" => $"Buscando \"{Hub.S(input, "query")}\" en {(Hub.B(input, "music") ? "YouTube Music" : "YouTube")}",
        _ => name,
    };

    private static string Short(string s, int max = 120) => s.Length <= max ? s : s[..max] + "…";
}
