using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows.Forms;
using QRCoder;

namespace PcHub.Agent;

/// <summary>
/// Entrega la app del teléfono (la APK) desde este PC por la red local, para instalarla escaneando un QR.
/// En una sesión de prueba (<c>--prueba</c>) entrega "PcHub-Prueba.apk", la app aparte que no pisa la normal.
/// </summary>
public static class ApkShare
{
    public static string FileName => Program.PruebaMode ? "PcHub-Prueba.apk" : "PcHub.apk";

    /// <summary>Esquema del enlace que abre la app del teléfono ya emparejada (distinto en la app de prueba).</summary>
    public static string Scheme => Program.PruebaMode ? "pchubprueba" : "pchub";

    private static string? _setupKey = Program.PruebaMode ? Environment.GetEnvironmentVariable("PCHUB_CLAVE_INSTALAR") : null;
    private static DateTime _setupUntil = DateTime.UtcNow.AddMinutes(20);

    /// <summary>Clave de un solo uso (vive 20 minutos) que va dentro del QR y abre la página de instalación con el PIN.</summary>
    public static string NewSetupKey()
    {
        _setupKey = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        // Solo en la sesión de prueba: permite abrir la página sin el QR para comprobarla desde una terminal
        if (Program.PruebaMode && Environment.GetEnvironmentVariable("PCHUB_CLAVE_INSTALAR") is { Length: > 0 } fija) _setupKey = fija;
        _setupUntil = DateTime.UtcNow.AddMinutes(20);
        return _setupKey;
    }

    public static bool SetupKeyValid(string? key) =>
        key != null && _setupKey != null && DateTime.UtcNow < _setupUntil &&
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(key), System.Text.Encoding.UTF8.GetBytes(_setupKey));

    /// <summary>
    /// Página que ve el teléfono al escanear el QR: 1) descargar e instalar la app, 2) tocar «Conectar», que abre la app
    /// ya emparejada con este PC (sin escribir IP ni PIN).
    /// </summary>
    public static string SetupPage(string host, int port, string pin)
    {
        var enlace = $"{Scheme}://conectar?host={Uri.EscapeDataString(host)}&port={port}&pin={Uri.EscapeDataString(pin)}";
        var titulo = Program.PruebaMode ? "PC Hub (prueba)" : "PC Hub";
        return $@"<!doctype html><html lang=""es""><head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width,initial-scale=1"">
<title>{titulo}</title>
<style>body{{font-family:system-ui,sans-serif;background:#000;color:#fff;margin:0;padding:24px;max-width:480px;margin:auto}}
h1{{font-size:26px}}p{{color:#bbb;line-height:1.4}}.paso{{background:#1c1c1e;border-radius:28px;padding:20px;margin:16px 0}}
a.b{{display:block;text-align:center;background:#3e91ff;color:#fff;text-decoration:none;border-radius:999px;padding:16px;font-size:18px;margin-top:12px}}
a.g{{background:#34c759}}small{{color:#888}}</style></head><body>
<h1>{titulo}</h1>
<div class=""paso""><b>Paso 1</b><p>Descarga la app y ábrela para instalarla. Si Android la bloquea, mira la sección Samsung del LEEME.</p>
<a class=""b"" href=""/apk"">Descargar la app</a></div>
<div class=""paso""><b>Paso 2</b><p>Cuando esté instalada, vuelve a esta página y toca el botón: la app se abre y queda conectada a este PC.</p>
<a class=""b g"" href=""{enlace}"">Conectar con este PC</a></div>
<p><small>Esta página solo funciona unos minutos mientras la ventana de instalación está abierta en el PC.</small></p>
</body></html>";
    }

    /// <summary>Busca la APK junto al programa, en la carpeta de arriba (paquete) o más arriba (código fuente).</summary>
    public static string? Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, FileName);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    /// <summary>La dirección de este PC en la red de la casa (la que usaría el teléfono).</summary>
    public static string? LanAddress()
    {
        try
        {
            var ips = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address)
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a) && !a.ToString().StartsWith("169.254"))
                .Select(a => a.ToString())
                .ToList();
            return ips.FirstOrDefault(IsPrivate) ?? ips.FirstOrDefault();
        }
        catch { return null; }
    }

    private static bool IsPrivate(string ip) =>
        ip.StartsWith("192.168.") || ip.StartsWith("10.") ||
        (ip.StartsWith("172.") && int.TryParse(ip.Split('.')[1], out var b) && b is >= 16 and <= 31);

    /// <summary>Ventana con el QR y el enlace de descarga.</summary>
    public static void ShowDialog(int port)
    {
        var apk = Find();
        var ip = LanAddress();
        if (apk == null || ip == null)
        {
            MessageBox.Show(apk == null
                    ? $"No encuentro {FileName} junto al programa.\n\nSi lo instalaste desde el paquete, vuelve a abrir PcHub.Agent.exe de la carpeta PcHub-Paquete y acepta actualizar."
                    : "No encuentro la red de este PC. Conéctalo al Wi-Fi e inténtalo de nuevo.",
                "PC Hub", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var url = $"http://{ip}:{port}/instalar?k={NewSetupKey()}";
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(12);
        using var ms = new MemoryStream(png);
        using var qr = new Bitmap(ms);

        using var form = new Form
        {
            Text = Program.PruebaMode ? "PC Hub (PRUEBA): instalar en el teléfono" : "PC Hub: instalar en el teléfono",
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ClientSize = new Size(440, 600),
            Font = new Font("Segoe UI", 10f),
        };
        form.Controls.Add(new Label
        {
            Left = 20, Top = 16, Width = 400, Height = 108,
            Text = "1. Conecta el teléfono al MISMO Wi-Fi que este PC.\r\n2. Abre la cámara y apunta al código.\r\n3. En la página que se abre: «Descargar la app», ábrela para instalar y después toca «Conectar con este PC».\r\n\r\nSi Android lo bloquea, mira la sección de Samsung en el LEEME.",
        });
        form.Controls.Add(new PictureBox
        {
            Left = 40, Top = 130, Width = 360, Height = 360,
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = (Bitmap)qr.Clone(),
        });
        form.Controls.Add(new TextBox { Left = 20, Top = 504, Width = 400, ReadOnly = true, Text = url });
        var copy = new Button { Left = 20, Top = 540, Width = 190, Height = 36, Text = "Copiar enlace" };
        copy.Click += (_, _) => { try { Clipboard.SetText(url); } catch { } };
        var close = new Button { Left = 230, Top = 540, Width = 190, Height = 36, Text = "Cerrar", DialogResult = DialogResult.OK };
        form.Controls.Add(copy);
        form.Controls.Add(close);
        form.AcceptButton = close;
        form.ShowDialog();
    }
}
