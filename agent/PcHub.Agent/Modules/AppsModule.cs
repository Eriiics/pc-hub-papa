using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace PcHub.Agent.Modules;

public sealed record AppEntry(string Id, string Name, string Path);

/// <summary>Aplicaciones del menú Inicio (accesos directos .lnk y .url, incluye juegos de Steam).</summary>
public static class AppsModule
{
    private static List<AppEntry> _cache = new();
    private static DateTime _cacheTime = DateTime.MinValue;
    private static readonly object Lock = new();

    public static List<AppEntry> List()
    {
        lock (Lock)
        {
            if (DateTime.UtcNow - _cacheTime < TimeSpan.FromMinutes(2) && _cache.Count > 0) return _cache;

            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            };

            var byName = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in roots.Where(Directory.Exists))
            {
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(root, "*.*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                        .Where(f => f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".url", StringComparison.OrdinalIgnoreCase));
                }
                catch { continue; }

                foreach (var f in files)
                {
                    var name = System.IO.Path.GetFileNameWithoutExtension(f);
                    var lower = name.ToLowerInvariant();
                    if (lower.Contains("uninstall") || lower.Contains("desinstal") || lower.Contains("readme") || lower.Contains("léame")) continue;
                    if (!byName.ContainsKey(name))
                        byName[name] = new AppEntry(Id(f), name, f);
                }
            }

            _cache = byName.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            _cacheTime = DateTime.UtcNow;
            return _cache;
        }
    }

    private static string Id(string path)
    {
        var hash = System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
        return Convert.ToHexString(hash, 0, 8);
    }

    public static JsonObject ListJson()
    {
        var arr = new JsonArray();
        foreach (var a in List())
            arr.Add(new JsonObject { ["id"] = a.Id, ["name"] = a.Name, ["icon"] = IconHelper.FromFile(a.Path) });
        return new JsonObject { ["apps"] = arr };
    }

    public static void Launch(string id)
    {
        var app = List().FirstOrDefault(a => a.Id == id) ?? throw new ArgumentException("Aplicación no encontrada");
        Start(app.Path);
    }

    public static void Start(string path, string? args = null)
    {
        var psi = new ProcessStartInfo(path) { UseShellExecute = true };
        if (!string.IsNullOrWhiteSpace(args)) psi.Arguments = args;
        Process.Start(psi);
    }

    /// <summary>Abre un enlace http(s) en el navegador predeterminado de Windows (en una pestaña nueva).</summary>
    public static void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("El enlace no es válido");
        Start(uri.AbsoluteUri);
    }

    /// <summary>Búsqueda tolerante (sin tildes ni mayúsculas) para la IA.</summary>
    public static (AppEntry? match, List<string> suggestions) Find(string query)
    {
        var q = Normalize(query);
        var apps = List();
        var exact = apps.FirstOrDefault(a => Normalize(a.Name) == q);
        if (exact != null) return (exact, new());
        var starts = apps.Where(a => Normalize(a.Name).StartsWith(q)).ToList();
        if (starts.Count == 1) return (starts[0], new());
        var contains = apps.Where(a => Normalize(a.Name).Contains(q)).ToList();
        if (contains.Count == 1) return (contains[0], new());
        var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var allWords = apps.Where(a => words.All(w => Normalize(a.Name).Contains(w))).ToList();
        if (allWords.Count == 1) return (allWords[0], new());

        var candidates = starts.Concat(contains).Concat(allWords).Distinct().Take(8).Select(a => a.Name).ToList();
        // Si hay varias, se lanza la de nombre más corto (suele ser la principal), pero se informan las demás
        var best = starts.Concat(contains).OrderBy(a => a.Name.Length).FirstOrDefault();
        return (best, candidates);
    }

    private static string Normalize(string s)
    {
        var d = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Trim();
    }
}
