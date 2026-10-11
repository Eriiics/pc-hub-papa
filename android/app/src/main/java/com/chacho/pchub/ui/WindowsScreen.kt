package com.chacho.pchub.ui

import androidx.compose.foundation.Image
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Minimize
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.Tab
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.chacho.pchub.net.Pc
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import org.json.JSONObject

private data class PcWindow(val h: Long, val title: String, val proc: String, val icon: String?, val active: Boolean)

private val WindowKeys = listOf(
    QuickKey("Escritorio", "win+d"),
    QuickKey("Vista de tareas", "win+tab"),
    QuickKey("Alt+Tab", "alt+tab"),
    QuickKey("Maximizar", "win+up"),
    QuickKey("Minimizar", "win+down"),
    QuickKey("Ajustar ←", "win+left"),
    QuickKey("Ajustar →", "win+right"),
    QuickKey("Escritorio virtual ←", "ctrl+win+left"),
    QuickKey("Escritorio virtual →", "ctrl+win+right"),
)

@Composable
fun WindowsScreen() {
    val scope = rememberCoroutineScope()
    val haptic = rememberHaptic()
    val ctx by Pc.context.collectAsState()
    var windows by remember { mutableStateOf<List<PcWindow>>(emptyList()) }
    var loading by remember { mutableStateOf(false) }

    fun load() {
        loading = true
        scope.pcCall("win.list") { d ->
            val arr = d.optJSONArray("windows")
            windows = if (arr == null) emptyList() else (0 until arr.length()).map {
                val o = arr.getJSONObject(it)
                PcWindow(o.optLong("h"), o.optString("title"), o.optString("proc"), o.str("icon"), o.optBoolean("active"))
            }
        }.invokeOnCompletion { loading = false }
    }

    fun act(method: String, w: PcWindow, reloadAfterMs: Long = 300) {
        haptic(false)
        scope.launch {
            try {
                Pc.call(method, JSONObject().put("h", w.h))
            } catch (e: Exception) {
                Toaster.show(e.message ?: "Error")
            }
            delay(reloadAfterMs)
            load()
        }
    }

    // Recargar al abrir y cada vez que cambia la ventana activa del PC
    LaunchedEffect(ctx) { load() }

    Column(Modifier.fillMaxSize()) {
        Spacer(Modifier.size(8.dp))
        Box(Modifier.padding(horizontal = 14.dp)) { QuickKeyRow(WindowKeys) }
        Row(Modifier.padding(start = 18.dp, end = 12.dp, top = 10.dp, bottom = 4.dp), verticalAlignment = Alignment.CenterVertically) {
            Text("VENTANAS ABIERTAS · ${windows.size}", style = SectionStyle, modifier = Modifier.weight(1f))
            NeonIconButton(Icons.Filled.Refresh, "Actualizar", size = 38.dp, enabled = !loading) { load() }
        }
        LazyColumn(
            contentPadding = PaddingValues(start = 14.dp, end = 14.dp, top = 4.dp, bottom = 16.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            items(windows, key = { it.h }) { w ->
                val icon = rememberBase64Image(w.icon)
                Row(
                    Modifier
                        .fillMaxWidth()
                        .glass(RoundedCornerShape(18.dp), glow = if (w.active) Neon.Cyan else null, tint = if (w.active) Neon.Cyan else null)
                        .clickable { act("win.focus", w) }
                        .padding(start = 14.dp, top = 10.dp, bottom = 10.dp, end = 8.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    if (icon != null) Image(icon, contentDescription = null, modifier = Modifier.size(32.dp))
                    else Icon(Icons.Filled.Tab, contentDescription = null, tint = Neon.Cyan, modifier = Modifier.size(32.dp))
                    Spacer(Modifier.width(12.dp))
                    Column(Modifier.weight(1f)) {
                        Text(w.title, style = MaterialTheme.typography.bodyMedium, color = Neon.Text, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        Text(w.proc, style = MaterialTheme.typography.labelSmall, color = Neon.TextFaint)
                    }
                    NeonIconButton(Icons.Filled.Minimize, "Minimizar", size = 36.dp, accent = Neon.TextDim) { act("win.minimize", w) }
                    Spacer(Modifier.width(6.dp))
                    NeonIconButton(Icons.Filled.Close, "Cerrar", size = 36.dp, accent = Neon.Red) { act("win.close", w, 700) }
                }
            }
        }
    }
}
