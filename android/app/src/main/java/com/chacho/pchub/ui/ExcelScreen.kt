package com.chacho.pchub.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.ArrowBack
import androidx.compose.material.icons.filled.ArrowDownward
import androidx.compose.material.icons.filled.ArrowForward
import androidx.compose.material.icons.filled.ArrowUpward
import androidx.compose.material.icons.filled.Save
import androidx.compose.material.icons.filled.Undo
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.chacho.pchub.net.Pc
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import org.json.JSONObject

private data class ExcelFile(val name: String, val path: String, val folder: String, val modified: String)

/** Excel del PC: ver la hoja, moverte, escribir datos, guardar y abrir archivos recientes. */
@Composable
fun ExcelScreen() {
    val scope = rememberCoroutineScope()
    var status by remember { mutableStateOf<JSONObject?>(null) }
    var grid by remember { mutableStateOf<JSONObject?>(null) }
    var files by remember { mutableStateOf<List<ExcelFile>>(emptyList()) }
    var text by remember { mutableStateOf("") }
    var busy by remember { mutableStateOf(false) }

    val running = status?.optBoolean("running") == true
    val hasBook = running && !status!!.isNull("workbook") && status!!.optString("workbook").isNotEmpty()

    suspend fun refresh() {
        try {
            val st = Pc.call("excel.status")
            status = st
            if (st.optBoolean("running") && st.optString("workbook").isNotEmpty()) {
                grid = Pc.call("excel.grid", JSONObject().put("rows", 12).put("cols", 5))
            } else {
                grid = null
            }
        } catch (_: Exception) {
            // Excel ocupado o sin conexión: se vuelve a intentar en el próximo ciclo
        }
    }

    fun loadFiles() = scope.pcCall("excel.files", timeoutMs = 20_000) { d ->
        val arr = d.optJSONArray("files")
        files = if (arr == null) emptyList() else (0 until arr.length()).map {
            val o = arr.getJSONObject(it)
            ExcelFile(o.optString("name"), o.optString("path"), o.optString("folder"), o.optString("modified"))
        }
    }

    // Llama al PC y actualiza lo que se ve con la respuesta
    fun call(method: String, params: JSONObject = JSONObject(), after: (() -> Unit)? = null) {
        scope.pcCall(method, params, timeoutMs = 30_000) { st ->
            status = st
            after?.invoke()
            scope.launch { refresh() }
        }
    }

    LaunchedEffect(Unit) {
        loadFiles()
        while (true) {
            refresh()
            delay(1500)
        }
    }

    LazyColumn(
        contentPadding = PaddingValues(start = 16.dp, end = 16.dp, bottom = 24.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        // ---------- estado ----------
        item {
            GlassCard(Modifier.fillMaxWidth(), glow = if (hasBook) Neon.Lime else null) {
                if (status == null) {
                    Text("Conectando con Excel…", color = Neon.TextDim)
                } else if (status?.optBoolean("installed") == false) {
                    Text("No encontré Excel en este PC", style = MaterialTheme.typography.titleMedium, color = Neon.Text)
                    Text("Instala Microsoft Excel en el PC para usar esta pantalla.", style = MaterialTheme.typography.bodySmall, color = Neon.TextDim)
                } else if (!hasBook) {
                    Text(
                        if (running) "Excel está abierto, pero sin ningún archivo" else "Excel no está abierto",
                        style = MaterialTheme.typography.titleMedium, color = Neon.Text,
                    )
                    Text("Abre un archivo reciente de la lista de abajo o crea uno nuevo.", style = MaterialTheme.typography.bodySmall, color = Neon.TextDim)
                    Spacer(Modifier.height(10.dp))
                    NeonButton(if (busy) "Abriendo…" else "Libro nuevo en blanco", Modifier.fillMaxWidth(), icon = Icons.Filled.Add, accent = Neon.Lime, enabled = !busy) {
                        busy = true
                        scope.pcCall("excel.new", timeoutMs = 40_000) { status = it; busy = false; scope.launch { refresh() } }
                            .invokeOnCompletion { busy = false }
                    }
                } else {
                    val saved = status?.optBoolean("saved") == true
                    Text(status?.optString("workbook") ?: "", style = MaterialTheme.typography.titleMedium, color = Neon.Text, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    Text(
                        if (saved) "Guardado" else "Con cambios sin guardar",
                        style = MaterialTheme.typography.labelSmall,
                        color = if (saved) Neon.Lime else Neon.Amber,
                    )
                    val sheets = status?.optJSONArray("sheets")
                    if (sheets != null && sheets.length() > 1) {
                        Spacer(Modifier.height(8.dp))
                        Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                            val current = status?.optString("sheet")
                            for (i in 0 until sheets.length()) {
                                val name = sheets.getString(i)
                                NeonButton(name, outlined = name != current, accent = Neon.Lime) {
                                    call("excel.sheet", JSONObject().put("name", name))
                                }
                            }
                        }
                    }
                }
            }
        }

        if (hasBook) {
            // ---------- hoja ----------
            item { SectionTitle("Hoja") }
            item {
                GlassCard(Modifier.fillMaxWidth(), padding = 8.dp) {
                    val g = grid
                    if (g == null) {
                        Text("Cargando…", color = Neon.TextDim, modifier = Modifier.padding(8.dp))
                    } else {
                        val cols = g.getJSONArray("cols")
                        val rows = g.getJSONArray("rows")
                        val firstRow = g.optInt("firstRow")
                        val firstCol = g.optInt("firstCol")
                        val activeRow = g.optInt("activeRow")
                        val activeCol = g.optInt("activeCol")
                        Row {
                            Box(Modifier.width(34.dp))
                            for (c in 0 until cols.length()) {
                                Box(Modifier.weight(1f).padding(1.dp), contentAlignment = Alignment.Center) {
                                    Text(cols.getString(c), color = if (firstCol + c == activeCol) Neon.Lime else Neon.TextFaint, fontSize = 11.sp)
                                }
                            }
                        }
                        for (r in 0 until rows.length()) {
                            val line = rows.getJSONArray(r)
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                Box(Modifier.width(34.dp), contentAlignment = Alignment.CenterEnd) {
                                    Text("${firstRow + r}", color = if (firstRow + r == activeRow) Neon.Lime else Neon.TextFaint, fontSize = 11.sp, modifier = Modifier.padding(end = 4.dp))
                                }
                                for (c in 0 until line.length()) {
                                    val active = firstRow + r == activeRow && firstCol + c == activeCol
                                    val address = cols.getString(c) + (firstRow + r)
                                    Box(
                                        Modifier
                                            .weight(1f)
                                            .height(34.dp)
                                            .padding(1.dp)
                                            .background(if (active) Neon.Lime.copy(alpha = 0.18f) else Neon.Text.copy(alpha = 0.04f))
                                            .border(if (active) 1.5.dp else 0.5.dp, if (active) Neon.Lime else Neon.Text.copy(alpha = 0.10f))
                                            .clickable { call("excel.select", JSONObject().put("cell", address)) }
                                            .padding(horizontal = 3.dp),
                                        contentAlignment = Alignment.CenterStart,
                                    ) {
                                        Text(line.getString(c), color = Neon.Text, fontSize = 12.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // ---------- movimiento ----------
            item {
                Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    NeonButton("", Modifier.weight(1f), icon = Icons.Filled.ArrowBack, outlined = true) { call("excel.select", JSONObject().put("dCol", -1)) }
                    NeonButton("", Modifier.weight(1f), icon = Icons.Filled.ArrowUpward, outlined = true) { call("excel.select", JSONObject().put("dRow", -1)) }
                    NeonButton("", Modifier.weight(1f), icon = Icons.Filled.ArrowDownward, outlined = true) { call("excel.select", JSONObject().put("dRow", 1)) }
                    NeonButton("", Modifier.weight(1f), icon = Icons.Filled.ArrowForward, outlined = true) { call("excel.select", JSONObject().put("dCol", 1)) }
                }
            }

            item {
                Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    NeonButton("Inicio", Modifier.weight(1f), outlined = true) { call("excel.action", JSONObject().put("a", "home")) }
                    NeonButton("Al borde ↑", Modifier.weight(1f), outlined = true) { call("excel.action", JSONObject().put("a", "jump_up")) }
                    NeonButton("Al borde ↓", Modifier.weight(1f), outlined = true) { call("excel.action", JSONObject().put("a", "jump_down")) }
                }
            }
            item {
                Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    NeonButton("Al borde ←", Modifier.weight(1f), outlined = true) { call("excel.action", JSONObject().put("a", "jump_left")) }
                    NeonButton("Al borde →", Modifier.weight(1f), outlined = true) { call("excel.action", JSONObject().put("a", "jump_right")) }
                }
            }

            // ---------- escribir ----------
            item {
                val cell = status?.optString("cell") ?: ""
                SectionTitle("Escribir en $cell")
                val current = status?.optString("formula") ?: ""
                if (current.isNotEmpty()) {
                    Text("Ahora tiene: $current", style = MaterialTheme.typography.labelSmall, color = Neon.TextFaint, modifier = Modifier.padding(start = 4.dp, bottom = 4.dp))
                }
                OutlinedTextField(
                    value = text,
                    onValueChange = { text = it },
                    modifier = Modifier.fillMaxWidth(),
                    singleLine = true,
                    placeholder = { Text("Texto, número o fórmula (=SUMA(A1:A5))") },
                )
                Spacer(Modifier.height(8.dp))
                Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    NeonButton("Poner y bajar", Modifier.weight(1f), accent = Neon.Lime, enabled = text.isNotEmpty()) {
                        call("excel.write", JSONObject().put("value", text).put("next", "down")) { text = "" }
                    }
                    NeonButton("Poner y →", Modifier.weight(1f), accent = Neon.Lime, outlined = true, enabled = text.isNotEmpty()) {
                        call("excel.write", JSONObject().put("value", text).put("next", "right")) { text = "" }
                    }
                }
            }

            // ---------- acciones ----------
            item { SectionTitle("Acciones") }
            item {
                Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    NeonButton("Guardar", Modifier.weight(1f), icon = Icons.Filled.Save, accent = Neon.Lime) { call("excel.action", JSONObject().put("a", "save")) { Toaster.show("Guardado") } }
                    NeonButton("Deshacer", Modifier.weight(1f), icon = Icons.Filled.Undo, outlined = true) { call("excel.action", JSONObject().put("a", "undo")) }
                }
            }
            item {
                Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    NeonButton("Σ Sumar arriba", Modifier.weight(1f), outlined = true) { call("excel.action", JSONObject().put("a", "autosum")) }
                    NeonButton("Negrita", Modifier.weight(1f), outlined = true) { call("excel.action", JSONObject().put("a", "bold")) }
                    NeonButton("Borrar", Modifier.weight(1f), outlined = true, accent = Neon.Red) { call("excel.action", JSONObject().put("a", "clear")) }
                }
            }
            item {
                Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    NeonButton("◀ Hoja", Modifier.weight(1f), outlined = true) { call("excel.action", JSONObject().put("a", "prev_sheet")) }
                    NeonButton("Hoja ▶", Modifier.weight(1f), outlined = true) { call("excel.action", JSONObject().put("a", "next_sheet")) }
                    NeonButton("+ Hoja", Modifier.weight(1f), outlined = true, accent = Neon.Lime) { call("excel.action", JSONObject().put("a", "new_sheet")) }
                }
            }
            item {
                Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    NeonButton("Copiar", Modifier.weight(1f), outlined = true) { call("excel.action", JSONObject().put("a", "copy")) }
                    NeonButton("Cortar", Modifier.weight(1f), outlined = true) { call("excel.action", JSONObject().put("a", "cut")) }
                    NeonButton("Pegar", Modifier.weight(1f), outlined = true, accent = Neon.Lime) { call("excel.action", JSONObject().put("a", "paste")) }
                }
            }
            item {
                Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    NeonButton("+ Fila", Modifier.weight(1f), outlined = true, accent = Neon.Lime) { call("excel.action", JSONObject().put("a", "insert_row")) }
                    NeonButton("− Fila", Modifier.weight(1f), outlined = true, accent = Neon.Red) { call("excel.action", JSONObject().put("a", "delete_row")) }
                    NeonButton("Ajustar ancho", Modifier.weight(1f), outlined = true) { call("excel.action", JSONObject().put("a", "autofit")) }
                }
            }
            item {
                Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    NeonButton("%", Modifier.weight(1f), outlined = true) { call("excel.action", JSONObject().put("a", "percent")) }
                    NeonButton("$", Modifier.weight(1f), outlined = true) { call("excel.action", JSONObject().put("a", "money")) }
                    NeonButton("Normal", Modifier.weight(1f), outlined = true) { call("excel.action", JSONObject().put("a", "number")) }
                }
            }
            item {
                NeonButton("Filtro en la columna", Modifier.fillMaxWidth(), outlined = true) { call("excel.action", JSONObject().put("a", "filter")) }
            }
        }

        // ---------- archivos ----------
        item { SectionTitle("Archivos recientes") }
        if (files.isEmpty()) {
            item { Text("No encontré hojas de cálculo en Recientes, Documentos, Escritorio ni Descargas.", color = Neon.TextDim, style = MaterialTheme.typography.bodySmall) }
        }
        items(files.size) { i ->
            val f = files[i]
            GlassCard(Modifier.fillMaxWidth(), padding = 12.dp, onClick = {
                Toaster.show("Abriendo ${f.name}…")
                scope.pcCall("excel.open", JSONObject().put("path", f.path), timeoutMs = 30_000)
            }) {
                Text(f.name, color = Neon.Text, style = MaterialTheme.typography.bodyMedium, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text("${f.folder} · ${f.modified}", color = Neon.TextFaint, style = MaterialTheme.typography.labelSmall, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
        }
    }
}
