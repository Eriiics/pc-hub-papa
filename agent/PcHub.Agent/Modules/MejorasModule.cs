using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PcHub.Agent.Modules;

public sealed class Mejora
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    /// <summary>nueva | en_curso | con_preguntas | lista (Claude la hizo, falta instalarla) | hecha | con_problema | descartada</summary>
    public string Status { get; set; } = "nueva";
    /// <summary>Lo que hizo Claude (en sencillo), sus preguntas o el problema.</summary>
    public string Note { get; set; } = "";
    /// <summary>Preguntas de Claude y respuestas de la persona (se le pasan la próxima vez).</summary>
    public string Answers { get; set; } = "";
    /// <summary>Commit de git donde quedó guardada (para poder deshacerla).</summary>
    public string Commit { get; set; } = "";
    public DateTime Created { get; set; } = DateTime.Now;
    public DateTime Updated { get; set; } = DateTime.Now;
}

/// <summary>
/// Lista de mejoras que la persona le pide a su Claude (desde el teléfono o el PC) y quién las va haciendo:
/// una a una o todas, con Claude Code en la carpeta del código (rama main del repositorio de GitHub). Cada mejora queda en su propio commit
/// de git que se sube al repositorio de GitHub (origin/main); al final se sube la versión, se arma el paquete y se instala.
/// </summary>
public sealed class MejorasModule
{
    public static readonly string[] Statuses = { "en_curso", "con_preguntas", "lista", "nueva", "con_problema", "hecha", "descartada" };

    private readonly Hub _hub;
    private readonly object _lock = new();
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public MejorasModule(Hub hub)
    {
        _hub = hub;
        // Si el programa se cerró en medio de un trabajo, esas mejoras vuelven a la lista
        lock (_lock)
        {
            var all = Load();
            var changed = false;
            foreach (var m in all.Where(m => m.Status == "en_curso")) { m.Status = "nueva"; m.Note = "Se interrumpió (se cerró PC Hub). Puedes volver a pedirla."; changed = true; }
            // Las que quedaron «listas» y ya están instaladas pasan a hechas
            var pend = LoadEstado();
            if (pend.Version != "" && Version.TryParse(pend.Version, out var pv) && Version.TryParse(Installer.Number, out var cur) && cur >= pv)
            {
                foreach (var m in all.Where(m => m.Status == "lista")) { m.Status = "hecha"; changed = true; }
                SaveEstado(new Estado());
            }
            if (changed) Save(all);
        }
    }

    // ------------------------------------------------------------------ lista

    private static string FilePath => Path.Combine(ConfigStore.Folder, "mejoras.json");
    private static string EstadoPath => Path.Combine(ConfigStore.Folder, "mejoras-estado.json");

    private sealed class Estado
    {
        public string Version { get; set; } = "";
        public string Exe { get; set; } = "";
    }

    private static Estado LoadEstado()
    {
        try { if (File.Exists(EstadoPath)) return JsonSerializer.Deserialize<Estado>(File.ReadAllText(EstadoPath), Json) ?? new(); }
        catch { }
        return new();
    }

    private static void SaveEstado(Estado e)
    {
        try { File.WriteAllText(EstadoPath, JsonSerializer.Serialize(e, Json)); } catch { }
    }

    private static List<Mejora> Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<List<Mejora>>(File.ReadAllText(FilePath), Json) ?? new();
        }
        catch (Exception ex) { Log.Error("mejoras.json inválido", ex); }
        return new();
    }

    private void Save(List<Mejora> all)
    {
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(all, Json));
        File.Move(tmp, FilePath, true);
    }

    private void Changed() => _hub.Broadcast("mejoras.changed", new JsonObject());

    public JsonObject ListJson()
    {
        lock (_lock)
        {
            var arr = new JsonArray();
            foreach (var m in Load().OrderBy(m => Array.IndexOf(Statuses, m.Status)).ThenBy(m => m.Created)) arr.Add(ToJson(m));
            return new JsonObject { ["mejoras"] = arr };
        }
    }

    public JsonObject Add(string text)
    {
        text = text.Trim();
        if (text.Length == 0) throw new ArgumentException("Escribe qué quieres mejorar.");
        if (text.Length > 4000) text = text[..4000];
        JsonObject res;
        lock (_lock)
        {
            var all = Load();
            var m = new Mejora { Id = DateTime.Now.ToString("yyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..4], Text = text };
            all.Add(m);
            Save(all);
            Log.Info("Mejora nueva: " + (text.Length > 60 ? text[..60] + "…" : text));
            res = ToJson(m);
        }
        Changed();
        return res;
    }

    public JsonObject Update(string id, string? text, string? status)
    {
        JsonObject res;
        lock (_lock)
        {
            var all = Load();
            var m = all.FirstOrDefault(x => x.Id == id) ?? throw new ArgumentException("Esa mejora ya no existe.");
            if (m.Status == "en_curso") throw new InvalidOperationException("Claude la está haciendo ahora. Espera a que termine o cancela.");
            if (text != null && text.Trim().Length > 0) m.Text = text.Trim();
            if (status != null)
            {
                if (!Statuses.Contains(status)) throw new ArgumentException("Estado inválido.");
                m.Status = status;
                if (status == "nueva") { m.Note = ""; }
            }
            m.Updated = DateTime.Now;
            Save(all);
            res = ToJson(m);
        }
        Changed();
        return res;
    }

    public void Delete(string id)
    {
        lock (_lock)
        {
            var all = Load();
            if (all.Any(x => x.Id == id && x.Status == "en_curso")) throw new InvalidOperationException("Claude la está haciendo ahora. Cancela primero.");
            if (all.RemoveAll(x => x.Id == id) > 0) Save(all);
        }
        Changed();
    }

    /// <summary>Respuesta a las preguntas de Claude: se guarda y la mejora queda lista para pedirse de nuevo.</summary>
    public JsonObject Answer(string id, string answers)
    {
        answers = answers.Trim();
        if (answers.Length == 0) throw new ArgumentException("Escribe tu respuesta.");
        JsonObject res;
        lock (_lock)
        {
            var all = Load();
            var m = all.FirstOrDefault(x => x.Id == id) ?? throw new ArgumentException("Esa mejora ya no existe.");
            var sb = new StringBuilder(m.Answers.Length > 0 ? m.Answers.TrimEnd() + "\n\n" : "");
            if (m.Note.Trim().Length > 0) sb.Append("Claude preguntó:\n").Append(m.Note.Trim()).Append('\n');
            sb.Append("La persona respondió:\n").Append(answers);
            m.Answers = sb.ToString();
            m.Note = "Respondiste: lista para que Claude la haga.";
            m.Status = "nueva";
            m.Updated = DateTime.Now;
            Save(all);
            res = ToJson(m);
        }
        Changed();
        return res;
    }

    private void SetStatus(string id, string status, string? note = null, string? commit = null)
    {
        lock (_lock)
        {
            var all = Load();
            var m = all.FirstOrDefault(x => x.Id == id);
            if (m == null) return;
            m.Status = status;
            if (note != null) m.Note = note;
            if (commit != null) m.Commit = commit;
            m.Updated = DateTime.Now;
            Save(all);
        }
        Changed();
    }

    private static JsonObject ToJson(Mejora m) => new()
    {
        ["id"] = m.Id,
        ["text"] = m.Text,
        ["status"] = m.Status,
        ["note"] = m.Note,
        ["commit"] = m.Commit,
        ["created"] = m.Created.ToString("yyyy-MM-dd HH:mm"),
        ["updated"] = m.Updated.ToString("yyyy-MM-dd HH:mm"),
    };

    // ------------------------------------------------------------------ trabajo con Claude

    /// <summary>libre | trabajando | compilando | listo | error</summary>
    private string _state = "libre";
    private string _step = "";
    private string _error = "";
    private string _currentId = "";
    private int _done, _total;
    private readonly List<string> _log = new();
    private CancellationTokenSource? _cts;
    private Process? _proc;
    private Task? _task;

    public event Action<string>? Output;
    public event Action<JsonObject>? StateChanged;

    public bool Busy => _state is "trabajando" or "compilando";

    private void Emit(string line)
    {
        lock (_log) { _log.Add(line); if (_log.Count > 300) _log.RemoveAt(0); }
        Output?.Invoke(line);
    }

    private void SetState(string state, string step = "")
    {
        _state = state;
        _step = step;
        StateChanged?.Invoke(StatusJson());
    }

    public JsonObject StatusJson()
    {
        var pend = LoadEstado();
        var listo = !Busy && pend.Exe != "" && File.Exists(pend.Exe) && Version.TryParse(pend.Version, out var pv)
                    && Version.TryParse(Installer.Number, out var cur) && pv > cur;
        var state = Busy ? _state : listo ? "listo" : _state == "error" ? "error" : "libre";
        JsonArray log;
        lock (_log) log = new JsonArray(_log.TakeLast(40).Select(l => (JsonNode?)l).ToArray());
        return new JsonObject
        {
            ["state"] = state,
            ["step"] = _step,
            ["error"] = _error,
            ["current"] = _currentId,
            ["done"] = _done,
            ["total"] = _total,
            ["version"] = listo ? pend.Version : "",
            ["prueba"] = Program.PruebaMode,
            ["behind"] = _behind,
            ["subjects"] = new JsonArray(_subjects.Select(x => (JsonNode?)x).ToArray()),
            ["log"] = log,
        };
    }

    /// <summary>Carpeta del código: Documentos\PcHub-Codigo (la deja lista el archivo CONFIGURAR-CON-CLAUDE.md).</summary>
    public static string CodeFolder =>
        Environment.GetEnvironmentVariable("PCHUB_CARPETA_CODIGO") is { Length: > 0 } e ? e
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PcHub-Codigo");

    /// <summary>Empieza a trabajar. ids = esas mejoras; sin ids = todas las nuevas.</summary>
    public JsonObject Start(IEnumerable<string>? ids)
    {
        if (Busy) throw new InvalidOperationException("Claude ya está trabajando. Espera a que termine o cancela.");
        List<string> queue;
        lock (_lock)
        {
            var all = Load();
            queue = (ids != null ? all.Where(m => ids.Contains(m.Id) && m.Status is "nueva" or "con_problema")
                                   : all.Where(m => m.Status == "nueva"))
                .OrderBy(m => m.Created).Select(m => m.Id).ToList();
        }
        if (queue.Count == 0) throw new InvalidOperationException("No hay mejoras nuevas para hacer. Escribe una primero.");
        _cts = new CancellationTokenSource();
        _error = "";
        _done = 0; _total = queue.Count;
        lock (_log) _log.Clear();
        SetState("trabajando", "Preparando…");
        var ct = _cts.Token;
        _task = Task.Run(() => RunAsync(queue, ct));
        return StatusJson();
    }

    public JsonObject Cancel()
    {
        _cts?.Cancel();
        try { _proc?.Kill(true); } catch { }
        return StatusJson();
    }

    private async Task RunAsync(List<string> queue, CancellationToken ct)
    {
        try
        {
            var repo = await EnsureCodeAsync(ct);
            var exe = AiModule.FindClaude(_hub.Config.Ai.ClaudeCodePath)
                      ?? throw new Falla("No encontré Claude Code. Clic derecho en el ícono → Configurar asistente IA… → Instalar Claude Code.");
            var (ok, msg) = await AiModule.ClaudeCodeStatusAsync(_hub.Config.Ai.ClaudeCodePath);
            if (!ok) throw new Falla("Falta conectar tu cuenta de Claude. Clic derecho en el ícono → Configurar asistente IA… → Iniciar sesión.\n" + msg);

            int hechas = 0;
            foreach (var id in queue)
            {
                ct.ThrowIfCancellationRequested();
                Mejora? m;
                lock (_lock) m = Load().FirstOrDefault(x => x.Id == id);
                if (m == null) { _done++; continue; }
                _currentId = id;
                SetStatus(id, "en_curso", "Claude la está haciendo…");
                SetState("trabajando", $"Mejora {_done + 1} de {_total}: {Corto(m.Text, 60)}");
                Emit($"▶ Mejora {_done + 1} de {_total}: {Corto(m.Text, 100)}");
                await GitAsync(repo, ct, "add", "-A");
                await GitAsync(repo, ct, "commit", "--quiet", "-m", "Cambios guardados antes de una mejora", "--allow-empty-message");
                var (good, text) = await ClaudeAsync(exe, repo, PromptMejora(m), ct);
                if (!good) { SetStatus(id, "con_problema", Corto(text, 600)); Emit("✖ " + Corto(text, 160)); _done++; continue; }
                var preg = Regex.Match(text, @"PREGUNTAS?:\s*(.+)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                var res = Regex.Match(text, @"RESUMEN:\s*(.+?)(?=\n\s*PREGUNTAS?:|\z)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                var cambios = (await GitAsync(repo, ct, "status", "--porcelain")).Trim();
                if (preg.Success && cambios.Length == 0)
                {
                    SetStatus(id, "con_preguntas", preg.Groups[1].Value.Trim());
                    Emit("❓ Claude tiene una pregunta: " + Corto(preg.Groups[1].Value, 160));
                }
                else if (cambios.Length == 0)
                {
                    SetStatus(id, "con_problema", "Claude no cambió nada. " + Corto(text, 400));
                    Emit("✖ Claude no cambió nada");
                }
                else
                {
                    var resumen = res.Success ? res.Groups[1].Value.Trim() : Corto(text.Split((char)10, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? text, 300);
                    await GitAsync(repo, ct, "add", "-A");
                    await GitAsync(repo, ct, "commit", "--quiet", "-m", "Mejora: " + Corto(m.Text, 70) + "\n\n" + resumen);
                    var sha = (await GitAsync(repo, ct, "rev-parse", "--short", "HEAD")).Trim();
                    SetStatus(id, "lista", resumen, sha);
                    hechas++;
                    Emit("✔ " + Corto(resumen, 160));
                }
                _done++;
            }
            _currentId = "";
            if (hechas > 0) await BuildAsync(repo, exe, ct);
            else SetState("libre", "");
            _hub.NotifyTray(hechas > 0 ? "Tu nueva versión de PC Hub está lista para instalar." : "Claude terminó, pero no hizo cambios nuevos. Mira la lista de mejoras.");
        }
        catch (OperationCanceledException)
        {
            ReponerEnCurso("Cancelada. Puedes volver a pedirla.");
            Emit("■ Cancelado");
            SetState("libre", "");
        }
        catch (Falla f)
        {
            ReponerEnCurso("No se pudo hacer: " + Corto(f.Message, 300));
            _error = f.Message;
            Emit("✖ " + Corto(f.Message, 200));
            SetState("error", "");
        }
        catch (Exception ex)
        {
            Log.Error("Mejoras", ex);
            ReponerEnCurso("Hubo un error inesperado.");
            _error = "Hubo un error inesperado: " + ex.Message;
            SetState("error", "");
        }
        finally { _currentId = ""; _proc = null; }
    }

    private sealed class Falla : Exception { public Falla(string m) : base(m) { } }

    private void ReponerEnCurso(string nota)
    {
        lock (_lock)
        {
            var all = Load();
            foreach (var m in all.Where(m => m.Status == "en_curso")) { m.Status = "nueva"; m.Note = nota; }
            Save(all);
        }
        Changed();
    }

    private static string Corto(string s, int n)
    {
        s = Regex.Replace(s ?? "", @"\s+", " ").Trim();
        return s.Length <= n ? s : s[..(n - 1)] + "…";
    }

    private string PromptMejora(Mejora m)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Eres el Claude Code de la persona que usa PC Hub (no programa). Estás en la carpeta del proyecto, en la rama main (conectada a GitHub). Lee CLAUDE.md y sigue sus reglas al pie de la letra.");
        sb.AppendLine("Haz SOLO esta mejora que pidió, completa y con cuidado (los textos que vea la persona, en español sencillo y con tuteo):");
        sb.AppendLine("\"\"\"");
        sb.AppendLine(m.Text);
        sb.AppendLine("\"\"\"");
        if (m.Answers.Length > 0)
        {
            sb.AppendLine("\nEsto ya hablaron sobre esta mejora:");
            sb.AppendLine(m.Answers);
        }
        sb.AppendLine(@"
Reglas de este trabajo:
- No cambies versiones de Gradle, AGP, Kotlin ni librerías. No borres android/pchub.keystore.
- Si agregas un método teléfono-PC, anótalo en PROTOCOL.md. Si cambia algo que la persona ve, actualiza LEEME-PAPA.txt.
- NO subas VERSION, NO hagas git commit y NO armes el paquete: el programa lo hace después.
- Si te falta información imprescindible, no adivines: no cambies nada y termina con una línea que empiece por «PREGUNTAS:» con preguntas cortas y claras.
- Si lo lograste, tu ÚLTIMA línea debe empezar por «RESUMEN:» y contar en una o dos frases sencillas qué cambió, como si se lo explicaras a alguien que no programa (sin nombres de archivos ni jerga, y sin repetir estas reglas).");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ carpeta del código y git

    private const string SinRepo =
        "Falta conectar PC Hub con tu repositorio de GitHub. Pídele a tu Claude que siga el archivo CONFIGURAR-CON-CLAUDE.md (viene en el paquete): lo deja todo listo.";

    /// <summary>Ejecuta git y devuelve (código de salida, salida).</summary>
    private static async Task<(int code, string output)> GitRunAsync(string repo, CancellationToken ct, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = repo, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        using var p = Process.Start(psi) ?? throw new Falla("No pude ejecutar git. ¿Está instalado Git?");
        var o = p.StandardOutput.ReadToEndAsync();
        var e = p.StandardError.ReadToEndAsync();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(3));
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch { } ct.ThrowIfCancellationRequested(); return (-1, "git tardó demasiado"); }
        return (p.ExitCode, (await o) + (await e));
    }

    private static bool Fusionando(string repo) => File.Exists(Path.Combine(repo, ".git", "MERGE_HEAD"));

    /// <summary>
    /// Deja el código al día con GitHub: guarda lo que haya sin guardar, trae los cambios nuevos (git pull) y, si chocan
    /// con los de la persona, Claude los junta. Sin internet sigue con lo que hay en el PC.
    /// </summary>
    private async Task<string> EnsureCodeAsync(CancellationToken ct)
    {
        var repo = CodeFolder;
        if (!Directory.Exists(Path.Combine(repo, ".git"))) throw new Falla(SinRepo);
        var (cr, remote) = await GitRunAsync(repo, ct, "remote", "get-url", "origin");
        if (cr != 0 || remote.Trim().Length == 0) throw new Falla(SinRepo);

        SetState("trabajando", "Revisando el repositorio…");
        Emit("▶ Trayendo los cambios nuevos del repositorio…");
        await GitRunAsync(repo, ct, "add", "-A");
        await GitRunAsync(repo, ct, "commit", "--quiet", "-m", "Cambios guardados antes de actualizar");
        var (pc, pout) = await GitRunAsync(repo, ct, "pull", "--no-rebase", "--no-edit", "origin", "main");
        if (pc != 0)
        {
            if (Fusionando(repo))
            {
                Emit("▶ Tus cambios y los nuevos tocaron las mismas líneas: Claude los junta…");
                var exeC = AiModule.FindClaude(_hub.Config.Ai.ClaudeCodePath) ?? throw new Falla("No encontré Claude Code.");
                var (ok, t) = await ClaudeAsync(exeC, repo,
                    "Hay una mezcla de git a medias en esta carpeta (git pull con conflictos) porque llegaron cambios nuevos del repositorio y chocaron con los de la persona. " +
                    "Resuélvelos conservando LO QUE LA PERSONA PIDIÓ ANTES y también lo nuevo (en VERSION deja el número más alto). Quita las marcas <<<<<<< ======= >>>>>>>. " +
                    "No hagas commit. Termina con «RESUMEN:» y una frase sencilla.", ct);
                if (!ok) throw new Falla("No pude juntar los cambios nuevos con los tuyos: " + Corto(t, 300));
                await GitRunAsync(repo, ct, "add", "-A");
                await GitRunAsync(repo, ct, "commit", "--quiet", "-m", "Actualización del repositorio (conflictos resueltos)");
            }
            else
            {
                // Sin internet, sin sesión de GitHub o repositorio vacío: se sigue con lo que hay en este PC
                Emit("⚠ No pude traer cambios de GitHub (¿sin internet?). Sigo con lo que hay en este PC.");
                Log.Info("git pull: " + Corto(pout, 300));
            }
        }
        return repo;
    }

    /// <summary>Sube los commits a GitHub (origin/main). Si no se puede, quedan guardados en el PC y se suben la próxima vez.</summary>
    private async Task PushAsync(string repo, CancellationToken ct)
    {
        var (c, o) = await GitRunAsync(repo, ct, "push", "origin", "HEAD:main");
        if (c == 0) { Emit("⬆ Cambios subidos a GitHub"); return; }
        // Alguien subió algo mientras tanto: se trae y se vuelve a intentar
        var (pc, _) = await GitRunAsync(repo, ct, "pull", "--no-rebase", "--no-edit", "origin", "main");
        if (pc == 0)
        {
            (c, o) = await GitRunAsync(repo, ct, "push", "origin", "HEAD:main");
            if (c == 0) { Emit("⬆ Cambios subidos a GitHub"); return; }
        }
        Emit("⚠ No pude subir los cambios a GitHub (¿sin internet o sin sesión?). Quedan guardados en este PC y se subirán la próxima vez.");
        Log.Info("git push: " + Corto(o, 300));
    }

    // ------------------------------------------------------------------ cambios nuevos en el repositorio

    private volatile int _behind;
    private volatile List<string> _subjects = new();
    private System.Threading.Timer? _remoteTimer;

    /// <summary>Revisa cada tanto si hay cambios nuevos en GitHub (git fetch) y avisa para actualizar.</summary>
    public void StartRemoteWatch()
    {
        _remoteTimer ??= new System.Threading.Timer(_ => { _ = CheckRemoteAsync(); }, null, TimeSpan.FromSeconds(45), TimeSpan.FromMinutes(10));
    }

    public async Task CheckRemoteAsync()
    {
        try
        {
            var repo = CodeFolder;
            if (Busy || !Directory.Exists(Path.Combine(repo, ".git"))) return;
            var ct = CancellationToken.None;
            if ((await GitRunAsync(repo, ct, "fetch", "--quiet", "origin", "main")).code != 0) return;
            var (c, n) = await GitRunAsync(repo, ct, "rev-list", "--count", "HEAD..origin/main");
            var behind = c == 0 && int.TryParse(n.Trim(), out var k) ? k : 0;
            var subjects = new List<string>();
            if (behind > 0)
            {
                var (_, log) = await GitRunAsync(repo, ct, "log", "--format=%s", "-n", "5", "HEAD..origin/main");
                subjects = log.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(x => Corto(x, 90)).ToList();
            }
            var before = _behind;
            _behind = behind;
            _subjects = subjects;
            if (behind != before)
            {
                _hub.Broadcast("mejoras.run", StatusJson());
                if (behind > 0) _hub.NotifyTray($"Hay {behind} cambio(s) nuevo(s) en tu repositorio. Toca «Actualizar» en la app o en «Pedir una mejora…».");
            }
        }
        catch (Exception ex) { Log.Error("Mejoras: revisar el repositorio", ex); }
    }

    private static async Task<string> GitAsync(string repo, CancellationToken ct, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = repo, StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new Falla("No pude ejecutar git. ¿Está instalado Git?");
        var outTask = p.StandardOutput.ReadToEndAsync();
        _ = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync(ct);
        return await outTask;
    }

    // ------------------------------------------------------------------ Claude Code

    private async Task<(bool ok, string text)> ClaudeAsync(string exe, string repo, string prompt, CancellationToken ct)
    {
        var args = new List<string>
        {
            "-p", "--output-format", "stream-json", "--verbose",
            "--model", "sonnet",
            "--strict-mcp-config",
            "--max-turns", "120",
            "--permission-mode", "acceptEdits",
            "--allowedTools", "Read,Edit,MultiEdit,Write,Glob,Grep,TodoWrite,Bash(dotnet build:*),Bash(git status:*),Bash(git diff:*),Bash(git log:*)",
            "--disallowedTools", "WebFetch,WebSearch",
        };
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false),
            WorkingDirectory = repo,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        // Siempre con la suscripción: una API key en el entorno se cobraría aparte
        psi.Environment.Remove("ANTHROPIC_API_KEY");
        psi.Environment.Remove("ANTHROPIC_AUTH_TOKEN");
        psi.Environment["CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"] = "1";

        string? result = null;
        bool isError = false;
        var stderr = new StringBuilder();
        using var p = new Process { StartInfo = psi };
        p.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            try
            {
                var node = JsonNode.Parse(e.Data);
                switch (node?["type"]?.ToString())
                {
                    case "assistant":
                        foreach (var c in node["message"]?["content"]?.AsArray() ?? new JsonArray())
                        {
                            var kind = c?["type"]?.ToString();
                            if (kind == "tool_use") Emit(Describir(c!["name"]?.ToString() ?? "", c["input"], repo));
                            else if (kind == "text" && c!["text"]?.ToString() is { Length: > 0 } t && !t.Contains("RESUMEN:"))
                                Emit("💬 " + Corto(t, 160));
                        }
                        break;
                    case "result":
                        result = node["result"]?.ToString() ?? "";
                        isError = node["is_error"]?.GetValue<bool>() == true || node["subtype"]?.ToString() is { } st && st != "success";
                        if (node["subtype"]?.ToString() == "error_max_turns") result = "Claude usó el máximo de pasos sin terminar.";
                        break;
                }
            }
            catch { }
        };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (stderr) stderr.AppendLine(e.Data); };
        try { p.Start(); }
        catch (Exception ex) { return (false, "No se pudo ejecutar Claude Code: " + ex.Message); }
        _proc = p;
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        try { await p.StandardInput.WriteAsync(prompt); p.StandardInput.Close(); } catch { }

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(45));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try { await p.WaitForExitAsync(linked.Token); p.WaitForExit(); }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch { }
            ct.ThrowIfCancellationRequested();
            return (false, "Claude Code tardó más de 45 minutos y se detuvo.");
        }
        finally { _proc = null; }
        ct.ThrowIfCancellationRequested();
        if (result == null)
        {
            string err; lock (stderr) err = stderr.ToString().Trim();
            return (false, err.Length > 0 ? AiModule.FriendlyError(err) : $"Claude Code terminó sin respuesta (código {p.ExitCode}).");
        }
        return (!isError, isError ? AiModule.FriendlyError(result) : result);
    }

    private static string Describir(string tool, JsonNode? input, string repo)
    {
        string Rel(string? p) => string.IsNullOrEmpty(p) ? "" : (p.StartsWith(repo, StringComparison.OrdinalIgnoreCase) ? p[repo.Length..].TrimStart('\\', '/') : p);
        return tool switch
        {
            "Read" => "📖 Lee " + Rel(input?["file_path"]?.ToString()),
            "Edit" or "MultiEdit" => "✏️ Cambia " + Rel(input?["file_path"]?.ToString()),
            "Write" => "📝 Escribe " + Rel(input?["file_path"]?.ToString()),
            "Grep" or "Glob" => "🔎 Busca en el código",
            "Bash" => "⚙️ " + Corto(input?["command"]?.ToString() ?? "", 80),
            _ => "· " + tool,
        };
    }

    // ------------------------------------------------------------------ compilar e instalar

    private async Task BuildAsync(string repo, string claudeExe, CancellationToken ct, bool soloSiHaceFalta = false)
    {
        // Nueva versión: una más que la actual (o que la del código si ya es mayor)
        var vf = Path.Combine(repo, "VERSION");
        Version.TryParse(File.Exists(vf) ? File.ReadAllText(vf).Trim() : "0.0.0", out var codeV);
        Version.TryParse(Installer.Number, out var curV);
        var baseV = codeV != null && curV != null && codeV > curV ? codeV : curV ?? new Version(1, 0, 0);
        // Si lo que llegó del repositorio ya trae una versión más nueva que la instalada, se usa tal cual
        string nueva;
        if (soloSiHaceFalta && codeV != null && curV != null && codeV > curV) nueva = codeV.ToString();
        else
        {
            nueva = new Version(baseV.Major, baseV.Minor, Math.Max(0, baseV.Build) + 1).ToString();
            await File.WriteAllTextAsync(vf, nueva + "\n", ct);
            await GitAsync(repo, ct, "add", "-A");
            await GitAsync(repo, ct, "commit", "--quiet", "-m", "Versión " + nueva);
        }
        await PushAsync(repo, ct);

        for (int intento = 1; intento <= 2; intento++)
        {
            SetState("compilando", $"Armando la versión {nueva} (puede tardar unos minutos)…");
            Emit($"▶ Armando la versión {nueva}…");
            var (code, tail) = await BuildScriptAsync(repo, ct);
            var exe = Path.Combine(repo, "PcHub-Paquete", "PcHub-PC", "PcHub.Agent.exe");
            if (code == 0 && File.Exists(exe))
            {
                SaveEstado(new Estado { Version = nueva, Exe = exe });
                Emit($"✔ Versión {nueva} lista para instalar");
                _error = "";
                SetState("libre", "");
                return;
            }
            Emit("✖ Falló al armar: " + Corto(tail, 200));
            if (intento == 2) throw new Falla("No se pudo armar la versión nueva. Lo último que dijo: " + Corto(tail, 500));
            SetState("trabajando", "Claude está corrigiendo un error…");
            Emit("▶ Claude revisa el error y lo corrige…");
            var (ok, t) = await ClaudeAsync(claudeExe, repo,
                "Al armar el paquete de PC Hub (Crear-paquete.ps1) falló esto:\n\n" + tail +
                "\n\nCorrígelo con el cambio mínimo, sin subir versiones de Gradle/AGP/Kotlin/librerías. No hagas commit. Termina con «RESUMEN:» y una frase sencilla.", ct);
            if (!ok) throw new Falla("No se pudo armar la versión nueva y Claude no logró corregirlo: " + Corto(t, 300));
            await GitAsync(repo, ct, "add", "-A");
            await GitAsync(repo, ct, "commit", "--quiet", "-m", "Corrección al armar la versión " + nueva);
        }
    }

    private async Task<(int code, string tail)> BuildScriptAsync(string repo, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = repo,
            Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"[Console]::OutputEncoding=[Text.Encoding]::UTF8; & '" + Path.Combine(repo, "Crear-paquete.ps1").Replace("'", "''") + "' -SinAbrir; exit $LASTEXITCODE\"",
        };
        var all = new List<string>();
        using var p = new Process { StartInfo = psi };
        void Line(string? l)
        {
            if (string.IsNullOrWhiteSpace(l)) return;
            lock (all) { all.Add(l.Trim()); if (all.Count > 400) all.RemoveAt(0); }
            if (l.StartsWith("1/") || l.StartsWith("2/") || l.StartsWith("3/") || l.Contains("error", StringComparison.OrdinalIgnoreCase) || l.Contains("BUILD"))
                Emit("   " + Corto(l, 140));
        }
        p.OutputDataReceived += (_, e) => Line(e.Data);
        p.ErrorDataReceived += (_, e) => Line(e.Data);
        p.Start();
        _proc = p;
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        try { await p.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch { } throw; }
        finally { _proc = null; }
        string tail; lock (all) tail = string.Join("\n", all.TakeLast(60));
        return (p.ExitCode, tail);
    }

    /// <summary>
    /// Actualiza desde GitHub: trae los cambios nuevos (los de quien sea), junta los de la persona, arma la versión
    /// y la instala (PC Hub se reinicia y el teléfono se actualiza).
    /// </summary>
    public JsonObject Sync()
    {
        if (Busy) throw new InvalidOperationException("Claude está trabajando. Espera a que termine.");
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _error = ""; _done = 0; _total = 0;
        lock (_log) _log.Clear();
        SetState("trabajando", "Actualizando desde el repositorio…");
        _task = Task.Run(async () =>
        {
            try
            {
                var repo = CodeFolder;
                var antes = Directory.Exists(Path.Combine(repo, ".git")) ? (await GitRunAsync(repo, ct, "rev-parse", "HEAD")).output.Trim() : "";
                await EnsureCodeAsync(ct);
                var despues = (await GitRunAsync(repo, ct, "rev-parse", "HEAD")).output.Trim();
                _behind = 0; _subjects = new();
                if (antes != despues)
                {
                    var exe = AiModule.FindClaude(_hub.Config.Ai.ClaudeCodePath) ?? "";
                    Emit("▶ Cambios nuevos recibidos. Armando la versión…");
                    await BuildAsync(repo, exe, ct, soloSiHaceFalta: true);
                    if (!Program.PruebaMode && StatusJson()["state"]!.ToString() == "listo") Install();
                }
                else { Emit("✔ Ya tienes todo al día."); SetState("libre", ""); }
            }
            catch (OperationCanceledException) { SetState("libre", ""); }
            catch (Falla f) { _error = f.Message; Emit("✖ " + f.Message); SetState("error", ""); }
            catch (Exception ex) { Log.Error("Mejoras: actualizar", ex); _error = "Hubo un error inesperado: " + ex.Message; SetState("error", ""); }
        });
        return StatusJson();
    }

    /// <summary>Abre el PcHub.Agent.exe nuevo: se instala solo (sin preguntar) y se reinicia con la versión nueva.</summary>
    public JsonObject Install()
    {
        var pend = LoadEstado();
        if (pend.Exe == "" || !File.Exists(pend.Exe)) throw new InvalidOperationException("No hay una versión nueva lista para instalar.");
        if (Program.PruebaMode) throw new InvalidOperationException("Estás en una sesión de prueba: aquí no se instala nada. En tu PC Hub de siempre sí se instalaría.");
        Emit("▶ Instalando la versión " + pend.Version + "… PC Hub se reinicia solo y el teléfono se reconecta.");
        Process.Start(new ProcessStartInfo(pend.Exe, "--actualizar") { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(pend.Exe)! });
        return new JsonObject { ["installing"] = true };
    }

    /// <summary>Deshace una mejora ya guardada (git revert) y arma una versión nueva sin ella.</summary>
    public JsonObject Undo(string id)
    {
        if (Busy) throw new InvalidOperationException("Claude está trabajando. Espera a que termine.");
        string sha;
        lock (_lock)
        {
            var m = Load().FirstOrDefault(x => x.Id == id) ?? throw new ArgumentException("Esa mejora ya no existe.");
            if (m.Commit == "") throw new InvalidOperationException("Esa mejora no tiene un cambio guardado que deshacer.");
            sha = m.Commit;
        }
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _error = ""; _done = 0; _total = 1;
        lock (_log) _log.Clear();
        SetState("trabajando", "Deshaciendo la mejora…");
        _task = Task.Run(async () =>
        {
            try
            {
                var repo = CodeFolder;
                var exe = AiModule.FindClaude(_hub.Config.Ai.ClaudeCodePath) ?? "";
                Emit("▶ Deshaciendo el cambio " + sha);
                var psi = new ProcessStartInfo("git") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true, WorkingDirectory = repo };
                foreach (var a in new[] { "revert", "--no-edit", sha }) psi.ArgumentList.Add(a);
                using (var p = Process.Start(psi)!)
                {
                    await p.WaitForExitAsync(ct);
                    if (p.ExitCode != 0)
                    {
                        var psi2 = new ProcessStartInfo("git") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = repo };
                        psi2.ArgumentList.Add("revert"); psi2.ArgumentList.Add("--abort");
                        Process.Start(psi2)?.WaitForExit();
                        throw new Falla("No pude deshacerla sola porque otros cambios dependen de ella. Pídele a Claude (menú del ícono → Abrir Claude con el código) que la quite.");
                    }
                }
                SetStatus(id, "descartada", "Deshecha.");
                await BuildAsync(repo, exe, ct);
            }
            catch (OperationCanceledException) { SetState("libre", ""); }
            catch (Falla f) { _error = f.Message; Emit("✖ " + f.Message); SetState("error", ""); }
            catch (Exception ex) { Log.Error("Mejoras: deshacer", ex); _error = "Hubo un error inesperado: " + ex.Message; SetState("error", ""); }
        });
        return StatusJson();
    }

    public void ClearError()
    {
        if (_state == "error") { _error = ""; SetState("libre", ""); }
    }
}
