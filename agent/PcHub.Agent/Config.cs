using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcHub.Agent;

public sealed class AgentConfig
{
    public string PcName { get; set; } = Environment.MachineName;
    public int Port { get; set; } = 47800;
    public int DiscoveryPort { get; set; } = 47801;
    /// <summary>Puerto UDP del canal rápido de mouse (touchpad y puntero).</summary>
    public int InputPort { get; set; } = 47802;
    /// <summary>Cargar tu perfil de PowerShell ($PROFILE) al ejecutar scripts, para usar tus funciones y alias.</summary>
    public bool PowerShellUseProfile { get; set; } = false;
    public List<PairedDevice> Devices { get; set; } = new();
    public AiConfig Ai { get; set; } = new();
    /// <summary>Versión de las reglas de contexto de fábrica (para sumar las nuevas sin pisar las tuyas).</summary>
    public int RulesVersion { get; set; }
    public List<ContextRule> ContextRules { get; set; } = new();
    public List<CustomCommand> Commands { get; set; } = new();
}

public sealed class PairedDevice
{
    public string Name { get; set; } = "";
    public string Token { get; set; } = "";
    public DateTime PairedAt { get; set; } = DateTime.Now;
}

public sealed class AiConfig
{
    /// <summary>
    /// Cómo conectarse a Claude:
    ///  "claude-code" = Claude Code (CLI) con tu suscripción de claude.ai, sin API key ni costo extra.
    ///  "api"         = API de Claude con API key (se paga por uso).
    ///  "auto"        = API si hay key guardada; si no, Claude Code.
    /// </summary>
    public string Provider { get; set; } = "auto";
    /// <summary>Modelo para Claude Code: alias "haiku", "sonnet", "opus" o un id completo.</summary>
    public string ClaudeCodeModel { get; set; } = "haiku";
    /// <summary>Ruta de claude.exe / claude.cmd. Vacío = buscarlo automáticamente.</summary>
    public string ClaudeCodePath { get; set; } = "";
    /// <summary>
    /// API key en texto plano (opcional). Lo normal es configurarla desde la bandeja → "Configurar asistente IA…",
    /// que la guarda cifrada en ApiKeyProtected. Si ambas están vacías se usa la variable de entorno ANTHROPIC_API_KEY.
    /// </summary>
    public string ApiKey { get; set; } = "";
    /// <summary>API key cifrada con DPAPI (solo tu usuario de Windows en este PC puede descifrarla).</summary>
    public string ApiKeyProtected { get; set; } = "";
    public string Model { get; set; } = "claude-haiku-5-5";
    public int MaxTokens { get; set; } = 1024;
    /// <summary>Si es false, la IA no puede ejecutar PowerShell aunque lo apruebes.</summary>
    public bool AllowPowerShell { get; set; } = true;
    /// <summary>Instrucciones extra que quieras darle a la IA (ej. "mi navegador es Chrome").</summary>
    public string ExtraInstructions { get; set; } = "";

    public string ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(ApiKeyProtected))
        {
            try
            {
                var bytes = System.Security.Cryptography.ProtectedData.Unprotect(
                    Convert.FromBase64String(ApiKeyProtected), null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                return System.Text.Encoding.UTF8.GetString(bytes).Trim();
            }
            catch (Exception ex)
            {
                Log.Error("No se pudo descifrar la API key (¿config copiada de otro PC o usuario?)", ex);
            }
        }
        if (!string.IsNullOrWhiteSpace(ApiKey)) return ApiKey.Trim();
        return (Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "").Trim();
    }

    public void StoreApiKey(string key)
    {
        key = key.Trim();
        ApiKey = "";
        ApiKeyProtected = key.Length == 0 ? "" : Convert.ToBase64String(System.Security.Cryptography.ProtectedData.Protect(
            System.Text.Encoding.UTF8.GetBytes(key), null, System.Security.Cryptography.DataProtectionScope.CurrentUser));
    }

    public bool HasApiKey => ResolveApiKey().Length > 0;
}

/// <summary>
/// Regla de contexto: si la ventana activa coincide, el teléfono abre <see cref="Module"/>.
/// Process y Title son "contiene" sin distinguir mayúsculas. Si ambos están, deben cumplirse los dos.
/// </summary>
public sealed class ContextRule
{
    public string Name { get; set; } = "";
    public string? Process { get; set; }
    public string? Title { get; set; }
    public string Module { get; set; } = "media";
}

/// <summary>
/// Comando personalizado que aparece en el hub del teléfono.
/// Type: keys | text | launch | url | powershell | sequence
///   keys       → Value = "ctrl+shift+esc"
///   text       → Value = texto a escribir
///   launch     → Value = ruta del programa, Args = argumentos
///   url        → Value = https://...
///   powershell → Value = script
///   sequence   → Value = líneas "keys:win+r", "wait:400", "text:notepad", "keys:enter"
/// </summary>
public sealed class CustomCommand
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "⚡";
    public string Type { get; set; } = "keys";
    public string Value { get; set; } = "";
    public string? Args { get; set; }
    public bool Confirm { get; set; }
}

public static class ConfigStore
{
    private static readonly object Lock = new();

    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Program.PruebaMode ? "PcHub-Prueba" : "PcHub");

    public static string FilePath => Path.Combine(Folder, "config.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static AgentConfig Load()
    {
        Directory.CreateDirectory(Folder);
        AgentConfig? cfg = null;
        if (File.Exists(FilePath))
        {
            try
            {
                cfg = JsonSerializer.Deserialize<AgentConfig>(File.ReadAllText(FilePath), Options);
            }
            catch (Exception ex)
            {
                Log.Error("config.json inválido, se respalda y se usa uno nuevo", ex);
                try { File.Copy(FilePath, FilePath + ".bak", true); } catch { }
            }
        }

        if (cfg == null)
        {
            cfg = new AgentConfig();
            cfg.ContextRules = DefaultRules();
            cfg.Commands = DefaultCommands();
            cfg.RulesVersion = CurrentRulesVersion;
            Save(cfg);
        }
        else if (cfg.RulesVersion < CurrentRulesVersion)
        {
            // Reglas nuevas de fábrica (FL Studio, OBS) sin tocar las que ya tienes
            foreach (var r in DefaultRules().Where(r => cfg.ContextRules.All(x => x.Name != r.Name)))
                cfg.ContextRules.Add(r);
            cfg.RulesVersion = CurrentRulesVersion;
            Save(cfg);
        }
        return cfg;
    }

    public static void Save(AgentConfig cfg)
    {
        lock (Lock)
        {
            Directory.CreateDirectory(Folder);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(cfg, Options));
            File.Move(tmp, FilePath, true);
        }
    }

    private const int CurrentRulesVersion = 2;

    private static List<ContextRule> DefaultRules() => new()
    {
        new() { Name = "FL Studio", Process = "fl64", Module = "flstudio" },
        new() { Name = "OBS", Process = "obs64", Module = "obs" },
        new() { Name = "YouTube Music", Title = "YouTube Music", Module = "media" },
        new() { Name = "Spotify", Process = "spotify", Module = "media" },
        new() { Name = "VLC", Process = "vlc", Module = "media" },
        new() { Name = "Netflix", Title = "Netflix", Module = "media" },
        new() { Name = "YouTube", Title = "- YouTube", Module = "media" },
    };

    private static List<CustomCommand> DefaultCommands() => new()
    {
        new() { Id = "ytmusic", Name = "YouTube Music", Icon = "🎵", Type = "url", Value = "https://music.youtube.com" },
        new() { Id = "taskmgr", Name = "Administrador de tareas", Icon = "📊", Type = "keys", Value = "ctrl+shift+esc" },
        new() { Id = "snip", Name = "Recorte de pantalla", Icon = "✂️", Type = "keys", Value = "win+shift+s" },
        new() { Id = "explorer", Name = "Explorador", Icon = "📁", Type = "keys", Value = "win+e" },
        new() { Id = "notepad", Name = "Bloc de notas", Icon = "📝", Type = "sequence", Value = "keys:win+r\nwait:500\ntext:notepad\nkeys:enter" },
        new() { Id = "ip", Name = "IP local", Icon = "🌐", Type = "powershell",
            Value = "(Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.InterfaceAlias -notmatch 'Loopback' -and $_.IPAddress -notlike '169.*' }).IPAddress" },
    };

    public static bool IsValidToken(AgentConfig cfg, string? token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        lock (Lock) return cfg.Devices.Any(d => d.Token == token);
    }
}
