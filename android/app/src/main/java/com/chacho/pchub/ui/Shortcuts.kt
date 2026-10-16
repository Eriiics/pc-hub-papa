package com.chacho.pchub.ui

import androidx.compose.foundation.Image
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.chacho.pchub.Prefs
import com.chacho.pchub.net.Pc
import kotlinx.coroutines.CoroutineScope
import org.json.JSONArray
import org.json.JSONObject
import java.text.Normalizer

/**
 * Acceso del inicio.
 *  type = "cmd"    → comando personalizado del PC (value = id)
 *  type = "app"    → aplicación del PC (value = id)
 *  type = "key"    → combinación de teclas (value = "ctrl+shift+esc")
 *  type = "module" → pantalla de la app (value = Screen.name)
 * icon = emoji, o "b64:<png>" para el ícono de una app del PC.
 */
data class Shortcut(val type: String, val value: String, val label: String, val icon: String) {
    fun toJson(): JSONObject = JSONObject().put("type", type).put("value", value).put("label", label).put("icon", icon)

    companion object {
        fun fromJson(o: JSONObject) = Shortcut(o.optString("type"), o.optString("value"), o.optString("label"), o.optString("icon"))
    }
}

object Shortcuts {
    private val Defaults = listOf(
        Shortcut("key", "win+d", "Escritorio", "🖥️"),
        Shortcut("key", "win+shift+s", "Captura", "✂️"),
        Shortcut("key", "ctrl+shift+esc", "Tareas", "📊"),
        Shortcut("module", Screen.YouTube.name, "YouTube", "▶️"),
        Shortcut("key", "win+e", "Explorador", "📁"),
        Shortcut("module", Screen.Excel.name, "Excel", "📊"),
    )

    fun load(): List<Shortcut> {
        val json = Prefs.shortcutsJson
        if (json.isBlank()) return Defaults
        return try {
            val arr = JSONArray(json)
            (0 until arr.length()).map { Shortcut.fromJson(arr.getJSONObject(it)) }
        } catch (_: Exception) {
            Defaults
        }
    }

    fun save(list: List<Shortcut>) = Prefs.saveShortcuts(JSONArray().apply { list.forEach { put(it.toJson()) } })

    fun run(s: Shortcut, scope: CoroutineScope, nav: Nav) {
        when (s.type) {
            "cmd" -> scope.pcCall("cmd.run", JSONObject().put("id", s.value), timeoutMs = 70_000) { d ->
                val out = d.optString("output")
                Toaster.show(if (out.isNotBlank()) out.take(300) else "✓ ${s.label}")
            }
            "app" -> {
                Toaster.show("Abriendo ${s.label}…")
                scope.pcCall("apps.launch", JSONObject().put("id", s.value))
            }
            "key" -> pressShortcutKey(s.value)
            "module" -> runCatching { Screen.valueOf(s.value) }.getOrNull()?.let { nav.go(it) }
        }
    }
}

/** Muestra el ícono de un acceso: emoji o imagen base64. */
@Composable
fun ShortcutTile(s: Shortcut, modifier: Modifier = Modifier, onLongClick: (() -> Unit)? = null, onClick: () -> Unit) {
    val img = if (s.icon.startsWith("b64:")) rememberBase64Image(s.icon.removePrefix("b64:")) else null
    val accent = when (s.type) {
        "cmd" -> Neon.Violet
        "app" -> Neon.Lime
        "module" -> runCatching { Screen.valueOf(s.value).accent }.getOrDefault(Neon.Cyan)
        else -> Neon.Cyan
    }
    NeonTile(
        s.label,
        modifier,
        emoji = if (img == null) s.icon.ifEmpty { "⚡" } else null,
        image = img,
        accent = accent,
        onLongClick = onLongClick,
        onClick = onClick,
    )
}

private fun norm(s: String) = Normalizer.normalize(s.lowercase(), Normalizer.Form.NFD).replace(Regex("\\p{Mn}+"), "")

/** Hoja para agregar un acceso: comandos del PC, apps, teclas o módulos. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AddShortcutSheet(onDismiss: () -> Unit, onAdd: (Shortcut) -> Unit) {
    val scope = rememberCoroutineScope()
    val sheet = rememberModalBottomSheetState(skipPartiallyExpanded = true)
    var tab by remember { mutableIntStateOf(0) }
    var commands by remember { mutableStateOf<List<Shortcut>?>(null) }
    var apps by remember { mutableStateOf<List<Shortcut>?>(null) }
    var query by remember { mutableStateOf("") }
    var customCombo by remember { mutableStateOf("") }

    LaunchedEffect(tab) {
        if (tab == 0 && commands == null) scope.pcCall("cmd.list") { d ->
            val arr = d.optJSONArray("commands")
            commands = if (arr == null) emptyList() else (0 until arr.length()).map {
                val o = arr.getJSONObject(it)
                Shortcut("cmd", o.optString("id"), o.optString("name"), o.optString("icon", "⚡"))
            }
        }
        if (tab == 1 && apps == null) scope.pcCall("apps.list", timeoutMs = 30_000) { d ->
            val arr = d.optJSONArray("apps")
            apps = if (arr == null) emptyList() else (0 until arr.length()).map {
                val o = arr.getJSONObject(it)
                val icon = o.str("icon")
                Shortcut("app", o.optString("id"), o.optString("name"), if (icon != null) "b64:$icon" else "🗔")
            }
        }
    }

    val keyPresets = listOf(
        Shortcut("key", "win+d", "Escritorio", "🖥️"),
        Shortcut("key", "win+tab", "Vista de tareas", "🗂️"),
        Shortcut("key", "win+shift+s", "Captura", "✂️"),
        Shortcut("key", "ctrl+shift+esc", "Administrador de tareas", "📊"),
        Shortcut("key", "win+e", "Explorador", "📁"),
        Shortcut("key", "win+l", "Bloquear", "🔒"),
        Shortcut("key", "win+v", "Historial del portapapeles", "📋"),
        Shortcut("key", "win+g", "Xbox Game Bar", "🎮"),
        Shortcut("key", "win+alt+r", "Grabar (Game Bar)", "⏺️"),
        Shortcut("key", "alt+f4", "Cerrar ventana", "❌"),
        Shortcut("key", "f11", "Pantalla completa", "⛶"),
        Shortcut("key", "volmute", "Silenciar", "🔇"),
    )
    val moduleItems = ModuleScreens.map { Shortcut("module", it.name, it.title, moduleEmoji(it)) }

    ModalBottomSheet(onDismissRequest = onDismiss, sheetState = sheet, containerColor = Neon.Panel) {
        Column(Modifier.padding(horizontal = 16.dp).navigationBarsPadding()) {
            Text("Agregar acceso", style = MaterialTheme.typography.titleLarge, color = Neon.Text)
            Spacer(Modifier.size(12.dp))
            SegmentedTabs(listOf("Comandos", "Apps", "Teclas", "Módulos"), tab) { tab = it; query = "" }
            Spacer(Modifier.size(12.dp))

            if (tab == 1) {
                OutlinedTextField(
                    value = query,
                    onValueChange = { query = it },
                    placeholder = { Text("Buscar app o juego") },
                    leadingIcon = { Icon(Icons.Filled.Search, contentDescription = null) },
                    singleLine = true,
                    shape = RoundedCornerShape(18.dp),
                    modifier = Modifier.fillMaxWidth(),
                )
                Spacer(Modifier.size(8.dp))
            }
            if (tab == 2) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    OutlinedTextField(
                        value = customCombo,
                        onValueChange = { customCombo = it.lowercase().replace(" ", "") },
                        placeholder = { Text("Otra: ej. ctrl+alt+t") },
                        singleLine = true,
                        shape = RoundedCornerShape(18.dp),
                        modifier = Modifier.weight(1f),
                    )
                    Spacer(Modifier.width(8.dp))
                    NeonButton("Agregar", enabled = customCombo.isNotBlank()) {
                        onAdd(Shortcut("key", customCombo, customCombo, "⌨️"))
                    }
                }
                Spacer(Modifier.size(8.dp))
            }

            val items: List<Shortcut>? = when (tab) {
                0 -> commands
                1 -> apps?.filter { query.isBlank() || norm(it.label).contains(norm(query)) }
                2 -> keyPresets
                else -> moduleItems
            }
            when {
                items == null -> Text("Cargando…", color = Neon.TextDim, modifier = Modifier.padding(16.dp))
                items.isEmpty() -> Text(
                    if (tab == 0) "No hay comandos. Se agregan en config.json del PC (sección \"commands\")." else "Nada por aquí.",
                    color = Neon.TextDim,
                    modifier = Modifier.padding(16.dp),
                )
                else -> LazyColumn(Modifier.heightIn(max = 460.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    items(items) { s ->
                        val img = if (s.icon.startsWith("b64:")) rememberBase64Image(s.icon.removePrefix("b64:")) else null
                        Row(
                            Modifier
                                .fillMaxWidth()
                                .glass(RoundedCornerShape(16.dp))
                                .clickable { onAdd(s) }
                                .padding(horizontal = 14.dp, vertical = 12.dp),
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            Box(Modifier.size(30.dp), contentAlignment = Alignment.Center) {
                                if (img != null) Image(img, contentDescription = null, modifier = Modifier.size(26.dp))
                                else Text(s.icon, fontSize = 20.sp)
                            }
                            Spacer(Modifier.width(12.dp))
                            Column(Modifier.weight(1f)) {
                                Text(s.label, color = Neon.Text, maxLines = 1, overflow = TextOverflow.Ellipsis)
                                if (s.type == "key") Text(s.value, color = Neon.TextFaint, style = MaterialTheme.typography.labelSmall)
                            }
                        }
                    }
                }
            }
            Spacer(Modifier.size(16.dp))
        }
    }
}

fun moduleEmoji(s: Screen): String = when (s) {
    Screen.Touchpad -> "🖱️"
    Screen.Pointer -> "🎮"
    Screen.Media -> "🎵"
    Screen.YouTube -> "▶️"
    Screen.Ai -> "✨"
    Screen.Windows -> "🗂️"
    Screen.Apps -> "📦"
    Screen.System -> "⚡"
    Screen.Excel -> "📊"
    Screen.Mejoras -> "🛠️"
    Screen.Terminal -> "💻"
    Screen.Settings -> "⚙️"
    else -> "•"
}
