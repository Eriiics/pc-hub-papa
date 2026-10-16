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
    internal static dynamic? App()
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

    /// <summary>La hoja activa como hoja de cálculo; si es un gráfico o no hay libro, explica qué pasa.</summary>
    private static dynamic Hoja(dynamic app)
    {
        if (EnVistaProtegida(app))
            throw new InvalidOperationException("El archivo está en Vista protegida. Toca «Habilitar edición» aquí abajo (o en el PC) para poder usarlo.");
        if ((int)app.Workbooks.Count == 0)
            throw new InvalidOperationException("Excel no tiene ningún libro abierto. Elige un archivo o crea un libro nuevo.");
        var sheet = app.ActiveSheet;
        if (sheet == null) throw new InvalidOperationException("Excel no tiene ninguna hoja abierta. Abre un archivo o crea un libro nuevo.");
        bool esHoja;
        try { esHoja = (int)sheet.Type == -4167; } catch { esHoja = true; } // xlWorksheet
        if (!esHoja) throw new InvalidOperationException("Lo que está abierto en Excel es un gráfico, no una hoja. Cambia a una hoja con las flechas de hoja.");
        return sheet;
    }

    /// <summary>¿Hay un archivo abierto en Vista protegida (descargado de internet o de un correo) y ningún libro editable?</summary>
    private static bool EnVistaProtegida(dynamic app)
    {
        try { return (int)app.Workbooks.Count == 0 && (int)app.ProtectedViewWindows.Count > 0; }
        catch { return false; }
    }

    /// <summary>Avisa con claridad si la celda está bloqueada por una hoja protegida (en vez de un error de Excel).</summary>
    private static void ComprobarEditable(dynamic sheet, dynamic celda)
    {
        bool protegida;
        try { protegida = (bool)sheet.ProtectContents; } catch { return; }
        if (!protegida) return;
        bool bloqueada;
        try { bloqueada = (bool)celda.Locked; } catch { bloqueada = true; }
        if (bloqueada)
            throw new InvalidOperationException("Esta hoja está protegida y esa celda está bloqueada. En Excel usa Revisar → Desproteger hoja (si tiene contraseña, hay que ponerla en el PC).");
    }

    /// <summary>Convierte el error "Excel está ocupado" (editando una celda, diálogo abierto) en un mensaje claro.</summary>
    private static T Seguro<T>(Func<T> fn)
    {
        try { return fn(); }
        catch (COMException ex) when ((uint)ex.HResult is 0x80010001 or 0x800AC472 or 0x8001010A)
        {
            throw new InvalidOperationException("Excel está ocupado. Termina de editar la celda o cierra el cuadro que tenga abierto.");
        }
        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            // Hoja, celda o libro nulos: Excel está abierto pero sin nada que editar
            throw new InvalidOperationException("Excel no tiene ninguna hoja abierta. Abre un archivo o crea un libro nuevo.");
        }
        catch (COMException ex)
        {
            // Cualquier otro error de Excel: un mensaje claro en vez del código (p. ej. 0x800A03EC)
            Log.Error("Excel: " + ex.HResult.ToString("X"), ex);
            throw new InvalidOperationException("Excel no pudo hacer eso con lo que está seleccionado. Revisa la celda o el rango e inténtalo de nuevo.");
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
            if ((int)app.Workbooks.Count == 0)
            {
                res["workbook"] = null;
                if (EnVistaProtegida(app))
                {
                    res["protectedView"] = true;
                    try { res["workbook"] = (string)app.ProtectedViewWindows[1].SourceName; } catch { }
                }
                return res;
            }
            var wb = app.ActiveWorkbook;
            var sheet = app.ActiveSheet;
            var sheets = new JsonArray();
            foreach (var hoja in wb.Worksheets)
            {
                try { if ((int)hoja.Visible != -1) continue; } catch { } // oculta: no se ofrece
                sheets.Add((string)hoja.Name);
            }
            res["workbook"] = (string)wb.Name;
            res["saved"] = (bool)wb.Saved;
            res["sheet"] = (string)sheet.Name;
            res["sheets"] = sheets;
            try { res["readOnly"] = (bool)wb.ReadOnly; } catch { }
            try { res["protectedSheet"] = (bool)sheet.ProtectContents; } catch { }
            // Cada dato por separado: un gráfico seleccionado o una hoja de gráfico no deben esconder el libro entero
            try
            {
                var cell = app.ActiveCell;
                res["cell"] = (string)cell.Address(false, false);
                res["value"] = Texto(cell);
                res["formula"] = Convert.ToString(cell.Formula) ?? "";
            }
            catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
            try { res["selection"] = (string)app.Selection.Address(false, false); }
            catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
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
            var sheet = Hoja(app);
            var active = app.ActiveCell;
            if (active == null) throw new InvalidOperationException("Excel no tiene ninguna hoja abierta. Abre un archivo o crea un libro nuevo.");
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

    /// <summary>
    /// Pone una fórmula tal como la escribe la persona: con los nombres de su Excel (=SUMA en español).
    /// "Formula" solo entiende inglés y daría #¿NOMBRE?; si FormulaLocal no la acepta, se prueba en inglés (=SUM).
    /// </summary>
    private static void PonerFormula(dynamic celda, string formula)
    {
        try { celda.FormulaLocal = formula; }
        catch (COMException) { celda.Formula = formula; return; }
        // En un Excel en español, "=SUM(...)" se acepta pero queda como #¿NOMBRE?: se prueba con los nombres en inglés
        if (TieneError(celda, 2029))
        {
            try
            {
                celda.Formula = formula;
                if (TieneError(celda, 2029)) celda.FormulaLocal = formula; // tampoco: queda como la escribió
            }
            catch (COMException) { celda.FormulaLocal = formula; }
        }
    }

    /// <summary>¿La celda muestra ese error de Excel (2029 = #NOMBRE?)?</summary>
    private static bool TieneError(dynamic celda, int codigo)
    {
        try { return celda.Value2 is int e && e == unchecked((int)(0x800A0000 | (uint)codigo)); }
        catch { return false; }
    }

    // ------------------------------------------------------------------ deshacer
    // Los cambios hechos desde fuera de Excel (como los de este programa) borran su historial de deshacer, así que
    // se lleva uno propio: antes de cambiar celdas se guarda cómo estaban y "Deshacer" las vuelve a dejar así.

    private static readonly List<Action<dynamic>> Historial = new();

    private static void Registrar(Action<dynamic>? deshacer)
    {
        if (deshacer == null) return;
        Historial.Add(deshacer);
        if (Historial.Count > 30) Historial.RemoveAt(0);
    }

    /// <summary>Guarda cómo está un rango (fórmulas y, si se pide, formato). Devuelve null si es demasiado grande.</summary>
    private static Action<dynamic>? Capturar(dynamic rango, bool formatos = false)
    {
        try
        {
            if ((double)rango.CountLarge > 20000) return null;
            dynamic hoja = rango.Worksheet;
            string libro = (string)hoja.Parent.Name, nombre = (string)hoja.Name, dir = (string)rango.Address;
            object? formulas = rango.Formula;
            List<(string Fmt, bool Bold)>? fmts = null;
            if (formatos)
            {
                if ((double)rango.CountLarge > 300) return null;
                fmts = new();
                foreach (var c in rango.Cells) fmts.Add((Convert.ToString(((dynamic)c).NumberFormat) ?? "General", ((dynamic)c).Font.Bold is bool b && b));
            }
            return app =>
            {
                dynamic h = app.Workbooks[libro].Worksheets[nombre];
                app.Workbooks[libro].Activate();
                h.Activate();
                dynamic r = h.Range[dir];
                r.Formula = formulas;
                if (fmts != null)
                {
                    int i = 0;
                    foreach (var c in r.Cells) { var f = fmts[i++]; ((dynamic)c).NumberFormat = f.Fmt; ((dynamic)c).Font.Bold = f.Bold; }
                }
                r.Select();
            };
        }
        catch { return null; }
    }

    private static JsonNode Deshacer()
    {
        var app = Requerir();
        while (Historial.Count > 0)
        {
            var paso = Historial[^1];
            Historial.RemoveAt(Historial.Count - 1);
            try { paso(app); return Status(); }
            catch (COMException) { /* el libro ya no está abierto o cambió: se prueba con el anterior */ }
        }
        try { app.Undo(); }
        catch (COMException) { throw new InvalidOperationException("No hay nada que deshacer desde el teléfono."); }
        return Status();
    }

    /// <summary>Lee un rango ("A1:C10", o toda la zona usada si va vacío) como texto, con un máximo de 500 celdas.</summary>
    public static JsonNode ReadRange(string? range) => Seguro<JsonNode>(() =>
    {
        var app = Requerir();
        var sheet = Hoja(app);
        dynamic r = string.IsNullOrWhiteSpace(range) ? sheet.UsedRange : Rango(sheet, range);
        int rows = (int)r.Rows.Count, cols = (int)r.Columns.Count;
        int firstRow = (int)r.Row, firstCol = (int)r.Column;
        if (rows * cols > 500) throw new ArgumentException($"El rango tiene {rows * cols} celdas; pide como máximo 500 a la vez.");
        var data = new JsonArray();
        for (int i = 1; i <= rows; i++)
        {
            var line = new JsonArray();
            for (int j = 1; j <= cols; j++)
            {
                var cell = r.Cells[i, j];
                var formula = Convert.ToString(cell.Formula) ?? "";
                line.Add(formula.StartsWith('=') ? $"{Texto(cell)} ({formula})" : Texto(cell));
            }
            data.Add(line);
        }
        return new JsonObject
        {
            ["sheet"] = (string)sheet.Name,
            ["range"] = (string)r.Address(false, false),
            ["firstRow"] = firstRow,
            ["firstCol"] = NombreColumna(firstCol),
            ["rows"] = data,
        };
    });

    /// <summary>Escribe un valor en una celda o una tabla de valores (filas) desde una celda inicial.</summary>
    public static JsonNode WriteRange(string cell, JsonNode values) => Seguro<JsonNode>(() =>
    {
        var app = Requerir();
        var sheet = Hoja(app);
        var start = Rango(sheet, cell);
        int r0 = (int)start.Row, c0 = (int)start.Column;
        var table = new List<List<string>>();
        if (values is JsonArray arr)
        {
            foreach (var row in arr)
                table.Add(row is JsonArray cells ? cells.Select(c => c?.ToString() ?? "").ToList() : new List<string> { row?.ToString() ?? "" });
        }
        else table.Add(new List<string> { values.ToString() });
        if (table.Sum(t => t.Count) > 500) throw new ArgumentException("Son demasiadas celdas; escribe como máximo 500 a la vez.");
        int nFilas = table.Count, nCols = table.Max(t => t.Count);
        Registrar(Capturar(sheet.Range[sheet.Cells[r0, c0], sheet.Cells[Math.Min(r0 + nFilas - 1, 1048576), Math.Min(c0 + nCols - 1, 16384)]]));
        for (int i = 0; i < table.Count; i++)
            for (int j = 0; j < table[i].Count; j++)
            {
                var v = table[i][j];
                if (r0 + i > 1048576 || c0 + j > 16384) throw new ArgumentException("Eso se sale del borde de la hoja.");
                var target = sheet.Cells[r0 + i, c0 + j];
                ComprobarEditable(sheet, target);
                if (v.StartsWith('=')) PonerFormula(target, v); else target.Value2 = v;
            }
        return Status();
    });

    /// <summary>Rango por dirección ("B7", "A1:C10") o por nombre definido; con un error claro si no existe.</summary>
    private static dynamic Rango(dynamic sheet, string texto)
    {
        var t = (texto ?? "").Trim().Replace("$", "");
        try { return sheet.Range[t.ToUpperInvariant()]; }
        catch (COMException) { }
        try { return sheet.Range[t]; } // un nombre definido en el libro
        catch (COMException) { throw new ArgumentException($"No entiendo «{texto}» como celda o rango. Usa algo como B7 o A1:C10."); }
    }

    private static string NombreColumna(int n)
    {
        var s = "";
        while (n > 0) { n--; s = (char)('A' + n % 26) + s; n /= 26; }
        return s;
    }

    // ------------------------------------------------------------------ editar

    /// <summary>Ir a una celda ("B7") o moverse relativo a la activa (dRow/dCol).</summary>
    public static JsonNode Select(string? cell, int dRow, int dCol) => Seguro<JsonNode>(() =>
    {
        var app = Requerir();
        var sheet = Hoja(app);
        if (!string.IsNullOrWhiteSpace(cell))
        {
            Rango(sheet, cell).Select();
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
        var sheet = Hoja(app);
        var a = app.ActiveCell;
        ComprobarEditable(sheet, a);
        Registrar(Capturar(a));
        // Los números y fórmulas ("=SUMA(A1:A5)") se interpretan como en Excel; lo demás queda como texto
        if (value.StartsWith('=')) PonerFormula(a, value); else a.Value2 = value;
        int r = (int)a.Row, c = (int)a.Column;
        switch (next)
        {
            case "down": sheet.Cells[Math.Min(r + 1, 1048576), c].Select(); break;
            case "right": sheet.Cells[r, Math.Min(c + 1, 16384)].Select(); break;
        }
        return Status();
    });

    public static JsonNode Action(string action) => Seguro<JsonNode>(() =>
    {
        var app = Requerir();
        if (action == "enable_edit") { HabilitarEdicion(app); return Status(); }
        Hoja(app); // falla con un mensaje claro si no hay hoja (vista protegida, gráfico, sin libro)
        switch (action)
        {
            case "save": Guardar(app); break;
            case "undo": return Deshacer();
            case "clear": Registrar(Capturar(app.Selection)); app.Selection.ClearContents(); break;
            case "bold": { Registrar(Capturar(app.Selection, true)); object cur = app.Selection.Font.Bold; app.Selection.Font.Bold = !(cur is bool b && b); break; }
            case "autosum": Registrar(Capturar(app.Selection)); app.Selection.Formula = "=SUM(" + SumaArriba(app) + ")"; break;
            case "filter": app.ActiveCell.AutoFilter(); break;
            case "next_sheet": SiguienteHoja(app, +1); break;
            case "prev_sheet": SiguienteHoja(app, -1); break;
            case "new_sheet": app.Worksheets.Add(After: app.ActiveSheet); break;
            case "copy": app.Selection.Copy(); break;
            case "cut": app.Selection.Cut(); break;
            case "paste": app.ActiveSheet.Paste(); app.CutCopyMode = false; break;
            case "escape": app.CutCopyMode = false; break;
            case "home": app.ActiveSheet.Range["A1"].Select(); break;
            case "jump_up": Saltar(app, -4162); break;    // xlUp
            case "jump_down": Saltar(app, -4121); break;  // xlDown
            case "jump_left": Saltar(app, -4159); break;  // xlToLeft
            case "jump_right": Saltar(app, -4161); break; // xlToRight
            case "insert_row":
            {
                string dirFila = (string)app.Selection.EntireRow.Address;
                app.Selection.EntireRow.Insert();
                Registrar(a => { a.ActiveSheet.Range[dirFila].EntireRow.Delete(); });
                break;
            }
            case "delete_row":
            {
                string dirFila = (string)app.Selection.EntireRow.Address;
                dynamic usada = app.Intersect(app.Selection.EntireRow, app.ActiveSheet.UsedRange);
                var restaurar = usada == null ? null : Capturar(usada, false);
                app.Selection.EntireRow.Delete();
                if (restaurar != null) Registrar(a => { a.ActiveSheet.Range[dirFila].EntireRow.Insert(); restaurar(a); });
                break;
            }
            case "autofit": app.Selection.EntireColumn.AutoFit(); break;
            case "percent": Registrar(Capturar(app.Selection, true)); app.Selection.NumberFormat = "0%"; break;
            case "money": Registrar(Capturar(app.Selection, true)); app.Selection.NumberFormat = "$#,##0"; break;
            case "number": Registrar(Capturar(app.Selection, true)); app.Selection.NumberFormat = "General"; break;
            default: throw new ArgumentException("Acción de Excel desconocida: " + action);
        }
        return Status();
    });

    /// <summary>Guarda sin abrir cuadros que dejarían a Excel esperando en el PC (libro nuevo, solo lectura, CSV).</summary>
    private static void Guardar(dynamic app)
    {
        var wb = app.ActiveWorkbook;
        if (string.IsNullOrEmpty((string)wb.Path))
            throw new InvalidOperationException("Este libro es nuevo y todavía no tiene nombre. Guárdalo una vez en el PC (Ctrl + G) y después podrás guardar desde el teléfono.");
        if ((bool)wb.ReadOnly)
            throw new InvalidOperationException("Este archivo está abierto en solo lectura (otra persona o programa lo está usando). Usa «Guardar como» en el PC.");
        bool avisos = (bool)app.DisplayAlerts;
        app.DisplayAlerts = false; // evita el "¿mantener el formato CSV?"
        try { wb.Save(); }
        finally { app.DisplayAlerts = avisos; }
    }

    /// <summary>Equivale a tocar «Habilitar edición» en la franja amarilla de Vista protegida.</summary>
    private static void HabilitarEdicion(dynamic app)
    {
        if (!EnVistaProtegida(app)) return;
        app.ProtectedViewWindows[1].Edit();
    }

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
        int idx = (int)app.ActiveSheet.Index;
        for (int i = 0; i < n; i++)
        {
            idx = ((idx + delta - 1) % n + n) % n + 1;
            var hoja = wb.Worksheets[idx];
            bool visible;
            try { visible = (int)hoja.Visible == -1; } catch { visible = true; }
            if (visible) { hoja.Activate(); return; }
        }
    }

    public static JsonNode SelectSheet(string name) => Seguro<JsonNode>(() =>
    {
        var app = Requerir();
        if (EnVistaProtegida(app)) throw new InvalidOperationException("El archivo está en Vista protegida. Toca «Habilitar edición» primero.");
        try { app.ActiveWorkbook.Worksheets[name].Activate(); }
        catch (COMException) { throw new InvalidOperationException($"No pude abrir la hoja «{name}»."); }
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
        var app = App();
        if (app == null && Instalado())
        {
            try { app = Lanzar(); }
            catch (Exception ex) { Log.Error("Excel: no se pudo lanzar por COM, se abre con Windows", ex); }
        }
        if (app != null)
        {
            // Se pide por COM: si el libro ya está abierto solo se muestra, y no sale el cuadro
            // "ya está abierto, ¿volver a abrir?" ni el de actualizar vínculos, que dejarían a Excel esperando en el PC.
            try
            {
                var completa = Path.GetFullPath(path);
                foreach (var wb in app.Workbooks)
                {
                    if (string.Equals((string)wb.FullName, completa, StringComparison.OrdinalIgnoreCase))
                    {
                        wb.Activate();
                        return new JsonObject { ["opening"] = false, ["already"] = true };
                    }
                }
                app.Workbooks.Open(completa, 0); // 0 = no actualizar vínculos externos
                return new JsonObject { ["opening"] = false };
            }
            catch (COMException ex) when ((uint)ex.HResult is 0x80010001 or 0x8001010A)
            {
                throw new InvalidOperationException("Excel está ocupado. Cierra el cuadro que tenga abierto e inténtalo de nuevo.");
            }
            catch (COMException ex)
            {
                // Con contraseña, dañado o bloqueado: que lo abra Windows y avise en pantalla lo que haga falta
                Log.Error("Excel: abrir por COM falló (" + ex.HResult.ToString("X") + "), se abre con Windows", ex);
            }
        }
        AppsModule.Start(path); // Windows lo abre con Excel (o el programa que tenga asociado)
        return new JsonObject { ["opening"] = true };
    }

    /// <summary>
    /// Abre Excel creándolo por COM. Un Excel lanzado con excel.exe no siempre queda localizable desde otro programa
    /// hasta que se usa; así queda registrado y visible desde el primer momento.
    /// </summary>
    private static dynamic Lanzar()
    {
        var tipo = Type.GetTypeFromProgID("Excel.Application") ?? throw new InvalidOperationException("No encontré Excel instalado en este PC.");
        dynamic app = Activator.CreateInstance(tipo)!;
        app.Visible = true;
        return app;
    }

    /// <summary>Abre Excel con un libro en blanco (o crea uno nuevo si Excel ya estaba abierto).</summary>
    public static JsonNode New()
    {
        if (!Instalado()) throw new InvalidOperationException("No encontré Excel instalado en este PC.");
        var app = App() ?? Lanzar();
        app.Visible = true;
        app.Workbooks.Add();
        return Status();
    }
}
