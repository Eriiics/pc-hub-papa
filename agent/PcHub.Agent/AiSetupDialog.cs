using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PcHub.Agent.Modules;

namespace PcHub.Agent;

/// <summary>
/// Bandeja → "Configurar asistente IA…". Dos opciones:
///  - Suscripción (Claude Code): usa tu plan de claude.ai, sin API key ni costo extra.
///  - API key: se paga por uso; la key se guarda cifrada.
/// </summary>
public sealed class AiSetupDialog : Form
{
    private sealed record ModelItem(string Id, string Label)
    {
        public override string ToString() => Label;
    }

    private static readonly ModelItem[] CodeModels =
    {
        new("haiku", "Haiku — rápido y gasta poco de tu límite (recomendado)"),
        new("sonnet", "Sonnet — más capaz para tareas largas"),
        new("opus", "Opus — el más capaz, gasta más límite"),
    };

    private static readonly ModelItem[] ApiModels =
    {
        new("claude-haiku-5-5", "Claude Haiku 5.5 — rápido y barato (recomendado)"),
        new("claude-sonnet-5-5", "Claude Sonnet 5.5 — más capaz para tareas largas"),
        new("claude-opus-5-5", "Claude Opus 5.5 — el más capaz, más caro"),
    };

    private readonly AgentConfig _config;
    private readonly RadioButton _useCode;
    private readonly RadioButton _useApi;
    private readonly Label _codeStatus;
    private readonly ComboBox _codeModel;
    private readonly TextBox _key;
    private readonly ComboBox _apiModel;
    private readonly Label _result;
    private readonly Button _test;
    private readonly Control[] _codeControls;
    private readonly Control[] _apiControls;

    public AiSetupDialog(AgentConfig config)
    {
        _config = config;

        Text = "PC Hub — Asistente IA";
        Width = 600;
        Height = 470;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);

        int y = 12;

        // ---------- opción 1: suscripción ----------
        _useCode = new RadioButton { Left = 14, Top = y, Width = 560, Text = "Usar mi suscripción de Claude (con Claude Code) — sin costo extra", Font = new Font(Font, FontStyle.Bold) };
        y += 28;
        var codeInfo = new Label
        {
            Left = 34, Top = y, Width = 540, Height = 36,
            Text = "Usa el límite de uso de tu plan de claude.ai. Necesita Claude Code instalado y con sesión iniciada en este PC.",
        };
        y += 38;
        _codeStatus = new Label { Left = 34, Top = y, Width = 540, Height = 36, Text = "Revisando Claude Code…" };
        y += 38;
        var codeModelLabel = new Label { Left = 34, Top = y + 3, Width = 60, Text = "Modelo:", AutoSize = true };
        _codeModel = new ComboBox { Left = 100, Top = y, Width = 474, DropDownStyle = ComboBoxStyle.DropDownList };
        FillModels(_codeModel, CodeModels, config.Ai.ClaudeCodeModel);
        y += 40;

        // ---------- opción 2: API key ----------
        _useApi = new RadioButton { Left = 14, Top = y, Width = 560, Text = "Usar una API key (se paga por uso, aparte del plan)", Font = new Font(Font, FontStyle.Bold) };
        y += 28;
        var keyLabel = new Label { Left = 34, Top = y + 3, Width = 60, Text = "API key:", AutoSize = true };
        _key = new TextBox { Left = 100, Top = y, Width = 474, UseSystemPasswordChar = true };
        if (config.Ai.HasApiKey) _key.PlaceholderText = "(ya hay una guardada; déjalo vacío para mantenerla)";
        y += 32;
        var apiModelLabel = new Label { Left = 34, Top = y + 3, Width = 60, Text = "Modelo:", AutoSize = true };
        _apiModel = new ComboBox { Left = 100, Top = y, Width = 474, DropDownStyle = ComboBoxStyle.DropDownList };
        FillModels(_apiModel, ApiModels, config.Ai.Model);
        y += 40;

        // ---------- resultado y botones ----------
        _result = new Label { Left = 14, Top = y, Width = 560, Height = 48 };
        y += 54;
        _test = new Button { Text = "Probar", Left = 14, Top = y, Width = 90 };
        var save = new Button { Text = "Guardar", Left = 398, Top = y, Width = 85 };
        var cancel = new Button { Text = "Cancelar", Left = 489, Top = y, Width = 85, DialogResult = DialogResult.Cancel };

        _codeControls = new Control[] { _codeModel };
        _apiControls = new Control[] { _key, _apiModel };

        var provider = config.Ai.Provider.Trim().ToLowerInvariant();
        bool api = provider == "api" || (provider == "auto" && config.Ai.HasApiKey);
        _useApi.Checked = api;
        _useCode.Checked = !api;
        _useCode.CheckedChanged += (_, _) => UpdateEnabled();
        _useApi.CheckedChanged += (_, _) => UpdateEnabled();
        UpdateEnabled();

        _test.Click += async (_, _) => await TestAsync();
        save.Click += (_, _) => Save();

        Controls.AddRange(new Control[]
        {
            _useCode, codeInfo, _codeStatus, codeModelLabel, _codeModel,
            _useApi, keyLabel, _key, apiModelLabel, _apiModel,
            _result, _test, save, cancel,
        });
        AcceptButton = save;
        CancelButton = cancel;

        Shown += async (_, _) =>
        {
            var (ok, message) = await AiModule.ClaudeCodeStatusAsync(_config.Ai.ClaudeCodePath);
            _codeStatus.ForeColor = ok ? Color.FromArgb(20, 120, 70) : Color.Firebrick;
            _codeStatus.Text = message;
        };
    }

    private static void FillModels(ComboBox box, ModelItem[] models, string current)
    {
        foreach (var m in models) box.Items.Add(m);
        var sel = models.FirstOrDefault(m => m.Id == current);
        if (sel == null && !string.IsNullOrWhiteSpace(current))
        {
            sel = new ModelItem(current, current);
            box.Items.Add(sel);
        }
        box.SelectedItem = sel ?? models[0];
    }

    private void UpdateEnabled()
    {
        foreach (var c in _codeControls) c.Enabled = _useCode.Checked;
        foreach (var c in _apiControls) c.Enabled = _useApi.Checked;
    }

    private static string Selected(ComboBox box, ModelItem[] fallback) => (box.SelectedItem as ModelItem)?.Id ?? fallback[0].Id;

    private async Task TestAsync()
    {
        _test.Enabled = false;
        ShowResult(true, "Probando… (la primera vez puede tardar unos segundos)");
        try
        {
            if (_useCode.Checked)
            {
                var (ok, message) = await AiModule.TestClaudeCodeAsync(_config.Ai.ClaudeCodePath, Selected(_codeModel, CodeModels));
                ShowResult(ok, message);
            }
            else
            {
                var key = _key.Text.Trim().Length > 0 ? _key.Text.Trim() : _config.Ai.ResolveApiKey();
                if (key.Length == 0) { ShowResult(false, "Primero pega una API key."); return; }
                var (ok, message) = await AiModule.TestAsync(key, Selected(_apiModel, ApiModels));
                ShowResult(ok, message);
            }
        }
        finally
        {
            _test.Enabled = true;
        }
    }

    private void Save()
    {
        if (_useApi.Checked)
        {
            var typed = _key.Text.Trim();
            if (typed.Length == 0 && !_config.Ai.HasApiKey)
            {
                ShowResult(false, "Para usar la API necesitas pegar una API key.");
                return;
            }
            if (typed.Length > 0 && !typed.StartsWith("sk-ant-") &&
                MessageBox.Show("La key no empieza con \"sk-ant-\". ¿Guardarla igual?", "PC Hub",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            if (typed.Length > 0) _config.Ai.StoreApiKey(typed);
            _config.Ai.Provider = "api";
            _config.Ai.Model = Selected(_apiModel, ApiModels);
        }
        else
        {
            _config.Ai.Provider = "claude-code";
            _config.Ai.ClaudeCodeModel = Selected(_codeModel, CodeModels);
        }
        ConfigStore.Save(_config);
        Log.Info($"Asistente IA configurado: {_config.Ai.Provider}");
        DialogResult = DialogResult.OK;
        Close();
    }

    private void ShowResult(bool ok, string message)
    {
        _result.ForeColor = ok ? Color.FromArgb(20, 120, 70) : Color.Firebrick;
        _result.Text = message;
    }
}
