package com.chacho.pchub.ui

import android.annotation.SuppressLint
import android.graphics.Color as AColor
import android.view.ViewGroup
import android.webkit.JavascriptInterface
import android.webkit.WebView
import android.webkit.WebViewClient
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.RestartAlt
import androidx.compose.material.icons.filled.Send
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import com.chacho.pchub.Prefs
import com.chacho.pchub.net.Pc
import kotlinx.coroutines.launch
import org.json.JSONObject

private data class TermTab(val id: String, val title: String, val running: Boolean, val onPc: Boolean)

private fun JSONObject.toTab() = TermTab(optString("id"), optString("title"), optBoolean("running"), optBoolean("onPc"))

/** Último tab abierto, para volver al mismo al entrar otra vez. */
private object TermMemory {
    var current: String? = null
}

/**
 * Terminal completa del PC (PowerShell): colores, cursor, vim, etc. La dibuja xterm.js en un WebView.
 * "Ver en el PC" abre o cierra en la pantalla del PC una ventana con la misma terminal.
 */
@SuppressLint("SetJavaScriptEnabled")
@Composable
fun TerminalScreen() {
    val scope = rememberCoroutineScope()
    val haptic = rememberHaptic()
    val tabs = remember { mutableStateListOf<TermTab>() }
    var current by remember { mutableStateOf(TermMemory.current) }
    var ready by remember { mutableStateOf(false) }
    var ctrl by remember { mutableStateOf(false) }
    var command by remember { mutableStateOf("") }
    var size by remember { mutableStateOf(100 to 30) }
    val webRef = remember { arrayOfNulls<WebView>(1) }

    fun js(code: String) {
        val w = webRef[0] ?: return
        w.post { w.evaluateJavascript(code, null) }
    }

    fun sendInput(data: String) {
        val id = current ?: return
        Pc.send("term.input", JSONObject().put("id", id).put("data", data))
    }

    fun upsert(t: TermTab) {
        val i = tabs.indexOfFirst { it.id == t.id }
        if (i >= 0) tabs[i] = t else tabs.add(t)
    }

    fun open() = scope.pcCall("term.open", JSONObject().put("cols", size.first).put("rows", size.second)) { d ->
        val t = d.toTab()
        upsert(t)
        current = t.id
    }

    // Puente con la página: lo que escribes en la terminal y su tamaño en columnas/filas
    val bridge = remember {
        object {
            @JavascriptInterface
            fun input(data: String) {
                var d = data
                // Ctrl "pegado": la siguiente letra se manda como Ctrl+letra
                if (ctrl && d.length == 1 && d[0].lowercaseChar() in 'a'..'z') {
                    d = (d[0].lowercaseChar().code - 96).toChar().toString()
                    ctrl = false
                }
                sendInput(d)
            }

            @JavascriptInterface
            fun resize(cols: Int, rows: Int) {
                size = cols to rows
                val id = current ?: return
                Pc.send("term.resize", JSONObject().put("id", id).put("cols", cols).put("rows", rows))
            }

            @JavascriptInterface
            fun ready() {
                webRef[0]?.post { ready = true }
            }
        }
    }

    // ---------- pestañas ----------
    LaunchedEffect(Unit) {
        scope.pcCall("term.list") { d ->
            val arr = d.optJSONArray("tabs")
            tabs.clear()
            if (arr != null) for (i in 0 until arr.length()) tabs.add(arr.getJSONObject(i).toTab())
            if (tabs.none { it.id == current }) current = tabs.firstOrNull()?.id
            if (tabs.isEmpty()) open()
        }
    }
    LaunchedEffect(Unit) {
        Pc.termState.collect { d ->
            if (d.optBoolean("closed")) {
                tabs.removeAll { it.id == d.optString("id") }
                if (current == d.optString("id")) current = tabs.firstOrNull()?.id
            } else {
                upsert(d.toTab())
            }
        }
    }

    // ---------- conectar la pestaña actual con la página ----------
    LaunchedEffect(current, ready) {
        val id = current ?: return@LaunchedEffect
        if (!ready) return@LaunchedEffect
        TermMemory.current = id
        try {
            val d = Pc.call("term.attach", JSONObject().put("id", id).put("cols", size.first).put("rows", size.second), timeoutMs = 15_000)
            js("pchubReset()")
            val data = d.optString("data")
            if (data.isNotEmpty()) js("pchubWrite('$data')")
            js("pchubFit()")
        } catch (e: Exception) {
            Toaster.show(e.message ?: "Error")
        }
    }
    LaunchedEffect(Unit) {
        Pc.termOut.collect { (id, data) ->
            if (id == current && data.isNotEmpty()) js("pchubWrite('$data')")
        }
    }
    DisposableEffect(Unit) {
        onDispose { Pc.send("term.detach") }
    }

    val cur = tabs.firstOrNull { it.id == current }

    Column(Modifier.fillMaxSize().padding(horizontal = 10.dp, vertical = 6.dp)) {
        // ---------- pestañas ----------
        Row(verticalAlignment = Alignment.CenterVertically) {
            Row(
                Modifier.weight(1f).horizontalScroll(rememberScrollState()),
                horizontalArrangement = Arrangement.spacedBy(6.dp),
            ) {
                tabs.forEach { t ->
                    val active = t.id == current
                    Text(
                        (if (t.running) "" else "⏹ ") + t.title + if (t.onPc) " 🖥" else "",
                        color = if (active) Neon.Lime else Neon.TextDim,
                        fontWeight = if (active) FontWeight.Bold else FontWeight.Medium,
                        style = MaterialTheme.typography.labelLarge,
                        modifier = Modifier
                            .glass(RoundedCornerShape(50), tint = if (active) Neon.Lime else null)
                            .clickable { current = t.id }
                            .padding(horizontal = 12.dp, vertical = 7.dp),
                    )
                }
            }
            NeonIconButton(Icons.Filled.Add, "Nueva terminal", size = 36.dp, accent = Neon.Lime) { open() }
        }

        Spacer(Modifier.height(6.dp))

        // ---------- ver en el PC / reiniciar / cerrar ----------
        if (cur != null) Row(verticalAlignment = Alignment.CenterVertically) {
            Switch(
                checked = cur.onPc,
                onCheckedChange = { show ->
                    upsert(cur.copy(onPc = show))
                    scope.pcCall("term.showPc", JSONObject().put("id", cur.id).put("show", show), timeoutMs = 15_000) { upsert(it.toTab()) }
                },
                colors = SwitchDefaults.colors(checkedTrackColor = Neon.Lime),
            )
            Spacer(Modifier.width(8.dp))
            Text("Ver en el PC", color = Neon.Text, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.weight(1f))
            NeonIconButton(Icons.Filled.RestartAlt, "Reiniciar", size = 36.dp, accent = Neon.Amber) {
                scope.pcCall("term.restart", JSONObject().put("id", cur.id)) { js("pchubReset()") }
            }
            Spacer(Modifier.width(6.dp))
            NeonIconButton(Icons.Filled.Close, "Cerrar terminal", size = 36.dp, accent = Neon.Red) {
                scope.pcCall("term.close", JSONObject().put("id", cur.id))
            }
        }

        Spacer(Modifier.height(6.dp))

        // ---------- la terminal ----------
        Box(
            Modifier
                .weight(1f)
                .fillMaxWidth()
                .clip(RoundedCornerShape(14.dp))
                .glass(RoundedCornerShape(14.dp))
                .padding(2.dp)
        ) {
            AndroidView(
                factory = { ctx ->
                    WebView(ctx).apply {
                        layoutParams = ViewGroup.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT)
                        setBackgroundColor(AColor.TRANSPARENT)
                        settings.javaScriptEnabled = true
                        settings.domStorageEnabled = true
                        webViewClient = WebViewClient()
                        addJavascriptInterface(bridge, "PcHubBridge")
                        loadUrl("file:///android_asset/terminal/index.html")
                        webRef[0] = this
                    }
                },
                modifier = Modifier.fillMaxSize(),
            )
        }

        Spacer(Modifier.height(6.dp))

        // ---------- teclas que el teclado del teléfono no tiene ----------
        Row(
            Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()),
            horizontalArrangement = Arrangement.spacedBy(5.dp),
        ) {
            TermKey("⌨", accent = Neon.Lime) { js("pchubFocus()") }
            TermKey("Esc") { sendInput("\u001b") }
            TermKey("Tab") { sendInput("\t") }
            TermKey("Ctrl", active = ctrl) { ctrl = !ctrl }
            TermKey("^C", accent = Neon.Red) { haptic(true); sendInput("\u0003") }
            TermKey("↑") { sendInput("\u001b[A") }
            TermKey("↓") { sendInput("\u001b[B") }
            TermKey("←") { sendInput("\u001b[D") }
            TermKey("→") { sendInput("\u001b[C") }
            TermKey("Inicio") { sendInput("\u001b[H") }
            TermKey("Fin") { sendInput("\u001b[F") }
            TermKey("RePág") { sendInput("\u001b[5~") }
            TermKey("AvPág") { sendInput("\u001b[6~") }
            TermKey("A−") {
                Prefs.terminalFont = (Prefs.terminalFont - 1).coerceAtLeast(7)
                js("pchubFont(${Prefs.terminalFont})")
            }
            TermKey("A+") {
                Prefs.terminalFont = (Prefs.terminalFont + 1).coerceAtMost(22)
                js("pchubFont(${Prefs.terminalFont})")
            }
        }
        LaunchedEffect(ready) { if (ready) js("pchubFont(${Prefs.terminalFont})") }

        Spacer(Modifier.height(6.dp))

        // ---------- línea de comando (más cómoda que escribir directo en la terminal) ----------
        Row(
            Modifier
                .fillMaxWidth()
                .glass(RoundedCornerShape(22.dp), glow = Neon.Lime)
                .padding(start = 14.dp, end = 4.dp, top = 4.dp, bottom = 4.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Box(Modifier.weight(1f)) {
                if (command.isEmpty()) Text("Escribe un comando…", color = Neon.TextFaint, fontFamily = FontFamily.Monospace)
                BasicTextField(
                    value = command,
                    onValueChange = { command = it },
                    singleLine = true,
                    textStyle = MaterialTheme.typography.bodyMedium.copy(color = Neon.Text, fontFamily = FontFamily.Monospace),
                    cursorBrush = SolidColor(Neon.Lime),
                    keyboardOptions = KeyboardOptions(imeAction = ImeAction.Send, autoCorrectEnabled = false),
                    keyboardActions = KeyboardActions(onSend = {
                        sendInput(command + "\r")
                        command = ""
                    }),
                    modifier = Modifier.fillMaxWidth(),
                )
            }
            NeonIconButton(Icons.Filled.Send, "Enviar", size = 38.dp, accent = Neon.Lime, filled = true) {
                sendInput(command + "\r")
                command = ""
            }
        }
    }
}

@Composable
private fun TermKey(label: String, active: Boolean = false, accent: androidx.compose.ui.graphics.Color = Neon.Cyan, onClick: () -> Unit) {
    val haptic = rememberHaptic()
    Text(
        label,
        color = if (active) Neon.Bg else Neon.Text,
        fontFamily = FontFamily.Monospace,
        style = MaterialTheme.typography.labelLarge,
        modifier = Modifier
            .glass(RoundedCornerShape(10.dp), tint = if (active) accent else null, glow = if (active) accent else null)
            .clickable { haptic(false); onClick() }
            .padding(horizontal = 12.dp, vertical = 9.dp),
    )
}
