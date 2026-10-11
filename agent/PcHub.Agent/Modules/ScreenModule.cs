using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using PcHub.Agent.Native;

namespace PcHub.Agent.Modules;

/// <summary>
/// Capturas de pantalla pedidas desde el teléfono: el teléfono muestra una vista previa de la pantalla, dibuja la zona
/// y el PC deja la imagen en el portapapeles (lista para pegar con Ctrl+V).
/// </summary>
public static class ScreenModule
{
    private const int PreviewMaxWidth = 1280;

    private static Rectangle PrimaryBounds => Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);

    /// <summary>Vista previa (JPEG en base64) de la pantalla principal, con su tamaño real en píxeles.</summary>
    public static JsonObject Preview()
    {
        var b = PrimaryBounds;
        using var full = Grab(b);
        double scale = Math.Min(1.0, (double)PreviewMaxWidth / b.Width);
        int w = Math.Max(1, (int)(b.Width * scale)), h = Math.Max(1, (int)(b.Height * scale));
        using var small = new Bitmap(w, h);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(full, 0, 0, w, h);
        }
        using var ms = new MemoryStream();
        var jpeg = Array.Find(ImageCodecInfo.GetImageEncoders(), e => e.FormatID == ImageFormat.Jpeg.Guid);
        if (jpeg != null)
        {
            using var ep = new EncoderParameters(1);
            ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 70L);
            small.Save(ms, jpeg, ep);
        }
        else small.Save(ms, ImageFormat.Jpeg);
        return new JsonObject { ["image"] = Convert.ToBase64String(ms.ToArray()), ["width"] = b.Width, ["height"] = b.Height };
    }

    /// <summary>
    /// Captura y copia al portapapeles. <paramref name="mode"/>: "full" (pantalla principal), "window" (ventana activa) o
    /// "region" (x, y, w, h como fracciones 0..1 de la pantalla principal).
    /// </summary>
    public static JsonObject Capture(string mode, double x, double y, double w, double h)
    {
        Rectangle area;
        string what;
        switch (mode)
        {
            case "full":
                area = PrimaryBounds;
                what = "pantalla completa";
                break;
            case "window":
                area = ForegroundBounds() ?? throw new InvalidOperationException("No hay una ventana activa para capturar");
                what = "ventana activa";
                break;
            case "region":
                var b = PrimaryBounds;
                x = Math.Clamp(x, 0, 1); y = Math.Clamp(y, 0, 1);
                w = Math.Clamp(w, 0, 1 - x); h = Math.Clamp(h, 0, 1 - y);
                area = new Rectangle(b.Left + (int)Math.Round(x * b.Width), b.Top + (int)Math.Round(y * b.Height),
                    (int)Math.Round(w * b.Width), (int)Math.Round(h * b.Height));
                what = "zona";
                break;
            default:
                throw new ArgumentException($"Modo de captura desconocido: {mode}");
        }
        if (area.Width < 2 || area.Height < 2) throw new ArgumentException("La zona es demasiado pequeña");

        using var bmp = Grab(area);
        SetClipboardImage(bmp);
        return new JsonObject { ["message"] = $"Captura ({what}) copiada al portapapeles del PC" };
    }

    private static Bitmap Grab(Rectangle area)
    {
        var bmp = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(area.Location, Point.Empty, area.Size, CopyPixelOperation.SourceCopy);
        return bmp;
    }

    private static Rectangle? ForegroundBounds()
    {
        var h = Win32.GetForegroundWindow();
        if (h == IntPtr.Zero) return null;
        Win32.RECT r;
        if (Win32.DwmGetWindowRect(h, Win32.DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf<Win32.RECT>()) != 0
            && !Win32.GetWindowRect(h, out r)) return null;
        var rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        // Solo lo que está dentro de los monitores (una ventana maximizada se pasa unos píxeles)
        rect.Intersect(SystemInformation.VirtualScreen);
        return rect.Width > 1 && rect.Height > 1 ? rect : null;
    }

    private static void SetClipboardImage(Bitmap bmp) => UiThread.Invoke(() =>
    {
        for (int i = 0; i < 5; i++)
        {
            try { Clipboard.SetImage(bmp); return; }
            catch { System.Threading.Thread.Sleep(50); } // portapapeles bloqueado por otra app
        }
        throw new InvalidOperationException("El portapapeles está ocupado por otra aplicación");
    });
}
