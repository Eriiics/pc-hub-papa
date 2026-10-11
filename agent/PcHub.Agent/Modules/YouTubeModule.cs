using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using PcHub.Agent.Native;

namespace PcHub.Agent.Modules;

/// <summary>
/// Buscar en YouTube / YouTube Music (sin API key, con la API interna que usa la propia web) y reproducir el
/// resultado en la pestaña de YouTube que esté abierta en el navegador (si no hay, abre una nueva).
/// La API interna de YouTube puede cambiar; si una búsqueda deja de funcionar, es aquí donde hay que ajustar.
/// </summary>
public static class YouTubeModule
{
    private static readonly HttpClient Http = new(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(15) };
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

    /// <summary>Filtro "solo canciones" de YouTube Music.</summary>
    private const string MusicSongsFilter = "EgWKAQIIAWoMEA4QChADEAQQCRAF";

    private static readonly string[] Browsers = { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc", "zen", "librewolf", "thorium" };

    // ------------------------------------------------------------------ búsqueda

    public static async Task<JsonObject> SearchAsync(string query, bool music)
    {
        query = query.Trim();
        if (query.Length == 0) throw new ArgumentException("Escribe qué buscar");

        List<JsonObject> results = new();
        string? error = null;
        try
        {
            results = music ? await SearchMusicAsync(query) : await SearchVideosAsync(query);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Log.Error($"Búsqueda {(music ? "YouTube Music" : "YouTube")}", ex);
        }

        // Si YouTube Music falla o no devuelve canciones, se usa la búsqueda normal de YouTube
        if (music && results.Count == 0)
        {
            try { results = await SearchVideosAsync(query); }
            catch (Exception ex) { error ??= ex.Message; }
        }
        if (results.Count == 0 && error != null) throw new InvalidOperationException("No se pudo buscar en YouTube: " + error);

        return new JsonObject { ["results"] = new JsonArray(results.Take(25).Cast<JsonNode?>().ToArray()) };
    }

    private static async Task<List<JsonObject>> SearchVideosAsync(string query)
    {
        var body = new JsonObject
        {
            ["context"] = new JsonObject { ["client"] = new JsonObject { ["clientName"] = "WEB", ["clientVersion"] = "2.20240918.01.00", ["hl"] = "es", ["gl"] = "CL" } },
            ["query"] = query,
        };
        JsonNode? root;
        try
        {
            root = await PostInnerTubeAsync("https://www.youtube.com/youtubei/v1/search?prettyPrint=false", body, "1", "2.20240918.01.00", "https://www.youtube.com");
        }
        catch
        {
            root = await ScrapeSearchPageAsync(query); // respaldo: la página HTML trae los mismos datos
        }

        var list = new List<JsonObject>();
        foreach (var vr in FindAll(root, "videoRenderer"))
        {
            var id = vr["videoId"]?.ToString();
            if (string.IsNullOrEmpty(id)) continue;
            list.Add(new JsonObject
            {
                ["id"] = id,
                ["title"] = Text(vr["title"]),
                ["channel"] = Text(vr["ownerText"]) ?? Text(vr["longBylineText"]),
                ["duration"] = Text(vr["lengthText"]) ?? "",
                ["views"] = Text(vr["shortViewCountText"]) ?? "",
                ["thumb"] = $"https://i.ytimg.com/vi/{id}/mqdefault.jpg",
            });
        }
        // El mismo video puede aparecer en varias secciones de resultados
        return list.GroupBy(r => r["id"]!.ToString()).Select(g => g.First()).ToList();
    }

    private static async Task<List<JsonObject>> SearchMusicAsync(string query)
    {
        const string version = "1.20240918.01.00";
        var body = new JsonObject
        {
            ["context"] = new JsonObject { ["client"] = new JsonObject { ["clientName"] = "WEB_REMIX", ["clientVersion"] = version, ["hl"] = "es", ["gl"] = "CL" } },
            ["query"] = query,
            ["params"] = MusicSongsFilter,
        };
        var root = await PostInnerTubeAsync("https://music.youtube.com/youtubei/v1/search?prettyPrint=false", body, "67", version, "https://music.youtube.com");

        var list = new List<JsonObject>();
        foreach (var item in FindAll(root, "musicResponsiveListItemRenderer"))
        {
            var id = item["playlistItemData"]?["videoId"]?.ToString() ?? FindFirstString(item, "videoId");
            if (string.IsNullOrEmpty(id)) continue; // artistas, álbumes, listas: no se pueden reproducir directo
            var cols = item["flexColumns"] as JsonArray;
            string? Col(int i) => cols != null && cols.Count > i
                ? Text(cols[i]?["musicResponsiveListItemFlexColumnRenderer"]?["text"])
                : null;
            var title = Col(0) ?? "";
            var subtitle = Col(1) ?? "";
            // Subtítulo típico: "Canción • Artista • Álbum • 3:45"
            var parts = subtitle.Split('•', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
            var duration = parts.LastOrDefault(p => Regex.IsMatch(p, @"^\d{1,2}:\d{2}(:\d{2})?$")) ?? "";
            var artist = parts.Where(p => p != duration && p is not ("Canción" or "Song" or "Video")).FirstOrDefault() ?? "";
            var thumb = (item["thumbnail"]?["musicThumbnailRenderer"]?["thumbnail"]?["thumbnails"] as JsonArray)?.LastOrDefault()?["url"]?.ToString()
                        ?? $"https://i.ytimg.com/vi/{id}/mqdefault.jpg";
            list.Add(new JsonObject
            {
                ["id"] = id,
                ["title"] = title,
                ["channel"] = artist,
                ["duration"] = duration,
                ["views"] = "",
                ["thumb"] = thumb,
            });
        }
        return list.GroupBy(r => r["id"]!.ToString()).Select(g => g.First()).ToList();
    }

    private static async Task<JsonNode?> PostInnerTubeAsync(string url, JsonObject body, string clientName, string clientVersion, string origin)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        req.Headers.TryAddWithoutValidation("Origin", origin);
        req.Headers.TryAddWithoutValidation("Referer", origin + "/");
        req.Headers.TryAddWithoutValidation("X-YouTube-Client-Name", clientName);
        req.Headers.TryAddWithoutValidation("X-YouTube-Client-Version", clientVersion);
        req.Headers.TryAddWithoutValidation("Accept-Language", "es-CL,es;q=0.9");
        req.Headers.TryAddWithoutValidation("Cookie", "SOCS=CAI; CONSENT=YES+1");
        req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var resp = await Http.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"YouTube respondió {(int)resp.StatusCode}");
        return JsonNode.Parse(text);
    }

    private static async Task<JsonNode?> ScrapeSearchPageAsync(string query)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://www.youtube.com/results?hl=es&gl=CL&search_query=" + Uri.EscapeDataString(query));
        req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        req.Headers.TryAddWithoutValidation("Accept-Language", "es-CL,es;q=0.9");
        req.Headers.TryAddWithoutValidation("Cookie", "SOCS=CAI; CONSENT=YES+1");
        using var resp = await Http.SendAsync(req);
        var html = await resp.Content.ReadAsStringAsync();
        var m = Regex.Match(html, @"var ytInitialData\s*=\s*(\{.+?\});\s*</script>", RegexOptions.Singleline);
        if (!m.Success) throw new InvalidOperationException("No se pudo leer la página de resultados de YouTube");
        return JsonNode.Parse(m.Groups[1].Value);
    }

    // ------------------------------------------------------------------ reproducir

    /// <summary>
    /// Reproduce un video (o una lista) en la pestaña de YouTube / YouTube Music abierta: enfoca esa ventana del
    /// navegador, Ctrl+L, escribe la URL y Enter. Si no hay ninguna, lo abre en el navegador predeterminado.
    /// Si el video estaba en pantalla completa, primero sale (Esc) —si no, las teclas le llegan al reproductor— y al
    /// cargar el nuevo vuelve a pantalla completa.
    /// <paramref name="list"/> es opcional: con video = el video dentro de la lista; sin video = la lista completa.
    /// </summary>
    public static async Task<string> PlayAsync(string videoId, bool music, string list = "", Func<Task>? pauseIfPlaying = null)
    {
        bool hasVideo = videoId.Length > 0;
        bool hasList = list.Length > 0;
        if (hasVideo && !Regex.IsMatch(videoId, @"^[A-Za-z0-9_-]{6,20}$")) throw new ArgumentException("Video inválido");
        if (hasList && !Regex.IsMatch(list, @"^[A-Za-z0-9_-]{8,80}$")) throw new ArgumentException("Lista inválida");
        if (!hasVideo && !hasList) throw new ArgumentException("Falta el video o la lista");

        string PathFor() => hasVideo
            ? $"/watch?v={videoId}" + (hasList ? $"&list={list}" : "")
            : $"/playlist?list={list}";
        string UrlFor(bool m) => (m ? "https://music.youtube.com" : "https://www.youtube.com") + PathFor();
        var url = UrlFor(music);

        var windows = WindowModule.List();

        // Música: se abre en la app (PWA) de YouTube Music; si hay un video de YouTube sonando, se pausa antes
        if (music) return await PlayInMusicAppAsync(url, windows, pauseIfPlaying);

        var target = windows.FirstOrDefault(w => IsBrowserTab(w) && (music ? IsMusicTitle(w.Title) : IsVideoTitle(w.Title)))
                     // Si se busca en YouTube y no hay pestaña de YouTube, pero sí de YouTube Music (o al revés), se usa esa
                     ?? windows.FirstOrDefault(w => IsBrowserTab(w) && (IsMusicTitle(w.Title) || IsVideoTitle(w.Title)));

        if (target == null)
        {
            AppsModule.Start(url);
            return "No había una pestaña de YouTube abierta: se abrió en el navegador.";
        }

        // Los títulos de las pestañas de YouTube Music no cambian si es otra pestaña de YouTube: usar el dominio correcto
        url = UrlFor(IsMusicTitle(target.Title));

        WindowModule.Focus(target.Handle);
        await Task.Delay(250);
        if (Win32.GetForegroundWindow() != target.Handle)
        {
            AppsModule.Start(url);
            return "No se pudo traer el navegador al frente: se abrió en una pestaña nueva.";
        }
        // Pantalla completa del video: Ctrl+L no muestra la barra de direcciones y lo escrito le llega a YouTube
        // (que lo toma como atajos). Esc sale de la pantalla completa del video.
        bool wasFullscreen = Win32.IsFullscreen(target.Handle);
        if (wasFullscreen)
        {
            InputModule.KeyCombo("esc");
            for (int i = 0; i < 10 && Win32.IsFullscreen(target.Handle); i++) await Task.Delay(100);
            if (Win32.IsFullscreen(target.Handle))
            {
                // Sigue en pantalla completa (F11 del navegador): F11 la quita
                InputModule.KeyCombo("f11");
                for (int i = 0; i < 10 && Win32.IsFullscreen(target.Handle); i++) await Task.Delay(100);
            }
            await Task.Delay(150);
        }

        InputModule.KeyCombo("ctrl+l");
        await Task.Delay(120);
        InputModule.TypeText(url);
        await Task.Delay(60);
        InputModule.KeyCombo("enter");

        bool isMusicTab = IsMusicTitle(target.Title);
        if (wasFullscreen && !isMusicTab && hasVideo)
            _ = RestoreFullscreenAsync(target.Handle, target.Title);
        return "Reproduciendo en la pestaña de " + (isMusicTab ? "YouTube Music." : "YouTube.");
    }

    /// <summary>
    /// Abre la canción en la app de YouTube Music instalada (PWA de Chrome/Edge). Las PWA no tienen barra de
    /// direcciones, así que se lanza el navegador con --app=URL (Chrome/Edge lo abren como la app instalada) y
    /// se cierra la ventana anterior de la app. Si hay un video de YouTube en una pestaña, se pausa primero.
    /// </summary>
    private static async Task<string> PlayInMusicAppAsync(string url, List<WindowInfo> windows, Func<Task>? pauseIfPlaying)
    {
        var videoTab = windows.FirstOrDefault(w => IsBrowserTab(w) && IsVideoTitle(w.Title));
        bool paused = false;
        if (videoTab != null && pauseIfPlaying != null)
        {
            try { await pauseIfPlaying(); paused = true; }
            catch (Exception ex) { Log.Error("Pausar el video de YouTube", ex); }
        }

        var oldApp = windows.FirstOrDefault(w => IsMusicApp(w));
        var exe = new[] { oldApp?.ExePath, windows.FirstOrDefault(w => IsBrowserTab(w) && IsMusicTitle(w.Title))?.ExePath, videoTab?.ExePath }
            .FirstOrDefault(p => p != null && SupportsAppMode(p)) ?? FindAppModeBrowser();
        if (exe == null)
        {
            AppsModule.Start(url);
            return "No encontré Chrome ni Edge para abrir la app de YouTube Music: se abrió en el navegador.";
        }

        AppsModule.Start(exe, $"--app=\"{url}\"");
        if (oldApp != null)
        {
            // Cuando la ventana nueva ya cargó, se cierra la vieja para no dejar dos
            await Task.Delay(1800);
            var oldHandle = oldApp.Handle;
            if (WindowModule.List().Any(w => w.Handle == oldHandle)) WindowModule.Close(oldHandle);
        }
        return "Reproduciendo en la app de YouTube Music." + (paused ? " Se pausó el video de YouTube." : "");
    }

    /// <summary>Ventana de la app (PWA) de YouTube Music: es de un navegador pero sin el nombre del navegador en el título.</summary>
    private static bool IsMusicApp(WindowInfo w) =>
        Browsers.Contains(w.Process.ToLowerInvariant()) && IsMusicTitle(w.Title) && !IsBrowserTab(w);

    private static bool SupportsAppMode(string exePath) =>
        System.IO.Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant() is "chrome" or "msedge" or "brave" or "vivaldi" or "opera";

    private static string? FindAppModeBrowser()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        };
        var relative = new[]
        {
            @"Google\Chrome\Application\chrome.exe",
            @"Microsoft\Edge\Application\msedge.exe",
        };
        foreach (var rel in relative)
            foreach (var root in roots)
            {
                var path = System.IO.Path.Combine(root, rel);
                if (System.IO.File.Exists(path)) return path;
            }
        return null;
    }

    /// <summary>
    /// Cuando cambia el título de la pestaña (cargó el video nuevo), vuelve a pantalla completa con la tecla "f"
    /// de YouTube. Si en 8 s no cargó, no hace nada.
    /// </summary>
    private static async Task RestoreFullscreenAsync(IntPtr hwnd, string oldTitle)
    {
        try
        {
            for (int i = 0; i < 40; i++)
            {
                await Task.Delay(200);
                var title = WindowModule.List().FirstOrDefault(w => w.Handle == hwnd)?.Title;
                if (title == null) return;
                if (title != oldTitle && IsVideoTitle(title))
                {
                    await Task.Delay(1200); // que el reproductor esté listo
                    if (Win32.GetForegroundWindow() != hwnd || Win32.IsFullscreen(hwnd)) return;
                    InputModule.KeyCombo("f");
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Volver a pantalla completa", ex);
        }
    }

    /// <summary>
    /// Ventana normal de un navegador (no una app instalada/PWA, que no tiene barra de direcciones).
    /// En Chrome, Edge y Brave las PWA usan el mismo proceso: se distinguen porque las ventanas normales
    /// terminan con el nombre del navegador ("… - Google Chrome").
    /// </summary>
    private static bool IsBrowserTab(WindowInfo w)
    {
        var proc = w.Process.ToLowerInvariant();
        if (!Browsers.Contains(proc)) return false;
        var title = new string(w.Title.Where(c => c != '\u200b').ToArray()).TrimEnd();
        return proc switch
        {
            "chrome" => title.EndsWith("Google Chrome") || title.EndsWith("Chromium"),
            "msedge" => title.EndsWith("Microsoft Edge"),
            "brave" => title.EndsWith("Brave"),
            "opera" => title.EndsWith("Opera") || title.Contains("Opera"),
            _ => true,
        };
    }

    private static bool IsMusicTitle(string title) => title.Contains("YouTube Music", StringComparison.OrdinalIgnoreCase);

    private static bool IsVideoTitle(string title) =>
        !IsMusicTitle(title) && Regex.IsMatch(title, @"(^|[\s\-–])YouTube(\s|$|[\s\-–])", RegexOptions.IgnoreCase);

    // ------------------------------------------------------------------ JSON

    /// <summary>Todos los objetos que estén bajo una clave dada, a cualquier profundidad.</summary>
    private static IEnumerable<JsonObject> FindAll(JsonNode? node, string key)
    {
        var stack = new Stack<JsonNode?>();
        stack.Push(node);
        var found = new List<JsonObject>();
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            switch (n)
            {
                case JsonObject o:
                    foreach (var (k, v) in o)
                    {
                        if (k == key && v is JsonObject hit) found.Add(hit);
                        else stack.Push(v);
                    }
                    break;
                case JsonArray a:
                    for (int i = a.Count - 1; i >= 0; i--) stack.Push(a[i]);
                    break;
            }
        }
        return found;
    }

    private static string? FindFirstString(JsonNode? node, string key)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var (k, v) in o)
                {
                    if (k == key && v is JsonValue val && val.TryGetValue<string>(out var s)) return s;
                    var r = FindFirstString(v, key);
                    if (r != null) return r;
                }
                break;
            case JsonArray a:
                foreach (var item in a)
                {
                    var r = FindFirstString(item, key);
                    if (r != null) return r;
                }
                break;
        }
        return null;
    }

    /// <summary>Texto de un nodo de YouTube: {"simpleText": "..."} o {"runs": [{"text": "..."}]}.</summary>
    private static string? Text(JsonNode? node)
    {
        if (node is not JsonObject) return null;
        var simple = node["simpleText"]?.ToString();
        if (simple != null) return simple;
        if (node["runs"] is JsonArray runs)
            return string.Concat(runs.Select(r => r?["text"]?.ToString() ?? ""));
        return null;
    }
}
