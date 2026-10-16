using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows.Forms;

namespace PcHub.Agent;

/// <summary>Ícono en la bandeja del sistema con el PIN de emparejamiento y accesos rápidos.</summary>
public sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly Hub _hub;
    private readonly ToolStripMenuItem _pinItem;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _devicesItem;

    public TrayApp(AgentConfig config)
    {
        UiThread.Init();
        _hub = new Hub(config);

        _statusItem = new ToolStripMenuItem("Iniciando…") { Enabled = false };
        _pinItem = new ToolStripMenuItem("PIN: ------", null, (_, _) => ShowPin());
        _devicesItem = new ToolStripMenuItem("Dispositivos vinculados");

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem($"PC Hub {Installer.VersionLabel} — {config.PcName}") { Enabled = false, Font = new Font(SystemFonts.MenuFont ?? SystemFonts.DefaultFont, FontStyle.Bold) });
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_pinItem);
        menu.Items.Add(new ToolStripMenuItem("Generar nuevo PIN", null, (_, _) => { _hub.NewPin(); ShowPin(); }));
        menu.Items.Add(_devicesItem);
        menu.Items.Add(new ToolStripMenuItem("Instalar la app en el teléfono…", null, (_, _) => ApkShare.ShowDialog(_hub.Config.Port)));
        menu.Items.Add(new ToolStripSeparator());
        _installItem = new ToolStripMenuItem("Instalar PC Hub en este PC", null, (_, _) => InstallAgent());
        menu.Items.Add(_installItem);
        _startupItem = new ToolStripMenuItem("Iniciar con Windows", null, (_, _) => ToggleStartup()) { CheckOnClick = false };
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Configurar asistente IA…", null, (_, _) => OpenAiSetup()));
        menu.Items.Add(new ToolStripMenuItem("Pedir una mejora a mi Claude…", null, (_, _) => MejorasDialog.ShowSingle(_hub)));
        menu.Items.Add(new ToolStripMenuItem("Abrir Claude con el código (avanzado)…", null, (_, _) => OpenImprove()));
        menu.Items.Add(new ToolStripMenuItem("Abrir configuración (config.json)", null, (_, _) => OpenConfig()));
        menu.Items.Add(new ToolStripMenuItem("Abrir carpeta de datos y log", null, (_, _) => Shell(ConfigStore.Folder)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Reiniciar PC Hub", null, (_, _) => RestartApp()));
        menu.Items.Add(new ToolStripMenuItem("Salir", null, (_, _) => ExitApp()));
        menu.Opening += (_, _) => RefreshMenu();

        _tray = new NotifyIcon
        {
            Icon = MakeIcon(),
            Text = "PC Hub",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => ShowPin();

        _hub.PinChanged += () => UiThread.Invoke(RefreshMenu);
        _hub.SessionsChanged += () => UiThread.Invoke(RefreshMenu);
        _hub.TrayMessage += msg => UiThread.Invoke(() => _tray.ShowBalloonTip(5000, "PC Hub", msg, ToolTipIcon.Info));
        _hub.DevicePaired += name => UiThread.Invoke(() =>
            _tray.ShowBalloonTip(3000, "PC Hub", $"Se vinculó \"{name}\"", ToolTipIcon.Info));

        RefreshMenu();
        StartHub();
    }

    private async void StartHub()
    {
        try
        {
            await _hub.StartAsync();
            _statusItem.Text = $"Escuchando en {LocalIps()} : {_hub.Config.Port}";
            if (Program.StartedWithWindows) return; // arranque silencioso
            _tray.ShowBalloonTip(4000, "PC Hub activo",
                _hub.Config.Devices.Count == 0
                    ? $"Abre la app en el teléfono y usa el PIN {_hub.Pin}"
                    : "Listo para conectar desde el teléfono.",
                ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo iniciar el servidor", ex);
            _statusItem.Text = "Error al iniciar (ver log)";
            MessageBox.Show(
                $"No se pudo iniciar el servidor en el puerto {_hub.Config.Port}.\n\n{ex.Message}\n\n" +
                "¿Hay otra copia corriendo o el puerto está ocupado? Puedes cambiarlo en config.json.",
                "PC Hub", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private ToolStripMenuItem? _installItem;
    private ToolStripMenuItem? _startupItem;

    private void RefreshMenu()
    {
        if (_installItem != null)
        {
            // Si ya corre la copia instalada no hace falta; desde "dotnet run" ofrece instalar (o reinstalar)
            _installItem.Visible = !Installer.RunningFromInstall && !Program.PruebaMode;
            _installItem.Text = Installer.IsInstalled ? "Instalar esta versión en el PC (reemplaza la instalada)" : "Instalar PC Hub en este PC";
        }
        if (_startupItem != null)
        {
            _startupItem.Checked = Installer.StartupEnabled;
            _startupItem.Enabled = Installer.IsInstalled && !Program.PruebaMode;
            _startupItem.ToolTipText = Installer.IsInstalled ? "" : "Primero instala PC Hub";
        }
        _pinItem.Text = $"PIN de emparejamiento: {_hub.Pin}";
        _tray.Text = $"PC Hub{(Program.PruebaMode ? " (PRUEBA)" : "")} — {_hub.ConnectedCount} conectado(s)";

        _devicesItem.DropDownItems.Clear();
        var devices = _hub.Config.Devices.ToList();
        if (devices.Count == 0)
        {
            _devicesItem.DropDownItems.Add(new ToolStripMenuItem("(ninguno)") { Enabled = false });
        }
        else
        {
            foreach (var d in devices)
                _devicesItem.DropDownItems.Add(new ToolStripMenuItem($"{d.Name} — {d.PairedAt:dd/MM/yyyy}") { Enabled = false });
            _devicesItem.DropDownItems.Add(new ToolStripSeparator());
            _devicesItem.DropDownItems.Add(new ToolStripMenuItem("Olvidar todos", null, (_, _) =>
            {
                if (MessageBox.Show("¿Desvincular todos los dispositivos? Tendrán que emparejarse de nuevo.", "PC Hub",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                lock (_hub.Config) _hub.Config.Devices.Clear();
                ConfigStore.Save(_hub.Config);
                RefreshMenu();
            }));
        }
    }

    private void InstallAgent()
    {
        if (MessageBox.Show("Se instalará PC Hub en este PC y quedará en el menú Inicio.\n\n¿Continuar?",
                "PC Hub", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        Installer.InstallFromCurrent();
    }

    private void ToggleStartup()
    {
        try
        {
            Installer.SetStartup(!Installer.StartupEnabled);
            _tray.ShowBalloonTip(2500, "PC Hub", Installer.StartupEnabled ? "Se iniciará con Windows." : "Ya no se iniciará con Windows.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "PC Hub", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void OpenAiSetup()
    {
        using var dlg = new AiSetupDialog(_hub.Config);
        if (dlg.ShowDialog() == DialogResult.OK)
            _tray.ShowBalloonTip(2500, "PC Hub", "Asistente IA listo. Pruébalo desde el teléfono.", ToolTipIcon.Info);
    }

    /// <summary>Abre "Mejorar PC Hub con Claude.bat" (junto al programa): Claude Code con el código, para cambiar la app.</summary>
    private static void OpenImprove()
    {
        var bat = System.IO.Path.Combine(AppContext.BaseDirectory, "Mejorar PC Hub con Claude.bat");
        if (!System.IO.File.Exists(bat))
        {
            MessageBox.Show("No encuentro «Mejorar PC Hub con Claude.bat» junto al programa.\n\nVuelve a abrir PcHub.Agent.exe desde la carpeta del paquete y acepta actualizar.",
                "PC Hub", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(bat) { UseShellExecute = true, WorkingDirectory = AppContext.BaseDirectory });
    }

    private void ShowPin() =>
        MessageBox.Show($"PIN de emparejamiento:\n\n        {_hub.Pin}\n\nIP del PC: {LocalIps()}\nPuerto: {_hub.Config.Port}",
            "PC Hub", MessageBoxButtons.OK, MessageBoxIcon.Information);

    private void OpenConfig()
    {
        MessageBox.Show("Después de editar config.json, reinicia PC Hub (Salir y abrir de nuevo) para aplicar los cambios.",
            "PC Hub", MessageBoxButtons.OK, MessageBoxIcon.Information);
        Shell("notepad.exe", ConfigStore.FilePath);
    }

    private static void Shell(string file, string? args = null)
    {
        try { Process.Start(new ProcessStartInfo(file) { UseShellExecute = true, Arguments = args ?? "" }); }
        catch (Exception ex) { Log.Error("No se pudo abrir " + file, ex); }
    }

    private static Icon MakeIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe != null && exe.EndsWith("PcHub.Agent.exe", StringComparison.OrdinalIgnoreCase))
            {
                var fromExe = Icon.ExtractAssociatedIcon(exe);
                if (fromExe != null) return fromExe;
            }
        }
        catch { /* se dibuja uno */ }
        try
        {
            using var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using var bg = new SolidBrush(Color.FromArgb(124, 92, 255));
                g.FillEllipse(bg, 1, 1, 30, 30);
                using var font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
                using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString("PC", font, Brushes.White, new RectangleF(0, 1, 32, 32), fmt);
            }
            return Icon.FromHandle(bmp.GetHicon());
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    private static string LocalIps()
    {
        try
        {
            var ips = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address)
                            && !a.Address.ToString().StartsWith("169.254"))
                .Select(a => a.Address.ToString())
                .Distinct()
                .ToList();
            return ips.Count > 0 ? string.Join(", ", ips) : "?";
        }
        catch { return "?"; }
    }

    /// <summary>Cierra PC Hub y lo vuelve a abrir (un PowerShell aparte espera a que este proceso termine).</summary>
    private void RestartApp()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;
        try
        {
            var script = $"""
                $ErrorActionPreference = 'SilentlyContinue'
                Wait-Process -Id {Environment.ProcessId} -Timeout 30
                Start-Sleep -Milliseconds 400
                Start-Process -FilePath '{exe.Replace("'", "''")}'
                """;
            Installer.StartHiddenPowerShell(script);
            Log.Info("Reiniciando PC Hub");
            ExitApp();
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo reiniciar PC Hub", ex);
            MessageBox.Show("No se pudo reiniciar PC Hub: " + ex.Message, "PC Hub", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// Cierra todo: servidor, terminales, Claude Code que esté trabajando y el proceso. Si algo tarda en soltarse,
    /// no se espera más de unos segundos (antes el ícono desaparecía pero el proceso quedaba vivo).
    /// </summary>
    private async void ExitApp()
    {
        _tray.Visible = false;
        try { await System.Threading.Tasks.Task.WhenAny(_hub.DisposeAsync().AsTask(), System.Threading.Tasks.Task.Delay(4000)); } catch { }
        ExitThread();
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            await System.Threading.Tasks.Task.Delay(3000);
            Environment.Exit(0);
        });
    }
}
