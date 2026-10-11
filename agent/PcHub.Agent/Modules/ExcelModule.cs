using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace PcHub.Agent.Modules;

/// <summary>
/// Controla el Excel que ya está abierto en el PC (por COM, sin instalar nada extra):
/// ver la hoja, moverse entre celdas, escribir datos, guardar, deshacer y abrir archivos recientes.
/// </summary>
public static class ExcelModule
{
    private static readonly string[] Extensiones = { ".xlsx", ".xlsm", ".xls", ".csv" };

    [DllImport("oleaut32.dll", PreserveSig = false)]
    private static extern void GetActiveObject(ref Guid rclsid, IntPtr reserved, [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);

    [DllImport("ole32.dll")]
    private static extern int CLSIDFromProgID([MarshalAs(UnmanagedType.LPWStr)] string progId, out Guid clsid);

    /// <summary>El Excel abierto, o null si no hay.</summary>
    private static dynamic? App()
    {
        try
        {
            if (CLSIDFromProgID("Excel.Application", out var clsid) != 0) return null;
            GetActiveObject(ref clsid, IntPtr.Zero, out var obj);
            return obj;
        }
        catch (COMException) { return null; }
    }

    private static bool Instalado()
    {
        try { return CLSIDFromProgID("Excel.Application", out _) == 0; }
        catch { return false; }
    }

    private static dynamic Requerir()
    {
        var app = App();
        if (app == null) throw new InvalidOperationException("Excel no está abierto. Ábrelo desde la app o elige un archivo.");
        return app;
    }

    /// <summary>Convierte el error "Excel está ocupado" (editando una celda, diálogo abierto) en un mensaje claro.</summary>
    private static T Seguro<T>(Func<T> fn)
    {
        try { return fn(); }
        catch (COMException ex) when ((uint)ex.HResult is 0x80010001 or 0x800AC472 or 0x8001010A)
        {
            throw new InvalidOperationException("Excel está ocupado. Termina de editar la celda o cierra el cuadro que tenga abierto.");
        }
    }

    // ------------------------------------------------------------------ estado

    public static JsonNode Status() => Seguro<JsonNode>(() =>
    {
        var res = new JsonObject { ["installed"] = Instalado() };
        var app = App();
        if (app == null) { res["running"] = false; return res; }
        res["running"] = true;
        try
        {
            if ((int)app.Workbooks.Count == 0) { res["workbook"] = null; return res; }
            var wb = app.ActiveWorkbook;
            var sheet = app.ActiveSheet;
            var sheets = new JsonArray();
            foreach (var s in wb.Worksheets) sheets.Add((string)s.Name);
            res["workbook"] = (string)wb.Name;
            res["saved"] = (bool)wb.Saved;
            res["sheet"] = (string)sheet.Name;
            res["sheets"] = sheets;
            var cell = app.ActiveCell;
            res["cell"] = (string)cell.Address(false, false);
            res["value"] = Texto(cell);
            res["formula"] = Convert.ToString(cell.Formula) ?? "";
            res["selection"] = (string)app.Selection.Address(false, false);
        }
        catch (COMException)
        {
            // Excel abierto pero sin libro activo (por ejemplo mostrando la pantalla de inicio)
            res["workbook"] = null;
        }
        return res;
    });

    private static string Texto(dynamic cell)
    {
        try { return Convert.ToString(cell.Text) ?? ""; }
        catch { return ""; }
    }

    /// <summary>Cuadrícula alrededor de la celda activa (filas × columnas), con lo que se ve en pantalla.</summary>
    public static JsonNode Grid(int rows, int cols)
    {
        rows = Math.Clamp(rows, 1, 40);
        cols = Math.Clamp(cols, 1, 12);
        return Seguro<JsonNode>(() =>
        {
            var app = Requerir();
            var sheet = app.ActiveSheet;
            var active = app.ActiveCell;
            int row = (int)active.Row, col = (int)active.Column;
            // La ventana arranca donde el usuario ya la tiene; si la celda queda fuera, se centra en ella
            int firstRow = Math.Max(1, row - rows / 2), firstCol = Math.Max(1, col - cols / 2);
            try
            {
                var vis = app.ActiveWindow.VisibleRange;
                int vr = (int)vis.Row, vc = (int)vis.Column;
                if (row >= vr && row < vr + rows) firstRow = vr;
                if (col >= vc && col < vc + cols) firstCol = vc;
            }
            catch (COMException) { }

            var data = new JsonArray();
            for (int r = 0; r < rows; r++)
            {
                var line = new JsonArray();
                for (int c = 0; c < cols; c++) line.Add(Texto(sheet.Cells[firstRow + r, firstCol + c]));
                data.Add(line);
            }
            var colNames = new JsonArray();
            for (int c = 0; c < cols; c++) colNames.Add(NombreColumna(firstCol + c));
            return new JsonObject
            {
                ["firstRow"] = firstRow,
                ["firstCol"] = firstCol,
                ["cols"] = colNames,
                ["rows"] = data,
                ["activeRow"] = row,
                ["activeCol"] = col,
            };
        });
    }

    private static string NombreColumna(int n)
    {
        var s = "";
        while (n > 0) { n--; s = (char)('A' + n % 26) + s; }
        return s;
    }

    // ------------------------------------------------------------------ editar

    /// <summary>Ir a una celda ("B7") o moverse relativo a la activa (dRow/dCol).</summary>
    public static JsonNode Select(string? cell, int dRow, int dCol) => Seguro<JsonNode>(() =>
    {
        var app = Requerir();
        var sheet = app.ActiveSheet;
        if (!string.IsNullOrWhiteSpace(cell))
        {
            sheet.Range[cell.Trim().ToUpperInvariant()].Select();
        }
        else
        {
            var a = app.ActiveCell;
            int r = Math.Clamp((int)a.Row + dRow, 1, 1048576), c = Math.Clamp((int)a.Column + dCol, 1, 16384);
            sheet.Cells[r, c].Select();
        }
        return Status();
    });

    /// <summary>Escribe en la celda activa y baja (o avanza) a la siguiente, como al teclear en Excel.</summary>
    public static JsonNode Write(string value, string next) => Seguro<JsonNode>(() =>
    {
        var app = Requerir();
        var a = app.ActiveCell;
        // Los números y fórmulas ("=SUMA(A1:A5)") se interpretan como en Excel; lo demás queda como texto
        a.Value2 = value.StartsWith('=') ? null : value;
        if (value.StartsWith('=')) a.Formula = value;
        int r = (int)a.Row, c = (int)a.Column;
        var sheet = app.ActiveSheet;
        switch (next)
        {
            case "down": sheet.Cells[r + 1, c].Select(); break;
            case "right": sheet.Cells[r, c + 1].Select(); break;
        }
        return Status();
    });

    public static JsonNode Action(string action) => Seguro<JsonNode>(() =>
    {
        var app = Requerir();
        switch (action)
        {
            case "save": app.ActiveWorkbook.Save(); break;
            case "undo": app.Undo(); break;
            case "clear": app.Selection.ClearContents(); break;
            case "bold": { object cur = app.Selection.Font.Bold; app.Selection.Font.Bold = !(cur is bool b && b); break; }
            case "autosum": app.Selection.Formula = "=SUM(" + SumaArriba(app) + ")"; break;
            case "filter": app.ActiveSheet.Range[app.ActiveCell.Address].AutoFilter(); break;
            case "next_sheet": SiguienteHoja(app, +1); break;
            case "prev_sheet": SiguienteHoja(app, -1); break;
            case "new_sheet": app.Worksheets.Add(After: app.ActiveSheet); break;
            case "copy": app.Selection.Copy(); break;
            case "cut": app.Selection.Cut(); break;
            case "paste": app.ActiveSheet.Paste(); app.CutCopyMode = false; break;
            case "home": app.ActiveSheet.Range["A1"].Select(); break;
            case "jump_up": Saltar(app, -4162); break;    // xlUp
            case "jump_down": Saltar(app, -4121); break;  // xlDown
            case "jump_left": Saltar(app, -4159); break;  // xlToLeft
            case "jump_right": Saltar(app, -4161); break; // xlToRight
            case "insert_row": app.Selection.EntireRow.Insert(); break;
            case "delete_row": app.Selection.EntireRow.Delete(); break;
            case "autofit": app.Selection.EntireColumn.AutoFit(); break;
            case "percent": app.Selection.NumberFormat = "0%"; break;
            case "money": app.Selection.NumberFormat = "$#,##0"; break;
            case "number": app.Selection.NumberFormat = "General"; break;
            default: throw new ArgumentException("Acción de Excel desconocida: " + action);
        }
        return Status();
    });

    /// <summary>Salta al borde del bloque de datos, como Ctrl + flecha.</summary>
    private static void Saltar(dynamic app, int direccion)
    {
        app.ActiveCell.End(direccion).Select();
    }

    /// <summary>Rango de números justo encima de la celda activa (para la suma automática).</summary>
    private static string SumaArriba(dynamic app)
    {
        var a = app.ActiveCell;
        int r = (int)a.Row, c = (int)a.Column;
        var sheet = app.ActiveSheet;
        int top = r - 1;
        while (top >= 1 && sheet.Cells[top, c].Value2 is double) top--;
        top++;
        if (top >= r) throw new InvalidOperationException("No hay números arriba de esta celda para sumar.");
        return (string)sheet.Range[sheet.Cells[top, c], sheet.Cells[r - 1, c]].Address(false, false);
    }

    private static void SiguienteHoja(dynamic app, int delta)
    {
        var wb = app.ActiveWorkbook;
        int n = (int)wb.Worksheets.Count;
        int idx = (int)app.ActiveSheet.Index + delta;
        idx = ((idx - 1) % n + n) % n + 1;
        wb.Worksheets[idx].Activate();
    }

    public static JsonNode SelectSheet(string name) => Seguro<JsonNode>(() =>
    {
        Requerir().ActiveWorkbook.Worksheets[name].Activate();
        return Status();
    });

    // ------------------------------------------------------------------ archivos

    /// <summary>Hojas de cálculo recientes: la lista "Recientes" de Windows más Documentos y Escritorio.</summary>
    public static JsonNode Files()
    {
        var found = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        void Add(string path)
        {
            try
            {
                if (Path.GetFileName(path).StartsWith("~$")) return; // archivo temporal de Excel
                if (!Extensiones.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) return;
                var fi = new FileInfo(path);
                if (fi.Exists) found[path] = fi.LastWriteTime;
            }
            catch { /* ruta inaccesible */ }
        }

        // Accesos directos de "Recientes" → archivo real
        try
        {
            var recent = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType != null && Directory.Exists(recent))
            {
                dynamic shell = Activator.CreateInstance(shellType)!;
                try
                {
                    foreach (var lnk in new DirectoryInfo(recent).GetFiles("*.lnk").OrderByDescending(f => f.LastWriteTime).Take(300))
                    {
                        string target;
                        try { target = (string)shell.CreateShortcut(lnk.FullName).TargetPath; }
                        catch { continue; }
                        if (target.Length > 0) Add(target);
                    }
                }
                finally { Marshal.FinalReleaseComObject(shell); }
            }
        }
        catch (Exception ex) { Log.Error("Excel: archivos recientes", ex); }

        foreach (var folder in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                     Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                 })
        {
            try
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var f in Directory.EnumerateFiles(folder, "*.*", SearchOption.TopDirectoryOnly)) Add(f);
            }
            catch { /* sin permiso */ }
        }

        var list = new JsonArray();
        foreach (var kv in found.OrderByDescending(k => k.Value).Take(40))
            list.Add(new JsonObject
            {
                ["path"] = kv.Key,
                ["name"] = Path.GetFileName(kv.Key),
                ["folder"] = Path.GetFileName(Path.GetDirectoryName(kv.Key) ?? ""),
                ["modified"] = kv.Value.ToString("dd/MM/yyyy HH:mm"),
            });
        return new JsonObject { ["files"] = list };
    }

    public static JsonNode Open(string path)
    {
        if (!File.Exists(path) || !Extensiones.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Ese archivo no existe o no es una hoja de cálculo.");
        AppsModule.Start(path); // Windows lo abre con Excel (o el programa que tenga asociado)
        return new JsonObject { ["opening"] = true };
    }

    /// <summary>Abre Excel con un libro en blanco (o crea uno nuevo si Excel ya estaba abierto).</summary>
    public static JsonNode New()
    {
        if (!Instalado()) throw new InvalidOperationException("No encontré Excel instalado en este PC.");
        if (App() == null)
        {
            AppsModule.Start("excel.exe", "/e"); // /e = sin pantalla de inicio ni libro
            for (int i = 0; i < 40 && App() == null; i++) System.Threading.Thread.Sleep(500);
        }
        var app = Requerir();
        app.Visible = true;
        app.Workbooks.Add();
        return Status();
    }
}
