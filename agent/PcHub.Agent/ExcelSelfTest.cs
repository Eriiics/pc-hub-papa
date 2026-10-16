using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using PcHub.Agent.Modules;

namespace PcHub.Agent;

/// <summary>
/// Autoprueba del módulo de Excel contra un Excel real (<c>PcHub.Agent.exe --autoprueba-excel</c>).
/// Crea sus propios libros en una carpeta temporal, los cierra sin guardar y escribe el resultado en
/// %TEMP%\autoprueba-excel.txt. No toca los libros del usuario (pero necesita Excel instalado).
/// </summary>
public static class ExcelSelfTest
{
    private static readonly StringBuilder Out = new();
    private static int _ok, _fail;

    private static void Check(string name, bool cond, string detail = "")
    {
        if (cond) _ok++; else _fail++;
        Out.AppendLine((cond ? "  OK     " : "  FALLA  ") + name + (!cond && detail.Length > 0 ? "  → " + detail : ""));
    }

    /// <summary>Ejecuta algo que debe funcionar; devuelve el resultado o null (y lo anota) si falla.</summary>
    private static JsonNode? Try(string name, Func<JsonNode> f)
    {
        try { var r = f(); Check(name, true); return r; }
        catch (Exception ex) { Check(name, false, ex.GetType().Name + ": " + ex.Message); return null; }
    }

    /// <summary>Debe fallar con un mensaje en español claro (y rápido: sin dejar a Excel colgado).</summary>
    private static void Rechaza(string name, Func<JsonNode> f, string contiene)
    {
        var sw = Stopwatch.StartNew();
        try { f(); Check(name, false, "no falló"); }
        catch (Exception ex)
        {
            Check(name, ex.Message.Contains(contiene, StringComparison.OrdinalIgnoreCase) && sw.ElapsedMilliseconds < 8000,
                $"mensaje: «{ex.Message}» ({sw.ElapsedMilliseconds} ms)");
        }
    }

    private static string LeerCompartido(string ruta)
    {
        using var fs = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);
        return sr.ReadToEnd();
    }

    private static string Cel(JsonNode? st) => st?["cell"]?.ToString() ?? "?";

    private static void CerrarTodos(dynamic app)
    {
        foreach (var w in ((System.Collections.IEnumerable)app.Workbooks).Cast<object>().ToList())
        {
            try { ((dynamic)w).Close(false); } catch { }
        }
    }

    public static int Run()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "PcHub-AutopruebaExcel");
        if (Directory.Exists(tmp)) try { Directory.Delete(tmp, true); } catch { }
        Directory.CreateDirectory(tmp);
        dynamic? app = null;
        bool lanzamos = false;
        try
        {
            var yaAbierto = ExcelModule.Status()["running"]?.GetValue<bool>() == true;
            Out.AppendLine("== Excel ya estaba abierto: " + yaAbierto);
            if (!yaAbierto)
            {
                Check("Sin Excel: estado dice que no está abierto", ExcelModule.Status()["running"]?.GetValue<bool>() == false);
                Rechaza("Sin Excel: escribir da un error claro", () => ExcelModule.Write("1", "down"), "Excel no está abierto");
                Rechaza("Sin Excel: leer da un error claro", () => ExcelModule.ReadRange("A1"), "Excel no está abierto");
                lanzamos = true;
            }
            Try("Crear libro nuevo (abre Excel si hace falta)", () => ExcelModule.New());
            app = ExcelModule.App();
            if (lanzamos) app.Visible = true;

            // ---- Libro 1: datos y fórmulas
            Out.AppendLine("== Libro nuevo: escribir, fórmulas, formato");
            Try("Ir a A1", () => ExcelModule.Select("A1", 0, 0));
            foreach (var n in new[] { "10", "20", "30.5", "abc 1", "00123", "12/03/2024", "50%" })
                Try("Escribir «" + n + "»", () => ExcelModule.Write(n, "down"));
            var rg = ExcelModule.ReadRange("A1:A7");
            Out.AppendLine("  leído: " + rg["rows"]!.ToJsonString());
            Check("A1 vale 10", (string)app.Range["A1"].Text == "10");
            Check("Texto con letras se conserva", (string)app.Range["A4"].Text == "abc 1");
            Check("Porcentaje se interpreta como número", Convert.ToDouble(app.Range["A7"].Value2) == 0.5);
            Try("Ir a A8", () => ExcelModule.Select("A8", 0, 0));
            Try("Fórmula en español", () => ExcelModule.Write("=SUMA(A1:A3)", "none"));
            Check("=SUMA da 60.5 (no #¿NOMBRE?)", Math.Abs(Convert.ToDouble(app.Range["A8"].Value2) - 60.5) < 1e-9, (string)app.Range["A8"].Text);
            Try("Fórmula en inglés también", () => ExcelModule.Write("=SUM(A1:A3)", "none"));
            Check("=SUM da 60.5", Math.Abs(Convert.ToDouble(app.Range["A8"].Value2) - 60.5) < 1e-9);
            Try("Fórmula con SI y punto y coma", () => ExcelModule.Write("=SI(A1>5;\"alto\";\"bajo\")", "none"));
            Check("SI(...) da «alto»", (string)app.Range["A8"].Text == "alto", (string)app.Range["A8"].Text);
            Try("Escribir tabla 3×2 desde C1", () => ExcelModule.WriteRange("C1", JsonNode.Parse("[[\"Nombre\",\"Monto\"],[\"Ana\",100],[\"Luis\",250]]")!));
            Check("Tabla escrita (D3 = 250)", Convert.ToDouble(app.Range["D3"].Value2) == 250);
            Try("Ir a D4", () => ExcelModule.Select("D4", 0, 0));
            Try("autosum", () => ExcelModule.Action("autosum"));
            Check("autosum = 350", Convert.ToDouble(app.Range["D4"].Value2) == 350, (string)app.Range["D4"].Text);
            Try("negrita", () => { ExcelModule.Select("C1:D1", 0, 0); return ExcelModule.Action("bold"); });
            Check("C1 en negrita", (bool)app.Range["C1"].Font.Bold);
            Try("negrita otra vez la quita", () => ExcelModule.Action("bold"));
            Check("C1 sin negrita", !(bool)app.Range["C1"].Font.Bold);
            Try("formato porcentaje", () => { ExcelModule.Select("D2", 0, 0); return ExcelModule.Action("percent"); });
            Try("formato dinero", () => ExcelModule.Action("money"));
            Try("formato general", () => ExcelModule.Action("number"));
            Try("autofit", () => ExcelModule.Action("autofit"));
            Try("filtro", () => { ExcelModule.Select("C1", 0, 0); return ExcelModule.Action("filter"); });
            Try("quitar filtro", () => ExcelModule.Action("filter"));
            Try("copiar/pegar", () => { ExcelModule.Select("C2", 0, 0); ExcelModule.Action("copy"); ExcelModule.Select("F2", 0, 0); return ExcelModule.Action("paste"); });
            Check("F2 = Ana", (string)app.Range["F2"].Text == "Ana");
            Try("cortar/pegar", () => { ExcelModule.Select("F2", 0, 0); ExcelModule.Action("cut"); ExcelModule.Select("G2", 0, 0); return ExcelModule.Action("paste"); });
            Check("G2 = Ana, F2 vacío", (string)app.Range["G2"].Text == "Ana" && (string)app.Range["F2"].Text == "");
            Try("insertar fila", () => { ExcelModule.Select("A2", 0, 0); return ExcelModule.Action("insert_row"); });
            Try("borrar fila", () => ExcelModule.Action("delete_row"));
            Try("deshacer el borrar fila", () => ExcelModule.Action("undo"));
            Check("la fila borrada volvió (Ana sigue en C3, bajo la fila insertada)", (string)app.Range["C3"].Text == "Ana", (string)app.Range["C3"].Text);
            Try("deshacer el insertar fila", () => ExcelModule.Action("undo"));
            Check("sin la fila insertada, Ana vuelve a C2", (string)app.Range["C2"].Text == "Ana", (string)app.Range["C2"].Text);
            Try("escribir y deshacer", () => { ExcelModule.Select("H1", 0, 0); ExcelModule.Write("nuevo", "none"); return ExcelModule.Action("undo"); });
            Check("H1 quedó vacía tras deshacer", (string)app.Range["H1"].Text == "");
            Try("negrita y deshacer", () => { ExcelModule.Select("C2", 0, 0); ExcelModule.Action("bold"); return ExcelModule.Action("undo"); });
            Check("C2 sin negrita tras deshacer", !(bool)app.Range["C2"].Font.Bold);
            Try("borrar celda y deshacer", () => { ExcelModule.Select("C2", 0, 0); ExcelModule.Action("clear"); return ExcelModule.Action("undo"); });
            Check("C2 recuperó «Ana»", (string)app.Range["C2"].Text == "Ana");
            Try("saltar en las 4 direcciones", () => { ExcelModule.Select("C1", 0, 0); ExcelModule.Action("jump_down"); ExcelModule.Action("jump_right"); ExcelModule.Action("jump_up"); ExcelModule.Action("jump_left"); return ExcelModule.Action("home"); });
            Check("home deja en A1", Cel(ExcelModule.Status()) == "A1");
            Try("mover relativo (+2 filas, +1 col)", () => ExcelModule.Select(null, 2, 1));
            Check("quedó en B3", Cel(ExcelModule.Status()) == "B3");
            Try("borde: subir más allá de la fila 1", () => { ExcelModule.Select("A1", 0, 0); return ExcelModule.Select(null, -5, -5); });
            var sw = Stopwatch.StartNew();
            var g = ExcelModule.Grid(40, 12);
            Check("Cuadrícula 40×12 en menos de 3 s", sw.ElapsedMilliseconds < 3000, sw.ElapsedMilliseconds + " ms");
            Check("Cuadrícula trae 40 filas", g["rows"]!.AsArray().Count == 40);
            Rechaza("Rango inválido da mensaje claro", () => ExcelModule.Select("ZZZ", 0, 0), "No entiendo");
            Rechaza("Leer más de 500 celdas lo rechaza", () => ExcelModule.ReadRange("A1:Z100"), "500");
            Rechaza("Acción desconocida", () => ExcelModule.Action("volar"), "desconocida");
            Rechaza("Guardar un libro nuevo sin nombre no se cuelga", () => ExcelModule.Action("save"), "no tiene nombre");
            Check("Excel sigue interactivo tras guardar sin nombre", (bool)app.Interactive);

            // ---- Hojas
            Out.AppendLine("== Hojas");
            Try("hoja nueva", () => ExcelModule.Action("new_sheet"));
            Try("escribir en la hoja nueva", () => ExcelModule.Write("hola", "down"));
            dynamic wb = app.ActiveWorkbook;
            var hn = wb.Worksheets.Add(After: wb.Worksheets[wb.Worksheets.Count]);
            hn.Name = "Mi hoja (2) ñandú";
            Try("hoja con espacios, tildes y paréntesis", () => ExcelModule.SelectSheet("Mi hoja (2) ñandú"));
            Check("hoja activa correcta", (string)app.ActiveSheet.Name == "Mi hoja (2) ñandú");
            var oculta = wb.Worksheets.Add(After: wb.Worksheets[wb.Worksheets.Count]);
            oculta.Name = "Oculta"; oculta.Visible = 0;
            var st = ExcelModule.Status();
            Check("Hoja oculta no aparece en la lista", !st["sheets"]!.AsArray().Any(x => x!.ToString() == "Oculta"));
            Try("siguiente hoja salta la oculta (varias veces)", () => { for (int i = 0; i < 6; i++) ExcelModule.Action("next_sheet"); return ExcelModule.Action("prev_sheet"); });
            Check("la hoja activa no es la oculta", (string)app.ActiveSheet.Name != "Oculta");
            Rechaza("hoja que no existe", () => ExcelModule.SelectSheet("NoExiste"), "No pude abrir");

            // ---- Celdas combinadas
            Out.AppendLine("== Celdas combinadas");
            Try("ir a la hoja 1", () => ExcelModule.SelectSheet((string)wb.Worksheets[1].Name));
            app.Range["J1:L2"].Merge();
            Try("escribir en celda combinada", () => { ExcelModule.Select("J1", 0, 0); return ExcelModule.Write("titulo", "down"); });
            Check("combinada tiene «titulo»", (string)app.Range["J1"].Text == "titulo");
            Try("cuadrícula con combinadas", () => ExcelModule.Grid(14, 6));

            // ---- Hoja protegida
            Out.AppendLine("== Hoja protegida");
            var hp = wb.Worksheets.Add(After: wb.Worksheets[wb.Worksheets.Count]);
            hp.Name = "Prot"; hp.Range["B2"].Locked = false; hp.Protect();
            Try("ir a hoja protegida", () => ExcelModule.SelectSheet("Prot"));
            var sp = ExcelModule.Status();
            Check("Estado dice que la hoja está protegida", sp["protectedSheet"]?.GetValue<bool>() == true);
            Rechaza("escribir en celda bloqueada: mensaje claro", () => { ExcelModule.Select("A1", 0, 0); return ExcelModule.Write("x", "down"); }, "protegida");
            Try("escribir en celda desbloqueada de hoja protegida", () => { ExcelModule.Select("B2", 0, 0); return ExcelModule.Write("ok", "none"); });
            Check("B2 = ok", (string)hp.Range["B2"].Text == "ok");
            Rechaza("WriteRange en bloqueada", () => ExcelModule.WriteRange("A1", JsonNode.Parse("[[1,2]]")!), "protegida");
            hp.Unprotect();

            // ---- Hoja de gráfico
            Out.AppendLine("== Hoja de gráfico y objeto seleccionado");
            var datos = wb.Worksheets[1];
            datos.Activate();
            var gr = wb.Charts.Add(After: wb.Sheets[wb.Sheets.Count]);
            gr.Name = "Grafico";
            gr.Activate();
            var sg = ExcelModule.Status();
            Check("Estado con hoja de gráfico activa sigue mostrando el libro", sg["workbook"] != null);
            Rechaza("Escribir en hoja de gráfico da mensaje claro", () => ExcelModule.Write("x", "down"), "gráfico");
            Rechaza("Cuadrícula en hoja de gráfico da mensaje claro", () => ExcelModule.Grid(10, 5), "gráfico");
            datos.Activate();
            var forma = datos.Shapes.AddShape(1, 300, 300, 50, 50); forma.Select();
            var sf = ExcelModule.Status();
            Check("Estado con una forma seleccionada sigue mostrando el libro", sf["workbook"] != null);
            Try("Ir a una celda con una forma seleccionada", () => ExcelModule.Select("A1", 0, 0));
            forma.Delete();

            // ---- Guardar, abrir, ya abierto, CSV, varios libros
            Out.AppendLine("== Guardar y abrir archivos");
            var ruta1 = Path.Combine(tmp, "ventas con espacios ñ.xlsx");
            wb.SaveAs(ruta1);
            Try("Guardar un libro con nombre", () => { ExcelModule.Select("A1", 0, 0); ExcelModule.Write("cambio", "none"); return ExcelModule.Action("save"); });
            Check("Quedó guardado (Saved = true)", (bool)wb.Saved);
            var ruta2 = Path.Combine(tmp, "otro.xlsx");
            var wb2 = app.Workbooks.Add(); wb2.Worksheets[1].Range["A1"].Value2 = "segundo"; wb2.SaveAs(ruta2); wb2.Close(false);
            var ruta3 = Path.Combine(tmp, "datos.csv");
            File.WriteAllText(ruta3, "a,b\n1,2\n3,4\n");
            Try("Abrir otro libro por COM (xlsx)", () => ExcelModule.Open(ruta2));
            Check("Es el libro activo", ((string)app.ActiveWorkbook.Name) == "otro.xlsx");
            var n0 = (int)app.Workbooks.Count;
            Try("Abrir el mismo libro ya abierto no lo duplica ni pregunta", () => ExcelModule.Open(ruta2));
            Check("Sigue habiendo los mismos libros", (int)app.Workbooks.Count == n0);
            Try("Volver al primer libro (ya abierto)", () => ExcelModule.Open(ruta1));
            Check("Es el libro activo (ventas)", ((string)app.ActiveWorkbook.Name).StartsWith("ventas"));
            Try("Abrir un CSV", () => ExcelModule.Open(ruta3));
            Check("CSV abierto y activo", ((string)app.ActiveWorkbook.Name) == "datos.csv");
            Try("Leer el CSV", () => ExcelModule.ReadRange(""));
            Try("Escribir en el CSV y guardar sin diálogo", () => { ExcelModule.Select("C1", 0, 0); ExcelModule.Write("z", "none"); return ExcelModule.Action("save"); });
            Check("CSV guardado con el cambio", LeerCompartido(ruta3).Contains('z'));
            Rechaza("Abrir archivo que no existe", () => ExcelModule.Open(Path.Combine(tmp, "nada.xlsx")), "no existe");
            Rechaza("Abrir un archivo que no es una hoja", () => { File.WriteAllText(Path.Combine(tmp, "a.txt"), "x"); return ExcelModule.Open(Path.Combine(tmp, "a.txt")); }, "no es una hoja");
            Try("Lista de archivos recientes", () => ExcelModule.Files());

            // ---- Solo lectura
            Out.AppendLine("== Solo lectura y vista protegida");
            var rutaRo = Path.Combine(tmp, "solo lectura.xlsx");
            var wb3 = app.Workbooks.Add(); wb3.SaveAs(rutaRo); wb3.Close(false);
            var wbRo = app.Workbooks.Open(rutaRo, 0, true);
            Try("Libro en solo lectura se puede leer", () => ExcelModule.Grid(5, 3));
            Rechaza("Guardar solo lectura: mensaje claro", () => ExcelModule.Action("save"), "solo lectura");
            wbRo.Close(false);

            // Vista protegida: se abre el archivo por COM como lo haría Excel con un archivo descargado
            CerrarTodos(app);
            Out.AppendLine("  (todos los libros cerrados; abriendo en Vista protegida)");
            dynamic? pv = null;
            try { pv = app.ProtectedViewWindows.Open(ruta1); } catch (Exception ex) { Out.AppendLine("  (no se pudo simular Vista protegida: " + ex.Message + ")"); }
            if (pv != null)
            {
                Thread.Sleep(500);
                var s = ExcelModule.Status();
                Check("Vista protegida: el estado lo indica", s["protectedView"]?.GetValue<bool>() == true, s.ToJsonString());
                Rechaza("Vista protegida: escribir da mensaje claro", () => ExcelModule.Write("x", "down"), "Vista protegida");
                Rechaza("Vista protegida: cuadrícula da mensaje claro", () => ExcelModule.Grid(5, 3), "Vista protegida");
                Try("Habilitar edición desde el teléfono", () => ExcelModule.Action("enable_edit"));
                Check("Ahora hay un libro editable", (int)app.Workbooks.Count == 1);
                Try("Escribir tras habilitar edición", () => ExcelModule.Write("editado", "none"));
            }

            // ---- Sin libros
            CerrarTodos(app);
            Out.AppendLine("== Excel abierto sin libros");
            var s0 = ExcelModule.Status();
            Check("Estado: Excel abierto sin libro", s0["running"]?.GetValue<bool>() == true && s0["workbook"] == null, s0.ToJsonString());
            Rechaza("Sin libro: escribir da mensaje claro", () => ExcelModule.Write("x", "down"), "libro");
            Rechaza("Sin libro: cuadrícula da mensaje claro", () => ExcelModule.Grid(5, 3), "libro");
            Try("Crear libro nuevo estando Excel sin libros", () => ExcelModule.New());
            Try("Escribir en él", () => ExcelModule.Write("1", "down"));
        }
        catch (Exception ex)
        {
            Check("Autoprueba terminó sin excepciones inesperadas", false, ex.ToString());
        }
        finally
        {
            try
            {
                if (app != null)
                {
                    try { app.Interactive = true; } catch { }
                    app.DisplayAlerts = false;
                    CerrarTodos(app);
                    if (lanzamos) app.Quit();
                }
            }
            catch { }
            try { Directory.Delete(tmp, true); } catch { }
            Out.AppendLine($"\nRESULTADO: {_ok} bien, {_fail} con falla");
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "autoprueba-excel.txt"), Out.ToString(), Encoding.UTF8);
        }
        return _fail == 0 ? 0 : 1;
    }
}
