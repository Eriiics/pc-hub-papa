using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace PcHub.Agent.Modules;

/// <summary>Extrae el ícono de un archivo (exe, lnk, url) como PNG base64 de 32 px, con caché.</summary>
public static class IconHelper
{
    private static readonly ConcurrentDictionary<string, string?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string? FromFile(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        return Cache.GetOrAdd(path, Extract);
    }

    private static string? Extract(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon == null) return null;
            using var src = icon.ToBitmap();
            using var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, 0, 0, 32, 32);
            }
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return Convert.ToBase64String(ms.ToArray());
        }
        catch
        {
            return null;
        }
    }
}
