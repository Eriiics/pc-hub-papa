using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace PcHub.Agent.Server;

/// <summary>Una conexión WebSocket con un teléfono.</summary>
public sealed class Session
{
    private readonly WebSocket _ws;
    private readonly Hub _hub;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _confirms = new();
    private int _confirmSeq;

    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
    public IPAddress? Remote { get; }
    public bool Authenticated { get; set; }
    public string DeviceName { get; set; } = "";

    // Conversación con la IA (una por teléfono conectado)
    public List<JsonObject> AiHistory { get; } = new();
    public SemaphoreSlim AiLock { get; } = new(1, 1);
    /// <summary>Conversación de Claude Code que se continúa con --resume (modo suscripción).</summary>
    public string? ClaudeSessionId { get; set; }
    /// <summary>Herramientas ejecutadas por la IA en esta conexión (para contar pasos).</summary>
    public int AiStepCount { get; set; }
    /// <summary>Clave del canal UDP rápido de mouse (ver UdpInput), si el teléfono lo pidió.</summary>
    public ulong? UdpKey { get; set; }
    /// <summary>Pestaña de terminal que está mirando este teléfono (solo a esa se le manda la salida).</summary>
    public string? TermTab { get; set; }

    public Session(WebSocket ws, Hub hub, IPAddress? remote)
    {
        _ws = ws;
        _hub = hub;
        Remote = remote;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var buffer = new byte[32 * 1024];
        using var ms = new MemoryStream();
        try
        {
            while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                ms.SetLength(0);
                WebSocketReceiveResult r;
                do
                {
                    r = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (r.MessageType == WebSocketMessageType.Close)
                    {
                        await _ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                        return;
                    }
                    ms.Write(buffer, 0, r.Count);
                    if (ms.Length > 8 * 1024 * 1024) throw new InvalidDataException("Mensaje demasiado grande");
                } while (!r.EndOfMessage);

                if (r.MessageType != WebSocketMessageType.Text) continue;
                var text = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
                await _hub.HandleAsync(this, text);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch (Exception ex)
        {
            Log.Error($"Sesión {Id}", ex);
        }
        finally
        {
            foreach (var tcs in _confirms.Values) tcs.TrySetResult(false);
        }
    }

    public async Task SendRawAsync(string json)
    {
        if (_ws.State != WebSocketState.Open) return;
        var bytes = Encoding.UTF8.GetBytes(json);
        await _sendLock.WaitAsync();
        try
        {
            if (_ws.State == WebSocketState.Open)
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException) { }
        finally
        {
            _sendLock.Release();
        }
    }

    public Task SendEventAsync(string evt, JsonNode? data) =>
        SendRawAsync($"{{\"t\":\"evt\",\"e\":{JsonValue.Create(evt)!.ToJsonString()},\"d\":{data?.ToJsonString() ?? "null"}}}");

    public Task SendResultAsync(long id, JsonNode? data) =>
        SendRawAsync($"{{\"t\":\"res\",\"id\":{id},\"ok\":true,\"d\":{data?.ToJsonString() ?? "null"}}}");

    public Task SendErrorAsync(long id, string error) =>
        SendRawAsync($"{{\"t\":\"res\",\"id\":{id},\"ok\":false,\"err\":{JsonValue.Create(error)!.ToJsonString()}}}");

    /// <summary>Pide confirmación al teléfono (para PowerShell, apagar, etc.). Devuelve false si no responde.</summary>
    public async Task<bool> RequestConfirmAsync(string title, string detail, int timeoutSeconds = 90)
    {
        var id = $"c{Interlocked.Increment(ref _confirmSeq)}";
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _confirms[id] = tcs;
        try
        {
            await SendEventAsync("ai.confirm_req", new JsonObject { ["id"] = id, ["title"] = title, ["detail"] = detail });
            var done = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)));
            return done == tcs.Task && tcs.Task.Result;
        }
        finally
        {
            _confirms.TryRemove(id, out _);
        }
    }

    public void ResolveConfirm(string id, bool ok)
    {
        if (_confirms.TryGetValue(id, out var tcs)) tcs.TrySetResult(ok);
    }
}
