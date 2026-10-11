using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace PcHub.Agent.Server;

/// <summary>Servidor WebSocket (Kestrel) en ws://0.0.0.0:puerto/ws</summary>
public sealed class HubServer : IAsyncDisposable
{
    private WebApplication? _app;

    public async Task StartAsync(Hub hub, int port)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            Args = Array.Empty<string>(),
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.ListenAnyIP(port));

        _app = builder.Build();
        _app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });

        _app.Use(async (HttpContext ctx, RequestDelegate next) =>
        {
            if (ctx.Request.Path == "/ws")
            {
                if (!ctx.WebSockets.IsWebSocketRequest)
                {
                    ctx.Response.StatusCode = 400;
                    return;
                }
                using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
                var session = new Session(ws, hub, ctx.Connection.RemoteIpAddress);
                hub.AddSession(session);
                Log.Info($"Conexión {session.Id} desde {session.Remote}");
                try
                {
                    await session.RunAsync(ctx.RequestAborted);
                }
                finally
                {
                    hub.RemoveSession(session);
                    Log.Info($"Desconexión {session.Id} ({session.DeviceName})");
                }
                return;
            }

            if (ctx.Request.Path == "/mcp")
            {
                // Herramientas del PC para Claude Code (solo desde este PC y con token; ver AiModule.ClaudeCode.cs)
                await hub.Ai.HandleMcpAsync(ctx);
                return;
            }

            // Terminal en la pantalla del PC (ver TerminalModule): solo desde este PC y con la clave de esta sesión
            if (ctx.Request.Path.StartsWithSegments("/term"))
            {
                var remote = ctx.Connection.RemoteIpAddress;
                if (remote == null || !IPAddress.IsLoopback(remote))
                {
                    ctx.Response.StatusCode = 403;
                    return;
                }
                if (ctx.Request.Path == "/term/ws")
                {
                    if (!ctx.WebSockets.IsWebSocketRequest || ctx.Request.Query["k"] != hub.Terminal.WebKey)
                    {
                        ctx.Response.StatusCode = 403;
                        return;
                    }
                    using var tws = await ctx.WebSockets.AcceptWebSocketAsync();
                    await hub.Terminal.HandlePcSocketAsync(tws, ctx.Request.Query["id"].ToString(), ctx.RequestAborted);
                    return;
                }
                await ServeTerminalFileAsync(ctx);
                return;
            }

            if (ctx.Request.Path == "/")
            {
                await ctx.Response.WriteAsync("PC Hub agent 0.1.0");
                return;
            }
            await next(ctx);
        });

        await _app.StartAsync();
        Log.Info($"Servidor WebSocket escuchando en el puerto {port}");
    }

    /// <summary>Archivos de la página de terminal (web/terminal junto al .exe).</summary>
    private static async Task ServeTerminalFileAsync(HttpContext ctx)
    {
        var rel = ctx.Request.Path.Value!["/term".Length..].TrimStart('/');
        if (rel.Length == 0) rel = "index.html";
        if (rel.Contains("..") || rel.Contains('\\') || rel.Contains(':'))
        {
            ctx.Response.StatusCode = 400;
            return;
        }
        var file = System.IO.Path.Combine(AppContext.BaseDirectory, "web", "terminal", rel);
        if (!System.IO.File.Exists(file))
        {
            ctx.Response.StatusCode = 404;
            return;
        }
        ctx.Response.ContentType = System.IO.Path.GetExtension(file) switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            _ => "application/octet-stream",
        };
        ctx.Response.Headers["Cache-Control"] = "no-cache";
        await ctx.Response.SendFileAsync(file);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app != null) await _app.DisposeAsync();
    }
}

/// <summary>Responde a "PCHUB?" por UDP para que el teléfono encuentre el PC sin escribir la IP.</summary>
public sealed class DiscoveryResponder : IDisposable
{
    private UdpClient? _udp;
    private readonly CancellationTokenSource _cts = new();

    public void Start(int discoveryPort, Func<string> nameProvider, int wsPort)
    {
        try
        {
            _udp = new UdpClient();
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, discoveryPort));
        }
        catch (Exception ex)
        {
            Log.Error($"No se pudo abrir el puerto UDP {discoveryPort} para descubrimiento", ex);
            return;
        }

        _ = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var r = await _udp.ReceiveAsync(_cts.Token);
                    var msg = Encoding.UTF8.GetString(r.Buffer);
                    if (!msg.StartsWith("PCHUB?")) continue;
                    var reply = new JsonObject { ["name"] = nameProvider(), ["port"] = wsPort, ["v"] = 1 }.ToJsonString();
                    var bytes = Encoding.UTF8.GetBytes(reply);
                    await _udp.SendAsync(bytes, bytes.Length, r.RemoteEndPoint);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    Log.Error("Descubrimiento UDP", ex);
                    await Task.Delay(500);
                }
            }
        });
        Log.Info($"Descubrimiento UDP en el puerto {discoveryPort}");
    }

    public void Dispose()
    {
        _cts.Cancel();
        _udp?.Dispose();
    }
}
