using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using PcHub.Agent.Server;

namespace PcHub.Agent.Modules;

/// <summary>
/// IA usando Claude Code (CLI) con la suscripción de claude.ai, sin API key.
///
/// Cómo funciona:
///  1. El agente expone sus herramientas (abrir apps, media, teclas...) como un servidor MCP local en
///     http://127.0.0.1:{puerto}/mcp, protegido con un token de un solo uso y solo accesible desde este PC.
///  2. Para cada pedido ejecuta `claude -p` con: sin herramientas propias de Claude Code (no puede tocar archivos
///     ni ejecutar comandos por su cuenta), solo las herramientas de PC Hub, y el modelo elegido (Haiku por defecto).
///  3. Claude Code llama a las herramientas por MCP; el agente las ejecuta (pidiendo confirmación en el teléfono
///     para PowerShell / apagar) y devuelve el resultado.
/// La conversación continúa entre pedidos con --resume, hasta que borras el chat en el teléfono.
/// </summary>
public sealed partial class AiModule
{
    private const string McpServerName = "pchub";
    private static readonly string[] McpVersions = { "2025-06-18", "2025-03-26", "2024-11-05" };
    private readonly ConcurrentDictionary<string, Session> _mcpTokens = new();

    private bool UseClaudeCode => _hub.Config.Ai.Provider.Trim().ToLowerInvariant() switch
    {
        "api" => false,
        "claude-code" or "claudecode" or "subscription" => true,
        _ => !_hub.Config.Ai.HasApiKey, // auto
    };

    public string ProviderLabel => UseClaudeCode ? "Claude Code (suscripción)" : "API de Claude";

    // ------------------------------------------------------------------ encontrar claude

    public static string? FindClaude(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var candidates = new List<string>
        {
            Path.Combine(home, ".local", "bin", "claude.exe"),   // instalador nativo
        };
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            candidates.Add(Path.Combine(dir.Trim(), "claude.exe"));
            candidates.Add(Path.Combine(dir.Trim(), "claude.cmd"));
        }
        candidates.Add(Path.Combine(appData, "npm", "claude.cmd"));       // npm install -g
        foreach (var c in candidates)
        {
            try { if (File.Exists(c)) return c; } catch { /* ruta inválida en el PATH */ }
        }
        return null;
    }

    /// <summary>¿Está instalado Claude Code y con sesión de claude.ai? Para el diálogo de configuración.</summary>
    public static async Task<(bool ok, string message)> ClaudeCodeStatusAsync(string? configuredPath)
    {
        var exe = FindClaude(configuredPath);
        if (exe == null)
            return (false, "No encontré Claude Code. Instálalo (ver README) y vuelve a probar.");

        var version = await RunClaudeAsync(exe, new[] { "--version" }, null, null, 30_000);
        var status = await RunClaudeAsync(exe, new[] { "auth", "status" }, null, null, 30_000);
        string method = "";
        try { method = JsonNode.Parse(ExtractJson(status.stdout) ?? "{}")?["authMethod"]?.ToString() ?? ""; } catch { }

        var v = version.stdout.Trim();
        return method switch
        {
            "claude.ai" or "oauth_token" => (true, $"Claude Code {v} con sesión de claude.ai: listo, sin costo extra."),
            "none" or "" when status.exitCode != 0 => (false, $"Claude Code {v} está instalado pero sin sesión. Abre una terminal, ejecuta \"claude\" y usa /login con tu cuenta de claude.ai."),
            "api_key" or "api_key_helper" => (false, $"Claude Code {v} está usando una API key (se cobraría aparte). Ejecuta \"claude auth logout\" y luego \"claude\" → /login con tu cuenta de claude.ai."),
            _ => (status.exitCode == 0, $"Claude Code {v}, autenticación: {method}."),
        };
    }

    /// <summary>Prueba real: una pregunta mínima por Claude Code, sin herramientas.</summary>
    public static async Task<(bool ok, string message)> TestClaudeCodeAsync(string? configuredPath, string model)
    {
        var exe = FindClaude(configuredPath);
        if (exe == null) return (false, "No encontré Claude Code.");
        var work = WorkDir();
        var r = await RunClaudeAsync(exe,
            new[] { "-p", "--output-format", "json", "--model", model, "--tools", "", "--strict-mcp-config", "--no-session-persistence" },
            "Responde solo con la palabra: ok", work, 120_000);
        var (ok, text, _) = ParseResult(r);
        return ok ? (true, $"Funciona con tu suscripción (modelo {model}). Respuesta: \"{Short(text, 60)}\"") : (false, FriendlyError(text));
    }

    // ------------------------------------------------------------------ pedido

    private async Task<JsonObject> AskViaClaudeCodeAsync(Session s, string text)
    {
        var exe = FindClaude(_hub.Config.Ai.ClaudeCodePath)
            ?? throw new InvalidOperationException("No encontré Claude Code en el PC. Instálalo o configura el asistente desde la bandeja del PC.");

        await s.AiLock.WaitAsync();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var work = WorkDir();
        var mcpFile = Path.Combine(work, $"mcp-{token[..8]}.json");
        var promptFile = Path.Combine(work, "system-prompt.txt");
        _mcpTokens[token] = s;
        var actionsBefore = s.AiStepCount;
        try
        {
            // Config MCP de un solo uso: solo este pedido puede usar el token
            var mcp = new JsonObject
            {
                ["mcpServers"] = new JsonObject
                {
                    [McpServerName] = new JsonObject
                    {
                        ["type"] = "http",
                        ["url"] = $"http://127.0.0.1:{_hub.Config.Port}/mcp",
                        ["headers"] = new JsonObject { ["Authorization"] = "Bearer " + token },
                    },
                },
            };
            await File.WriteAllTextAsync(mcpFile, mcp.ToJsonString());
            await File.WriteAllTextAsync(promptFile, SystemRules());

            var args = new List<string>
            {
                "-p",
                "--output-format", "json",
                "--model", string.IsNullOrWhiteSpace(_hub.Config.Ai.ClaudeCodeModel) ? "haiku" : _hub.Config.Ai.ClaudeCodeModel,
                "--system-prompt-file", promptFile,
                "--strict-mcp-config",
                "--mcp-config", mcpFile,
                "--tools", "",                       // sin Bash/Read/Edit propios de Claude Code
                "--allowedTools", "mcp__" + McpServerName,
                "--permission-mode", "dontAsk",      // lo que no esté permitido se rechaza, sin preguntar
                "--max-turns", "15",
            };
            var resumeId = s.ClaudeSessionId;
            if (resumeId != null) { args.Add("--resume"); args.Add(resumeId); }

            // El estado cambia en cada pedido: va en el mensaje, no en el prompt de sistema (que se reutiliza al reanudar)
            var prompt = StateBlock() + "\n\nPedido del usuario: " + text;

            var r = await RunClaudeAsync(exe, args, prompt, work, 240_000);
            var (ok, result, sessionId) = ParseResult(r);

            if (!ok && resumeId != null && (result.Contains("No conversation found", StringComparison.OrdinalIgnoreCase)
                                            || result.Contains("session", StringComparison.OrdinalIgnoreCase) && result.Contains("not found", StringComparison.OrdinalIgnoreCase)))
            {
                // La sesión anterior ya no existe: empezar una nueva
                s.ClaudeSessionId = null;
                var i = args.IndexOf("--resume");
                args.RemoveRange(i, 2);
                r = await RunClaudeAsync(exe, args, prompt, work, 240_000);
                (ok, result, sessionId) = ParseResult(r);
            }

            if (!ok) throw new InvalidOperationException(FriendlyError(result));
            if (!string.IsNullOrEmpty(sessionId)) s.ClaudeSessionId = sessionId;
            return new JsonObject
            {
                ["reply"] = result.Trim().Length > 0 ? result.Trim() : "Listo.",
                ["actions"] = new JsonArray(),
                ["steps"] = s.AiStepCount - actionsBefore,
            };
        }
        finally
        {
            _mcpTokens.TryRemove(token, out _);
            try { File.Delete(mcpFile); } catch { }
            s.AiLock.Release();
        }
    }

    public void ResetClaudeSession(Session s) => s.ClaudeSessionId = null;

    private static string WorkDir()
    {
        // Carpeta vacía propia: así Claude Code no carga el CLAUDE.md ni la config de ningún proyecto tuyo
        var dir = Path.Combine(ConfigStore.Folder, "claude-work");
        Directory.CreateDirectory(dir);
        return dir;
    }

    internal static string FriendlyError(string text)
    {
        var t = text.ToLowerInvariant();
        if (t.Contains("login") || t.Contains("not logged") || t.Contains("authentication") || t.Contains("oauth"))
            return "Claude Code no tiene sesión iniciada. En el PC abre una terminal, ejecuta \"claude\" y usa /login con tu cuenta de claude.ai.";
        if (t.Contains("usage limit") || t.Contains("rate limit") || t.Contains("limit reached"))
            return "Se alcanzó el límite de uso de tu plan por ahora. Vuelve a intentarlo más tarde.";
        if (t.Contains("credit balance") || t.Contains("billing"))
            return "Claude Code está usando una API key sin crédito. Ejecuta \"claude auth logout\" y vuelve a entrar con /login usando tu cuenta de claude.ai.";
        return "Claude Code: " + Short(text, 300);
    }

    private static (bool ok, string text, string? sessionId) ParseResult((int exitCode, string stdout, string stderr) r)
    {
        var json = ExtractJson(r.stdout);
        if (json != null)
        {
            try
            {
                var o = JsonNode.Parse(json) as JsonObject;
                if (o != null)
                {
                    var isError = o["is_error"]?.GetValue<bool>() ?? (o["subtype"]?.ToString() is { } st && st != "success");
                    var result = o["result"]?.ToString() ?? o["subtype"]?.ToString() ?? "";
                    return (!isError && r.exitCode == 0, result, o["session_id"]?.ToString());
                }
            }
            catch { /* cae al texto plano */ }
        }
        var raw = (r.stdout + "\n" + r.stderr).Trim();
        return (false, raw.Length > 0 ? raw : $"terminó con código {r.exitCode}", null);
    }

    /// <summary>La salida json es un objeto; se toma la última línea que lo sea (por si hay avisos antes).</summary>
    private static string? ExtractJson(string stdout)
    {
        var lines = stdout.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("{") && l.EndsWith("}")).ToList();
        if (lines.Count > 0) return lines[^1];
        var t = stdout.Trim();
        return t.StartsWith("{") ? t : null;
    }

    private static async Task<(int exitCode, string stdout, string stderr)> RunClaudeAsync(
        string exe, IEnumerable<string> args, string? stdin, string? workDir, int timeoutMs)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false),
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (workDir != null) psi.WorkingDirectory = workDir;
        // Sin esto, una ANTHROPIC_API_KEY en el entorno tendría prioridad y se cobraría por API
        psi.Environment.Remove("ANTHROPIC_API_KEY");
        psi.Environment.Remove("ANTHROPIC_AUTH_TOKEN");
        psi.Environment["CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"] = "1";

        using var p = new Process { StartInfo = psi };
        try
        {
            p.Start();
        }
        catch (Exception ex)
        {
            return (-1, "", "No se pudo ejecutar Claude Code: " + ex.Message);
        }

        var outTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();
        try
        {
            if (stdin != null) await p.StandardInput.WriteAsync(stdin);
            p.StandardInput.Close();
        }
        catch { /* el proceso pudo terminar antes */ }

        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await p.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch { }
            return (-1, "", $"Claude Code tardó más de {timeoutMs / 1000} s y se detuvo.");
        }
        return (p.ExitCode, await outTask, await errTask);
    }

    // ------------------------------------------------------------------ servidor MCP (lo llama Claude Code)

    /// <summary>Endpoint MCP "streamable HTTP" mínimo: responde JSON-RPC con application/json (sin SSE).</summary>
    public async Task HandleMcpAsync(HttpContext ctx)
    {
        var remote = ctx.Connection.RemoteIpAddress;
        if (remote == null || !IPAddress.IsLoopback(remote))
        {
            ctx.Response.StatusCode = 403;
            return;
        }

        var auth = ctx.Request.Headers["Authorization"].ToString();
        var token = auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? auth[7..].Trim() : "";
        if (token.Length == 0 || !_mcpTokens.TryGetValue(token, out var session))
        {
            ctx.Response.StatusCode = 401;
            return;
        }

        if (ctx.Request.Method == "GET")
        {
            // No hay canal de eventos del servidor: el cliente debe seguir solo con POST
            ctx.Response.StatusCode = 405;
            ctx.Response.Headers["Allow"] = "POST";
            return;
        }
        if (ctx.Request.Method == "DELETE")
        {
            ctx.Response.StatusCode = 200;
            return;
        }
        if (ctx.Request.Method != "POST")
        {
            ctx.Response.StatusCode = 405;
            return;
        }

        string body;
        using (var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8))
            body = await reader.ReadToEndAsync();

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(body);
        }
        catch
        {
            await WriteJsonAsync(ctx, RpcError(null, -32700, "JSON inválido"));
            return;
        }

        if (parsed is JsonArray batch)
        {
            var responses = new JsonArray();
            foreach (var item in batch.OfType<JsonObject>())
            {
                var res = await HandleRpcAsync(session, item);
                if (res != null) responses.Add(res);
            }
            if (responses.Count == 0) { ctx.Response.StatusCode = 202; return; }
            await WriteJsonAsync(ctx, responses);
            return;
        }

        if (parsed is not JsonObject msg)
        {
            await WriteJsonAsync(ctx, RpcError(null, -32600, "Petición inválida"));
            return;
        }

        var response = await HandleRpcAsync(session, msg);
        if (response == null)
        {
            ctx.Response.StatusCode = 202; // notificación: sin cuerpo
            return;
        }
        await WriteJsonAsync(ctx, response);
    }

    private static async Task WriteJsonAsync(HttpContext ctx, JsonNode node)
    {
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(node.ToJsonString());
    }

    private async Task<JsonObject?> HandleRpcAsync(Session session, JsonObject msg)
    {
        var method = msg["method"]?.ToString() ?? "";
        var id = msg["id"]?.DeepClone();
        var p = msg["params"] as JsonObject ?? new JsonObject();
        if (id == null) return null; // notificaciones (notifications/initialized, etc.)

        try
        {
            switch (method)
            {
                case "initialize":
                {
                    var requested = p["protocolVersion"]?.ToString();
                    var version = McpVersions.Contains(requested) ? requested! : McpVersions[0];
                    return RpcResult(id, new JsonObject
                    {
                        ["protocolVersion"] = version,
                        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
                        ["serverInfo"] = new JsonObject { ["name"] = McpServerName, ["version"] = "0.1.0" },
                        ["instructions"] = "Herramientas para controlar el PC con Windows del usuario.",
                    });
                }

                case "ping":
                    return RpcResult(id, new JsonObject());

                case "tools/list":
                {
                    var list = new JsonArray();
                    foreach (var t in Tools.OfType<JsonObject>())
                        list.Add(new JsonObject
                        {
                            ["name"] = t["name"]?.ToString(),
                            ["description"] = t["description"]?.ToString(),
                            ["inputSchema"] = t["input_schema"]?.DeepClone(),
                        });
                    return RpcResult(id, new JsonObject { ["tools"] = list });
                }

                case "tools/call":
                {
                    var name = p["name"]?.ToString() ?? "";
                    var input = p["arguments"] as JsonObject ?? new JsonObject();
                    var desc = Describe(name, input);
                    session.AiStepCount++;
                    await session.SendEventAsync("ai.step", new JsonObject { ["text"] = desc });

                    string result;
                    bool isError = false;
                    try
                    {
                        result = await ExecuteToolAsync(session, name, input);
                    }
                    catch (Exception ex)
                    {
                        result = "Error: " + ex.Message;
                        isError = true;
                    }
                    Log.Info($"IA (Claude Code) → {name}: {Short(result)}");
                    return RpcResult(id, new JsonObject
                    {
                        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = result.Length > 0 ? result : "ok" }),
                        ["isError"] = isError,
                    });
                }

                case "resources/list":
                    return RpcResult(id, new JsonObject { ["resources"] = new JsonArray() });

                case "prompts/list":
                    return RpcResult(id, new JsonObject { ["prompts"] = new JsonArray() });

                default:
                    return RpcError(id, -32601, $"Método no soportado: {method}");
            }
        }
        catch (Exception ex)
        {
            Log.Error("MCP " + method, ex);
            return RpcError(id, -32603, ex.Message);
        }
    }

    private static JsonObject RpcResult(JsonNode? id, JsonNode result) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };

    private static JsonObject RpcError(JsonNode? id, int code, string message) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };
}
