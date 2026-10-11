using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using PcHub.Agent.Native;
using PcHub.Agent.Server;

namespace PcHub.Agent.Modules;

/// <summary>
/// Terminal completa del PC (PowerShell en una pseudoconsola de Windows): colores, cursor, vim, etc.
/// El teléfono la dibuja con xterm.js. Cada pestaña es una sesión; la misma sesión se puede mostrar también en la
/// pantalla del PC (una ventana de Edge en modo app con la página /term/ del agente), así lo que escribes en un lado
/// se ve en el otro.
/// </summary>
public sealed class TerminalModule : IDisposable
{
    private const int MaxScrollback = 256 * 1024;

    private sealed class Tab
    {
        public required string Id;
        public required string Title;
        public ConPty? Pty;
        public bool Running;
        public int Cols = 100, Rows = 30;
        public readonly object Lock = new();
        /// <summary>Lo último que mostró, para quien se conecta después (teléfono que vuelve, ventana del PC).</summary>
        public readonly List<byte> Scrollback = new();
        public readonly MemoryStream Pending = new();
        /// <summary>Ventanas del PC conectadas a esta pestaña.</summary>
        public readonly ConcurrentDictionary<WebSocket, byte> PcViewers = new();
    }

    private readonly Hub _hub;
    private readonly ConcurrentDictionary<string, Tab> _tabs = new();
    private readonly CancellationTokenSource _cts = new();
    private int _counter;

    /// <summary>Clave para la página /term/ (solo desde este PC).</summary>
    public string WebKey { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    public TerminalModule(Hub hub)
    {
        _hub = hub;
        _ = Task.Run(FlushLoopAsync);
    }

    // ------------------------------------------------------------------ pestañas

    public JsonNode List() => new JsonObject
    {
        ["tabs"] = new JsonArray(_tabs.Values.OrderBy(t => t.Id.Length).ThenBy(t => t.Id).Select(TabJson).ToArray()),
    };

    private static JsonNode TabJson(Tab t) => new JsonObject
    {
        ["id"] = t.Id,
        ["title"] = t.Title,
        ["running"] = t.Running,
        ["onPc"] = !t.PcViewers.IsEmpty,
    };

    public JsonNode Open(int cols, int rows)
    {
        var n = Interlocked.Increment(ref _counter);
        var tab = new Tab { Id = "t" + n, Title = "Terminal " + n };
        if (cols > 0) tab.Cols = cols;
        if (rows > 0) tab.Rows = rows;
        _tabs[tab.Id] = tab;
        Start(tab);
        NotifyState(tab);
        return TabJson(tab);
    }

    /// <summary>El teléfono se suscribe a una pestaña: recibe lo último que mostró y desde ahí, lo nuevo.</summary>
    public JsonNode Attach(Session s, string id, int cols, int rows)
    {
        var tab = Get(id);
        s.TermTab = id;
        if (cols > 0 && rows > 0) Resize(id, cols, rows);
        byte[] buffer;
        lock (tab.Lock) buffer = tab.Scrollback.ToArray();
        return new JsonObject { ["id"] = id, ["data"] = Convert.ToBase64String(buffer), ["running"] = tab.Running, ["onPc"] = !tab.PcViewers.IsEmpty };
    }

    public void Detach(Session s) => s.TermTab = null;

    /// <summary>Teclas o texto (tal cual los manda xterm.js: incluye Enter, flechas, Ctrl+C como secuencias).</summary>
    public void Input(string id, string data)
    {
        var tab = Get(id);
        if (!tab.Running || tab.Pty == null) throw new InvalidOperationException("Esta terminal se cerró. Reiníciala.");
        try
        {
            tab.Pty.Write(Encoding.UTF8.GetBytes(data));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("No se pudo escribir en la terminal: " + ex.Message);
        }
    }

    public void Resize(string id, int cols, int rows)
    {
        var tab = Get(id);
        if (cols < 10 || rows < 4) return;
        tab.Cols = cols;
        tab.Rows = rows;
        tab.Pty?.Resize(cols, rows);
    }

    public void Restart(string id)
    {
        var tab = Get(id);
        Stop(tab);
        Append(tab, Encoding.UTF8.GetBytes("\r\n\x1b[90m— Reiniciada —\x1b[0m\r\n"));
        Start(tab);
        NotifyState(tab);
    }

    public void Close(string id)
    {
        if (!_tabs.TryRemove(id, out var tab)) return;
        ShowOnPc(tab, false);
        Stop(tab);
        _hub.Broadcast("term.closed", new JsonObject { ["id"] = id });
    }

    public void Rename(string id, string title)
    {
        var tab = Get(id);
        if (title.Trim().Length > 0) tab.Title = title.Trim()[..Math.Min(40, title.Trim().Length)];
        NotifyState(tab);
    }

    private Tab Get(string id) => _tabs.TryGetValue(id, out var t) ? t : throw new InvalidOperationException("Esa terminal ya no existe.");

    private static string Shell()
    {
        // PowerShell 7 si está instalado; si no, Windows PowerShell
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pwsh = Path.Combine(pf, "PowerShell", "7", "pwsh.exe");
        return File.Exists(pwsh) ? $"\"{pwsh}\" -NoLogo" : "powershell.exe -NoLogo";
    }

    private void Start(Tab tab)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        ConPty pty;
        try
        {
            pty = new ConPty(Shell(), home, tab.Cols, tab.Rows);
        }
        catch (Exception ex)
        {
            Log.Error("Terminal", ex);
            tab.Running = false;
            Append(tab, Encoding.UTF8.GetBytes("\x1b[31mNo se pudo abrir PowerShell: " + ex.Message + "\x1b[0m\r\n"));
            return;
        }
        tab.Pty = pty;
        tab.Running = true;

        // Lector: todo lo que muestra la terminal
        var thread = new Thread(() =>
        {
            var buf = new byte[16 * 1024];
            try
            {
                int n;
                while ((n = pty.Output.Read(buf, 0, buf.Length)) > 0)
                    Append(tab, buf.AsSpan(0, n).ToArray());
            }
            catch { /* se cerró */ }
            if (tab.Pty == pty)
            {
                tab.Running = false;
                Append(tab, Encoding.UTF8.GetBytes("\r\n\x1b[90m— PowerShell se cerró —\x1b[0m\r\n"));
                NotifyState(tab);
            }
        }) { IsBackground = true, Name = "pchub-term-" + tab.Id };
        thread.Start();

        pty.Process.EnableRaisingEvents = true;
        pty.Process.Exited += (_, _) =>
        {
            // Al terminar el proceso, cerrar la pseudoconsola hace que el lector reciba el fin
            if (tab.Pty == pty) Task.Run(() => { Thread.Sleep(300); try { pty.Dispose(); } catch { } });
        };
    }

    private static void Stop(Tab tab)
    {
        var pty = tab.Pty;
        tab.Pty = null;
        tab.Running = false;
        try { pty?.Dispose(); } catch { }
    }

    private void Append(Tab tab, byte[] data)
    {
        lock (tab.Lock)
        {
            tab.Scrollback.AddRange(data);
            if (tab.Scrollback.Count > MaxScrollback) tab.Scrollback.RemoveRange(0, tab.Scrollback.Count - MaxScrollback);
            tab.Pending.Write(data, 0, data.Length);
        }
    }

    /// <summary>Junta la salida cada ~25 ms y la manda en un solo mensaje (mucho más liviano que byte a byte).</summary>
    private async Task FlushLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(25, _cts.Token);
            }
            catch (OperationCanceledException) { break; }
            foreach (var tab in _tabs.Values)
            {
                byte[] chunk;
                lock (tab.Lock)
                {
                    if (tab.Pending.Length == 0) continue;
                    chunk = tab.Pending.ToArray();
                    tab.Pending.SetLength(0);
                }
                try
                {
                    var json = $"{{\"t\":\"evt\",\"e\":\"term.out\",\"d\":{{\"id\":\"{tab.Id}\",\"data\":\"{Convert.ToBase64String(chunk)}\"}}}}";
                    foreach (var s in _hub.AuthenticatedSessions.Where(s => s.TermTab == tab.Id))
                        _ = s.SendRawAsync(json);
                    foreach (var ws in tab.PcViewers.Keys)
                        _ = SendBinaryAsync(ws, chunk);
                }
                catch (Exception ex)
                {
                    Log.Error("Terminal: enviar salida", ex);
                }
            }
        }
    }

    private void NotifyState(Tab tab) => _hub.Broadcast("term.state", TabJson(tab));

    // ------------------------------------------------------------------ ventana en el PC

    /// <summary>Muestra u oculta esta terminal en la pantalla del PC (misma sesión que en el teléfono).</summary>
    public JsonNode ShowOnPc(string id, bool show)
    {
        var tab = Get(id);
        ShowOnPc(tab, show);
        return TabJson(tab);
    }

    private void ShowOnPc(Tab tab, bool show)
    {
        if (show)
        {
            if (!tab.PcViewers.IsEmpty)
            {
                var w = FindPcWindow(tab);
                if (w != null) WindowModule.Focus(w.Handle);
                return;
            }
            var url = $"http://127.0.0.1:{_hub.Config.Port}/term/?id={tab.Id}&k={WebKey}";
            var browser = FindAppBrowser();
            var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PcHub", "terminal-window");
            if (browser != null)
            {
                Process.Start(new ProcessStartInfo(browser)
                {
                    UseShellExecute = false,
                    Arguments = $"--app=\"{url}\" --user-data-dir=\"{profile}\" --window-size=1100,700 --no-first-run --no-default-browser-check",
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
        }
        else
        {
            var w = FindPcWindow(tab);
            if (w != null) Win32.PostMessage(w.Handle, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
            // Por si la ventana no se encuentra por el título: cortar sus conexiones (la página muestra que se cerró)
            foreach (var ws in tab.PcViewers.Keys)
                _ = ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "oculta", CancellationToken.None);
        }
    }

    private static WindowInfo? FindPcWindow(Tab tab) =>
        WindowModule.List().FirstOrDefault(w => w.Title.StartsWith(PageTitle(tab)));

    /// <summary>Título de la página /term/ (así se encuentra la ventana para cerrarla).</summary>
    private static string PageTitle(Tab tab) => $"PC Hub · {tab.Id} ·";

    private static string? FindAppBrowser()
    {
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var p in new[]
                 {
                     Path.Combine(pf86, "Microsoft", "Edge", "Application", "msedge.exe"),
                     Path.Combine(pf, "Microsoft", "Edge", "Application", "msedge.exe"),
                     Path.Combine(pf, "Google", "Chrome", "Application", "chrome.exe"),
                     Path.Combine(local, "Google", "Chrome", "Application", "chrome.exe"),
                 })
        {
            if (File.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>WebSocket de la página /term/ del PC: salida en binario; entrada y tamaño en JSON.</summary>
    public async Task HandlePcSocketAsync(WebSocket ws, string id, CancellationToken ct)
    {
        if (!_tabs.TryGetValue(id, out var tab))
        {
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "no existe", CancellationToken.None);
            return;
        }
        // Primero lo que ya mostró, para que la ventana arranque igual que el teléfono
        byte[] backlog;
        lock (tab.Lock) backlog = tab.Scrollback.ToArray();
        await SendTextAsync(ws, new JsonObject { ["t"] = "hello", ["title"] = PageTitle(tab) + " " + tab.Title }.ToJsonString());
        if (backlog.Length > 0) await SendBinaryAsync(ws, backlog);
        tab.PcViewers[ws] = 0;
        NotifyState(tab);

        var buffer = new byte[64 * 1024];
        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult r;
                do
                {
                    r = await ws.ReceiveAsync(buffer, ct);
                    if (r.MessageType == WebSocketMessageType.Close) return;
                    ms.Write(buffer, 0, r.Count);
                } while (!r.EndOfMessage);

                var msg = JsonNode.Parse(Encoding.UTF8.GetString(ms.ToArray()))?.AsObject();
                if (msg == null) continue;
                switch (msg["t"]?.ToString())
                {
                    case "in":
                        if (tab.Running) Input(id, msg["d"]?.ToString() ?? "");
                        break;
                    case "resize":
                        Resize(id, msg["c"]?.GetValue<int>() ?? 0, msg["r"]?.GetValue<int>() ?? 0);
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or InvalidOperationException) { }
        finally
        {
            tab.PcViewers.TryRemove(ws, out _);
            NotifyState(tab);
        }
    }

    private static readonly ConcurrentDictionary<WebSocket, SemaphoreSlim> SendLocks = new();

    private static async Task SendBinaryAsync(WebSocket ws, byte[] data) => await SendAsync(ws, data, WebSocketMessageType.Binary);
    private static async Task SendTextAsync(WebSocket ws, string text) => await SendAsync(ws, Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text);

    private static async Task SendAsync(WebSocket ws, byte[] data, WebSocketMessageType type)
    {
        var l = SendLocks.GetOrAdd(ws, _ => new SemaphoreSlim(1, 1));
        await l.WaitAsync();
        try
        {
            if (ws.State == WebSocketState.Open) await ws.SendAsync(data, type, true, CancellationToken.None);
        }
        catch { /* se cerró */ }
        finally
        {
            l.Release();
            if (ws.State != WebSocketState.Open) SendLocks.TryRemove(ws, out _);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        foreach (var t in _tabs.Values) Stop(t);
        _tabs.Clear();
    }
}
