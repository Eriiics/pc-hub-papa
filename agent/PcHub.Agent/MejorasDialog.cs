using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows.Forms;

namespace PcHub.Agent;

/// <summary>
/// Ventana «Mejorar PC Hub» en el PC: anota lo que quieres cambiar y tu Claude lo hace (una a una o todas).
/// Es la misma lista que la pantalla Mejoras del teléfono.
/// </summary>
public sealed class MejorasDialog : Form
{
    private static MejorasDialog? _open;

    private readonly Hub _hub;
    private readonly TextBox _input;
    private readonly ListView _list;
    private readonly Label _state;
    private readonly TextBox _log;
    private readonly Button _doOne, _doAll, _cancel, _install, _remove, _undo, _answer, _sync;
    private readonly System.Windows.Forms.Timer _timer;
    private string _lastLog = "";

    public static void ShowSingle(Hub hub)
    {
        if (_open is { IsDisposed: false }) { _open.Activate(); return; }
        _open = new MejorasDialog(hub);
        _open.Show();
    }

    private static string Etiqueta(string status) => status switch
    {
        "nueva" => "Por hacer",
        "en_curso" => "Claude la está haciendo…",
        "con_preguntas" => "Claude pregunta",
        "lista" => "Hecha, falta instalar",
        "hecha" => "Instalada",
        "con_problema" => "Con problema",
        "descartada" => "Descartada",
        _ => status,
    };

    private MejorasDialog(Hub hub)
    {
        _hub = hub;
        Text = "PC Hub — Mejorar con mi Claude";
        Width = 820;
        Height = 700;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);
        MinimumSize = new Size(680, 560);

        var intro = new Label
        {
            Left = 14, Top = 10, Width = 780, Height = 40, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Text = "Escribe lo que quieres cambiar en PC Hub y agrégalo a la lista. Después elige «Hacer esta» o «Hacer todas»: tu Claude lo programa, arma la versión nueva y la instala.",
        };
        _input = new TextBox
        {
            Left = 14, Top = 54, Width = 690, Height = 56, Multiline = true, ScrollBars = ScrollBars.Vertical,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, PlaceholderText = "Por ejemplo: en Excel, un botón para ordenar la columna de menor a mayor",
        };
        var add = new Button { Left = 712, Top = 54, Width = 90, Height = 56, Text = "Agregar", Anchor = AnchorStyles.Top | AnchorStyles.Right };
        add.Click += (_, _) =>
        {
            try { _hub.Mejoras.Add(_input.Text); _input.Clear(); Refresh2(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "PC Hub"); }
        };

        _list = new ListView
        {
            Left = 14, Top = 120, Width = 788, Height = 250, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        _list.Columns.Add("Estado", 170);
        _list.Columns.Add("Mejora", 330);
        _list.Columns.Add("Lo que dijo Claude", 270);
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();

        _doOne = new Button { Left = 14, Top = 378, Width = 120, Height = 32, Text = "Hacer esta" };
        _doAll = new Button { Left = 140, Top = 378, Width = 120, Height = 32, Text = "Hacer todas" };
        _cancel = new Button { Left = 266, Top = 378, Width = 100, Height = 32, Text = "Cancelar" };
        _answer = new Button { Left = 372, Top = 378, Width = 110, Height = 32, Text = "Responder…" };
        _undo = new Button { Left = 488, Top = 378, Width = 100, Height = 32, Text = "Deshacer" };
        _remove = new Button { Left = 594, Top = 378, Width = 100, Height = 32, Text = "Quitar" };
        _install = new Button { Left = 14, Top = 416, Width = 300, Height = 36, Text = "Instalar la versión nueva", BackColor = Color.FromArgb(220, 240, 255) };
        _sync = new Button { Left = 322, Top = 416, Width = 380, Height = 36, Text = "Traer las actualizaciones de Eric y juntarlas con mis cambios" };
        _sync.Click += (_, _) => Run(() => _hub.Mejoras.Sync());

        _doOne.Click += (_, _) => Run(() => _hub.Mejoras.Start(new[] { Selected()!.Id }));
        _doAll.Click += (_, _) => Run(() => _hub.Mejoras.Start(null));
        _cancel.Click += (_, _) => _hub.Mejoras.Cancel();
        _remove.Click += (_, _) => Run(() => { _hub.Mejoras.Delete(Selected()!.Id); return null; });
        _undo.Click += (_, _) =>
        {
            if (MessageBox.Show("Se quita esa mejora y se arma una versión nueva sin ella. ¿Seguro?", "PC Hub", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                Run(() => _hub.Mejoras.Undo(Selected()!.Id));
        };
        _answer.Click += (_, _) =>
        {
            var sel = Selected();
            if (sel == null) return;
            var texto = Preguntar("Claude preguntó:\n\n" + sel.Note + "\n\nEscribe tu respuesta:");
            if (!string.IsNullOrWhiteSpace(texto)) Run(() => _hub.Mejoras.Answer(sel.Id, texto));
        };
        _install.Click += (_, _) =>
        {
            if (MessageBox.Show("PC Hub se va a reiniciar con la versión nueva (unos segundos). Después, en el teléfono, toca «Actualizar la app ahora». ¿Instalar?",
                    "PC Hub", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                Run(() => _hub.Mejoras.Install());
        };

        _state = new Label { Left = 14, Top = 460, Width = 788, Height = 24, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Font = new Font(Font, FontStyle.Bold) };
        _log = new TextBox
        {
            Left = 14, Top = 488, Width = 788, Height = 160, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom, BackColor = Color.White,
        };

        Controls.AddRange(new Control[] { intro, _input, add, _list, _doOne, _doAll, _cancel, _answer, _undo, _remove, _install, _sync, _state, _log });
        _timer = new System.Windows.Forms.Timer { Interval = 1200 };
        _timer.Tick += (_, _) => Refresh2();
        _timer.Start();
        FormClosed += (_, _) => { _timer.Stop(); _timer.Dispose(); _open = null; };
        Refresh2();
    }

    private sealed record Fila(string Id, string Status, string Text, string Note);

    private Fila? Selected() => _list.SelectedItems.Count == 0 ? null : (Fila?)_list.SelectedItems[0].Tag;

    private void Run(Func<JsonNode?> action)
    {
        try { action(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "PC Hub", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        Refresh2();
    }

    private void Refresh2()
    {
        if (IsDisposed) return;
        var selId = Selected()?.Id;
        var data = _hub.Mejoras.ListJson()["mejoras"]!.AsArray();
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var n in data)
        {
            var f = new Fila(n!["id"]!.ToString(), n["status"]!.ToString(), n["text"]!.ToString(), n["note"]?.ToString() ?? "");
            var it = new ListViewItem(new[] { Etiqueta(f.Status), f.Text.Replace("\n", " "), f.Note.Replace("\n", " ") }) { Tag = f };
            if (f.Status == "hecha" || f.Status == "descartada") it.ForeColor = Color.Gray;
            if (f.Status == "con_problema") it.ForeColor = Color.Firebrick;
            if (f.Status == "con_preguntas") it.ForeColor = Color.DarkOrange;
            _list.Items.Add(it);
            if (f.Id == selId) it.Selected = true;
        }
        _list.EndUpdate();

        var st = _hub.Mejoras.StatusJson();
        var state = st["state"]!.ToString();
        _state.Text = state switch
        {
            "trabajando" => "⏳ " + st["step"],
            "compilando" => "🔧 " + st["step"],
            "listo" => $"✅ La versión {st["version"]} está lista para instalar.",
            "error" => "⚠ " + st["error"],
            _ => "Listo para recibir mejoras.",
        };
        _state.ForeColor = state == "error" ? Color.Firebrick : state == "listo" ? Color.DarkGreen : Color.Black;
        var lines = string.Join("\r\n", st["log"]!.AsArray().Select(l => l!.ToString()));
        if (lines != _lastLog)
        {
            _lastLog = lines;
            _log.Text = lines;
            _log.SelectionStart = _log.TextLength;
            _log.ScrollToCaret();
        }
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var busy = _hub.Mejoras.Busy;
        var sel = Selected();
        var anyNew = _list.Items.Cast<ListViewItem>().Any(i => ((Fila)i.Tag!).Status == "nueva");
        _doOne.Enabled = !busy && sel is { Status: "nueva" or "con_problema" };
        _doAll.Enabled = !busy && anyNew;
        _cancel.Enabled = busy;
        _answer.Enabled = !busy && sel is { Status: "con_preguntas" };
        _undo.Enabled = !busy && sel is { Status: "lista" or "hecha" };
        _remove.Enabled = !busy && sel != null && sel.Status != "en_curso";
        _sync.Enabled = !busy;
        _install.Enabled = !busy && _hub.Mejoras.StatusJson()["state"]!.ToString() == "listo";
    }

    private static string? Preguntar(string texto)
    {
        using var f = new Form { Width = 520, Height = 360, Text = "PC Hub", StartPosition = FormStartPosition.CenterParent, Font = new Font("Segoe UI", 9.5f) };
        var l = new Label { Left = 12, Top = 10, Width = 480, Height = 120, Text = texto };
        var t = new TextBox { Left = 12, Top = 136, Width = 480, Height = 120, Multiline = true, ScrollBars = ScrollBars.Vertical };
        var ok = new Button { Left = 312, Top = 270, Width = 85, Text = "Enviar", DialogResult = DialogResult.OK };
        var no = new Button { Left = 405, Top = 270, Width = 85, Text = "Cancelar", DialogResult = DialogResult.Cancel };
        f.Controls.AddRange(new Control[] { l, t, ok, no });
        f.AcceptButton = ok; f.CancelButton = no;
        return f.ShowDialog() == DialogResult.OK ? t.Text : null;
    }
}
