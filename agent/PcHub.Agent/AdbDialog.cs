using System;
using System.Drawing;
using System.Windows.Forms;

namespace PcHub.Agent;

/// <summary>
/// «Instalar en el teléfono por Wi-Fi»: se vincula el teléfono una sola vez (depuración inalámbrica) y desde entonces
/// el PC instala las versiones nuevas solo. Ver AdbTools.
/// </summary>
public sealed class AdbDialog : Form
{
    private static AdbDialog? _open;

    private readonly TextBox _host, _code;
    private readonly Label _result;
    private readonly Button _pair, _install;

    public static void ShowSingle()
    {
        if (_open is { IsDisposed: false }) { _open.Activate(); return; }
        _open = new AdbDialog();
        _open.Show();
    }

    private AdbDialog()
    {
        Text = Program.PruebaMode ? "PC Hub (PRUEBA): instalar por Wi-Fi" : "PC Hub: instalar por Wi-Fi";
        Width = 560;
        Height = 560;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        Font = new Font("Segoe UI", 9.5f);

        var intro = new Label
        {
            Left = 14, Top = 10, Width = 520, Height = 250,
            Text =
                "Con esto el PC instala y actualiza la app en tu teléfono por Wi-Fi, sin cables y sin tocar nada.\r\n\r\n" +
                "VINCULAR (una sola vez):\r\n" +
                "1. En el teléfono: Ajustes → Acerca del teléfono → Información de software → toca 7 veces «Número de compilación» (activa Opciones de desarrollador).\r\n" +
                "2. Ajustes → Opciones de desarrollador → activa «Depuración inalámbrica» (con el mismo Wi-Fi del PC).\r\n" +
                "3. Dentro de «Depuración inalámbrica» toca «Vincular dispositivo con código de vinculación».\r\n" +
                "4. Escribe aquí la dirección con puerto y el código que muestra el teléfono, y toca «Vincular».\r\n" +
                "5. Toca «Instalar en el teléfono ahora». Desde entonces la app se actualiza sola.\r\n\r\n" +
                "En un Samsung, si no deja: Ajustes → Seguridad y privacidad → Bloqueador automático → desactívalo.",
        };
        var l1 = new Label { Left = 14, Top = 270, Width = 190, Text = "Dirección con puerto (ej. 192.168.1.20:37215):", Height = 40 };
        _host = new TextBox { Left = 210, Top = 272, Width = 320 };
        var l2 = new Label { Left = 14, Top = 318, Width = 190, Text = "Código de vinculación (6 números):", Height = 40 };
        _code = new TextBox { Left = 210, Top = 320, Width = 140 };
        _pair = new Button { Left = 14, Top = 366, Width = 250, Height = 36, Text = "Vincular" };
        _install = new Button { Left = 280, Top = 366, Width = 250, Height = 36, Text = "Instalar en el teléfono ahora" };
        _result = new Label { Left = 14, Top = 414, Width = 520, Height = 100, ForeColor = Color.DimGray };

        _pair.Click += async (_, _) =>
        {
            if (_host.Text.Trim().Length == 0 || _code.Text.Trim().Length == 0) { _result.Text = "Escribe la dirección con puerto y el código."; return; }
            Busy(true, "Vinculando…");
            var (ok, msg) = await AdbTools.PairAsync(_host.Text, _code.Text);
            Busy(false, msg, ok);
        };
        _install.Click += async (_, _) =>
        {
            Busy(true, "Buscando el teléfono e instalando… (puede tardar un minuto)");
            var (ok, msg) = await AdbTools.InstallAsync(null);
            Busy(false, msg, ok);
        };
        Controls.AddRange(new Control[] { intro, l1, _host, l2, _code, _pair, _install, _result });
        FormClosed += (_, _) => _open = null;
    }

    private void Busy(bool busy, string msg, bool? ok = null)
    {
        _pair.Enabled = _install.Enabled = !busy;
        _result.ForeColor = ok == true ? Color.DarkGreen : ok == false ? Color.Firebrick : Color.DimGray;
        _result.Text = msg;
    }
}
