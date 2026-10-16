using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using PcHub.Agent.Modules;
using PcHub.Agent.Server;

namespace PcHub.Agent;

/// <summary>Núcleo del agente: módulos, sesiones y enrutamiento de mensajes.</summary>
public sealed class Hub : IAsyncDisposable
{
    private delegate Task<JsonNode?> Handler(Session s, JsonObject p);

    /// <param name="Detached">Si es true, se ejecuta en paralelo para no frenar el mouse/teclado mientras tanto.</param>
    private sealed record Route(Handler Fn, bool Detached);

    private readonly Dictionary<string, Route> _routes = new();
    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    private int _failedPairs;

    public AgentConfig Config { get; }
    public string Pin { get; private set; } = "";
    public MediaModule Media { get; }
    public ContextWatcher Context { get; }
    public FocusWatcher Focus { get; }
    public CommandModule Commands { get; }
    public AiModule Ai { get; }
    public MejorasModule Mejoras { get; }
    public TerminalModule Terminal { get; }
    /// <summary>Aviso corto para mostrar en la bandeja.</summary>
    public event Action<string>? TrayMessage;

    private readonly HubServer _server = new();
    private readonly DiscoveryResponder _discovery = new();
    public UdpInput FastInput { get; } = new();

    public event Action? PinChanged;
    public event Action<string>? DevicePaired;
    public event Action? SessionsChanged;

    public int ConnectedCount => _sessions.Values.Count(s => s.Authenticated);
    public IEnumerable<Session> AuthenticatedSessions => _sessions.Values.Where(s => s.Authenticated);

    public Hub(AgentConfig config)
    {
        Config = config;
        PowerShellRunner.UseProfile = config.PowerShellUseProfile;
        NewPin();
        Mejoras = new MejorasModule(this);
        Mejoras.Output += line => Broadcast("mejoras.log", new JsonObject { ["line"] = line });
        Mejoras.StateChanged += state => Broadcast("mejoras.run", state);
        Media = new MediaModule(state => Broadcast("media.state", state));
        Context = new ContextWatcher(config, ctx => Broadcast("ctx.changed", ctx));
        Focus = new FocusWatcher(f => Broadcast("input.focus", f));
        Commands = new CommandModule(config);
        Ai = new AiModule(this);
        Terminal = new TerminalModule(this);
        RegisterRoutes();
    }

    public async Task StartAsync()
    {
        Context.Start();
        Focus.Start();
        await Media.StartAsync();
        await _server.StartAsync(this, Config.Port);
        _discovery.Start(Config.DiscoveryPort, () => Config.PcName, Config.Port);
        FastInput.Start(Config.InputPort);
    }

    private JsonObject AppInfo() => new()
    {
        ["version"] = Installer.VersionLabel,
        ["number"] = Installer.Number,
        ["installed"] = Installer.IsInstalled,
        ["runningInstalled"] = Installer.RunningFromInstall,
        ["startup"] = Installer.StartupEnabled,
        ["ai"] = Ai.ProviderLabel,
    };

    public void NotifyTray(string message) => TrayMessage?.Invoke(message);

    public void NewPin()
    {
        Pin = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        _failedPairs = 0;
        PinChanged?.Invoke();
    }

    // ------------------------------------------------------------------ sesiones

    public void AddSession(Session s) => _sessions[s.Id] = s;

    public void RemoveSession(Session s)
    {
        _sessions.TryRemove(s.Id, out _);
        FastInput.Unregister(s);
        SessionsChanged?.Invoke();
    }

    public void Broadcast(string evt, JsonNode? data)
    {
        var json = $"{{\"t\":\"evt\",\"e\":\"{evt}\",\"d\":{data?.ToJsonString() ?? "null"}}}";
        foreach (var s in _sessions.Values)
            if (s.Authenticated) _ = s.SendRawAsync(json);
    }

    // ------------------------------------------------------------------ mensajes

    public async Task HandleAsync(Session s, string text)
    {
        JsonObject msg;
        try
        {
            msg = JsonNode.Parse(text) as JsonObject ?? throw new JsonException("No es un objeto");
        }
        catch (Exception)
        {
            return; // basura: se ignora
        }

        var method = msg["m"]?.ToString() ?? "";
        long? id = msg["id"] is JsonNode idNode && long.TryParse(idNode.ToString(), out var parsed) ? parsed : null;
        var p = msg["p"] as JsonObject ?? new JsonObject();

        // ---- autenticación ----
        if (method.StartsWith("auth."))
        {
            await HandleAuthAsync(s, method, p, id);
            return;
        }
        if (!s.Authenticated)
        {
            if (id.HasValue) await s.SendErrorAsync(id.Value, "no_auth");
            return;
        }

        if (!_routes.TryGetValue(method, out var route))
        {
            if (id.HasValue) await s.SendErrorAsync(id.Value, $"Método desconocido: {method}");
            return;
        }

        if (route.Detached)
            _ = Task.Run(() => ExecuteAsync(s, method, route, p, id));
        else
            await ExecuteAsync(s, method, route, p, id);
    }

    private static async Task ExecuteAsync(Session s, string method, Route route, JsonObject p, long? id)
    {
        try
        {
            var result = await route.Fn(s, p);
            if (id.HasValue) await s.SendResultAsync(id.Value, result ?? new JsonObject());
        }
        catch (Exception ex)
        {
            if (method != "mouse.move") Log.Error($"Error en {method}", ex);
            if (id.HasValue) await s.SendErrorAsync(id.Value, ex.Message);
        }
    }

    private async Task HandleAuthAsync(Session s, string method, JsonObject p, long? id)
    {
        long rid = id ?? 0;
        switch (method)
        {
            case "auth.hello":
                if (ConfigStore.IsValidToken(Config, S(p, "token")))
                {
                    var dev = Config.Devices.First(d => d.Token == S(p, "token"));
                    s.Authenticated = true;
                    s.DeviceName = dev.Name;
                    await s.SendResultAsync(rid, new JsonObject { ["pcName"] = Config.PcName });
                    await OnAuthenticatedAsync(s);
                }
                else
                {
                    await s.SendErrorAsync(rid, "token_invalido");
                }
                break;

            case "auth.pair":
                if (S(p, "pin") == Pin)
                {
                    var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                    var name = S(p, "device", "Teléfono");
                    lock (Config) Config.Devices.Add(new PairedDevice { Name = name, Token = token });
                    ConfigStore.Save(Config);
                    s.Authenticated = true;
                    s.DeviceName = name;
                    Log.Info($"Dispositivo emparejado: {name}");
                    DevicePaired?.Invoke(name);
                    await s.SendResultAsync(rid, new JsonObject { ["token"] = token, ["pcName"] = Config.PcName });
                    await OnAuthenticatedAsync(s);
                }
                else
                {
                    if (++_failedPairs >= 5) NewPin();
                    await s.SendErrorAsync(rid, "PIN incorrecto");
                }
                break;

            default:
                await s.SendErrorAsync(rid, "Método de autenticación desconocido");
                break;
        }
    }

    private async Task OnAuthenticatedAsync(Session s)
    {
        SessionsChanged?.Invoke();
        await s.SendEventAsync("ctx.changed", Context.Current.DeepClone());
        await s.SendEventAsync("media.state", Media.Current.DeepClone());
    }

    // ------------------------------------------------------------------ rutas

    private void On(string method, Func<Session, JsonObject, Task<JsonNode?>> fn, bool detached = false) =>
        _routes[method] = new Route((s, p) => fn(s, p), detached);

    private void On(string method, Func<JsonObject, JsonNode?> fn, bool detached = false) =>
        _routes[method] = new Route((s, p) => Task.FromResult(fn(p)), detached);

    private void On(string method, Action<JsonObject> fn, bool detached = false) =>
        _routes[method] = new Route((s, p) => { fn(p); return Task.FromResult<JsonNode?>(null); }, detached);

    private void RegisterRoutes()
    {
        On("ping", _ => new JsonObject { ["pong"] = true });

        // Mouse
        On("mouse.move", p => InputModule.MouseMove(I(p, "dx"), I(p, "dy")));
        On("mouse.click", p => InputModule.MouseClick(S(p, "b", "left"), B(p, "double")));
        On("mouse.down", p => InputModule.MouseButton(S(p, "b", "left"), true));
        On("mouse.up", p => InputModule.MouseButton(S(p, "b", "left"), false));
        On("mouse.scroll", p => InputModule.MouseScroll(I(p, "dy"), I(p, "dx")));
        On("mouse.abs", p => InputModule.MouseAbsolute((int)(D(p, "x") * 65535), (int)(D(p, "y") * 65535)));
        On("input.udp", async (s, p) =>
        {
            if (!FastInput.Running) throw new InvalidOperationException("El canal UDP no está disponible");
            var key = FastInput.Register(s);
            return await Task.FromResult<JsonNode?>(new JsonObject { ["port"] = FastInput.Port, ["key"] = key.ToString("x16") });
        });

        // Teclado
        On("kb.text", p => InputModule.TypeText(S(p, "text")));
        On("kb.key", p => InputModule.KeyCombo(S(p, "combo"), I(p, "repeat", 1)));
        On("kb.down", p => InputModule.KeyDown(S(p, "key")));
        On("kb.up", p => InputModule.KeyUp(S(p, "key")));

        // Media
        On("media.get", async (s, p) => await Media.ReadStateAsync(), detached: true);
        On("media.cmd", async (s, p) =>
        {
            double? pos = p["pos"] != null ? D(p, "pos") : null;
            await Media.CommandAsync(S(p, "a"), pos);
            return null;
        }, detached: true);

        // Volumen
        On("vol.get", _ => AudioModule.Get());
        On("vol.set", p => AudioModule.SetLevel(D(p, "level")));
        On("vol.mute", p => AudioModule.SetMute(B(p, "muted")));
        On("vol.apps", _ => AudioModule.ListApps(), detached: true);
        On("vol.app.set", p => AudioModule.SetApp((uint)L(p, "pid"),
            p["level"] != null ? D(p, "level") : null,
            p["muted"] != null ? B(p, "muted") : null));

        // Ventanas
        On("win.list", _ => WindowModule.ListJson(), detached: true);
        On("win.focus", p => WindowModule.Focus(new IntPtr(L(p, "h"))));
        On("win.close", p => WindowModule.Close(new IntPtr(L(p, "h"))));
        On("win.minimize", p => WindowModule.Minimize(new IntPtr(L(p, "h"))));

        // Aplicaciones
        On("apps.list", _ => AppsModule.ListJson(), detached: true);
        On("apps.launch", p => AppsModule.Launch(S(p, "id")), detached: true);

        // Sistema y portapapeles
        On("sys.action", p => SystemModule.Action(S(p, "a")), detached: true);
        On("clip.get", _ => new JsonObject { ["text"] = ClipboardModule.GetText() }, detached: true);
        On("clip.set", p => ClipboardModule.SetText(S(p, "text")), detached: true);

        // Comandos personalizados
        On("cmd.list", _ => Commands.ListJson());
        On("cmd.run", async (s, p) => new JsonObject { ["output"] = await Commands.RunAsync(S(p, "id")) }, detached: true);

        // Terminal con pestañas (PowerShell en la carpeta del usuario)
        On("term.list", _ => Terminal.List());
        On("term.open", p => Terminal.Open(I(p, "cols"), I(p, "rows")));
        On("term.attach", async (s, p) => await Task.FromResult<JsonNode?>(Terminal.Attach(s, S(p, "id"), I(p, "cols"), I(p, "rows"))));
        On("term.detach", async (s, p) => { Terminal.Detach(s); return await Task.FromResult<JsonNode?>(null); });
        On("term.input", p => Terminal.Input(S(p, "id"), S(p, "data")));
        On("term.resize", p => Terminal.Resize(S(p, "id"), I(p, "cols"), I(p, "rows")));
        On("term.restart", p => Terminal.Restart(S(p, "id")));
        On("term.rename", p => Terminal.Rename(S(p, "id"), S(p, "title")));
        On("term.close", p => Terminal.Close(S(p, "id")));
        On("term.showPc", p => Terminal.ShowOnPc(S(p, "id"), B(p, "show")), detached: true);

        // Contexto
        On("ctx.get", _ => Context.Current.DeepClone());

        // App del PC: versión, instalación, inicio con Windows y actualizaciones
        On("app.info", _ => AppInfo());
        On("app.startup", p =>
        {
            Installer.SetStartup(B(p, "enabled"));
            return AppInfo();
        });

        // Instalar la app en el teléfono por depuración inalámbrica (ver AdbTools): el teléfono lo pide y el PC se conecta por adb
        On("phone.install", async (s, p) =>
        {
            var ip = s.Remote?.MapToIPv4().ToString();
            var (ok, message) = await AdbTools.InstallAsync(ip);
            return new JsonObject { ["ok"] = ok, ["message"] = message };
        }, detached: true);
        On("phone.adb", _ => new JsonObject { ["available"] = AdbTools.FindAdb() != null });

        // Mejoras: lista de peticiones para su Claude (ver MejorasModule)
        On("mejoras.list", _ => Mejoras.ListJson());
        On("mejoras.add", p => Mejoras.Add(S(p, "text")));
        On("mejoras.update", p => Mejoras.Update(S(p, "id"), p["text"] != null ? S(p, "text") : null, p["status"] != null ? S(p, "status") : null));
        On("mejoras.delete", p => { Mejoras.Delete(S(p, "id")); return null; });
        On("mejoras.answer", p => Mejoras.Answer(S(p, "id"), S(p, "answers")));
        On("mejoras.run.status", _ => Mejoras.StatusJson());
        On("mejoras.run.start", p => Mejoras.Start(p["ids"] is JsonArray arr ? arr.Select(n => n?.ToString() ?? "").ToList() : null));
        On("mejoras.run.cancel", _ => Mejoras.Cancel());
        On("mejoras.run.clear", _ => { Mejoras.ClearError(); return Mejoras.StatusJson(); });
        On("mejoras.check", async (s, p) => { await Mejoras.CheckRemoteAsync(); return Mejoras.StatusJson(); }, detached: true);
        On("mejoras.sync", _ => Mejoras.Sync());
        On("mejoras.install", _ => Mejoras.Install());
        On("mejoras.undo", p => Mejoras.Undo(S(p, "id")));

        // Excel
        On("excel.status", _ => ExcelModule.Status(), detached: true);
        On("excel.grid", p => ExcelModule.Grid(I(p, "rows", 14), I(p, "cols", 6)), detached: true);
        On("excel.select", p => ExcelModule.Select(p["cell"] != null ? S(p, "cell") : null, I(p, "dRow"), I(p, "dCol")), detached: true);
        On("excel.write", p => ExcelModule.Write(S(p, "value"), S(p, "next", "down")), detached: true);
        On("excel.action", p => ExcelModule.Action(S(p, "a")), detached: true);
        On("excel.sheet", p => ExcelModule.SelectSheet(S(p, "name")), detached: true);
        On("excel.files", _ => ExcelModule.Files(), detached: true);
        On("excel.open", p => ExcelModule.Open(S(p, "path")), detached: true);
        On("excel.new", _ => ExcelModule.New(), detached: true);

        // YouTube / YouTube Music
        On("yt.search", async (s, p) => await YouTubeModule.SearchAsync(S(p, "q"), B(p, "music")), detached: true);
        On("yt.play", async (s, p) => new JsonObject
        {
            ["message"] = await YouTubeModule.PlayAsync(S(p, "id"), B(p, "music"), S(p, "list"), async () =>
            {
                if ((string?)Media.Current["status"] == "playing") await Media.CommandAsync("pause", null);
            }),
        }, detached: true);

        // Enlaces que no son de YouTube: pestaña nueva en el navegador predeterminado (no toca la pestaña de YouTube)
        On("web.open", p => AppsModule.OpenUrl(S(p, "url")), detached: true);

        // Captura de pantalla: vista previa para elegir la zona y copia al portapapeles del PC
        On("screen.preview", _ => ScreenModule.Preview(), detached: true);
        On("screen.capture", p => ScreenModule.Capture(S(p, "mode", "full"), D(p, "x"), D(p, "y"), D(p, "w", 1), D(p, "h", 1)), detached: true);

        // IA
        On("ai.ask", async (s, p) => await Ai.AskAsync(s, S(p, "text")), detached: true);
        On("ai.reset", async (s, p) =>
        {
            await s.AiLock.WaitAsync();
            try { s.AiHistory.Clear(); s.ClaudeSessionId = null; } finally { s.AiLock.Release(); }
            return null;
        }, detached: true);
        On("ai.confirm", async (s, p) =>
        {
            s.ResolveConfirm(S(p, "id"), B(p, "ok"));
            return await Task.FromResult<JsonNode?>(null);
        });
    }

    // ------------------------------------------------------------------ helpers de parámetros

    public static string S(JsonObject p, string k, string def = "") => p[k]?.ToString() ?? def;

    public static double D(JsonObject p, string k, double def = 0) =>
        p[k] is JsonNode n && double.TryParse(n.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : def;

    public static int I(JsonObject p, string k, int def = 0) => (int)Math.Round(D(p, k, def));

    public static long L(JsonObject p, string k, long def = 0) =>
        p[k] is JsonNode n && long.TryParse(n.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : def;

    public static bool B(JsonObject p, string k, bool def = false) =>
        p[k] is JsonNode n && bool.TryParse(n.ToString(), out var b) ? b : def;

    public async ValueTask DisposeAsync()
    {
        Terminal.Dispose();
        Context.Dispose();
        Focus.Dispose();
        Media.Dispose();
        _discovery.Dispose();
        FastInput.Dispose();
        await _server.DisposeAsync();
    }
}
