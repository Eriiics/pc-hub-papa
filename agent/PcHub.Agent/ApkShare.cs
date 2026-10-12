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

        var url = $"http://{ip}:{port}/apk";
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
            Text = "1. Conecta el teléfono al MISMO Wi-Fi que este PC.\r\n2. Abre la cámara y apunta al código.\r\n3. Toca el enlace, descarga el archivo y ábrelo para instalar.\r\n\r\nSi Android lo bloquea, mira la sección de Samsung en el LEEME.",
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
