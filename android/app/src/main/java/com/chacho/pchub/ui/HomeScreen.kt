package com.chacho.pchub.ui

import androidx.compose.foundation.Image
import androidx.compose.foundation.background
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
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.ArrowForward
import androidx.compose.material.icons.automirrored.filled.Send
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.AutoAwesome
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Mic
import androidx.compose.material.icons.filled.MusicNote
import androidx.compose.material.icons.filled.Pause
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.SkipNext
import androidx.compose.material.icons.filled.SkipPrevious
import androidx.compose.material.icons.filled.OpenInFull
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.blur
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.chacho.pchub.Prefs
import com.chacho.pchub.net.Pc
import org.json.JSONObject

@Composable
fun HomeScreen(nav: Nav) {
    val scope = rememberCoroutineScope()
    var editing by remember { mutableStateOf(false) }
    var adding by remember { mutableStateOf(false) }
    var shortcuts by remember(Prefs.shortcutsJson) { mutableStateOf(Shortcuts.load()) }

    fun update(list: List<Shortcut>) {
        shortcuts = list
        Shortcuts.save(list)
    }

    Column(
        Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 16.dp)
    ) {
        Spacer(Modifier.height(6.dp))
        AiBar(nav)

        Spacer(Modifier.height(14.dp))
        NowPlayingCard(onOpen = { nav.go(Screen.Media) })

        // ---------- módulos favoritos y más usados ----------
        SectionTitle("Tus módulos", trailing = {
            Text("Mantén para fijar ★", style = MaterialTheme.typography.labelSmall, color = Neon.TextFaint)
        })
        Row(
            Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()),
            horizontalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            topModules().forEach { s ->
                val fav = s.name in Prefs.favorites
                NeonTile(
                    s.title,
                    Modifier.width(92.dp),
                    icon = s.icon,
                    accent = s.accent,
                    highlighted = fav,
                    badge = if (fav) "★" else null,
                    onLongClick = { Prefs.toggleFavorite(s.name) },
                ) { nav.go(s) }
            }
        }

        // ---------- accesos editables ----------
        SectionTitle("Accesos", trailing = {
            TextButton(onClick = { editing = !editing }) {
                Text(if (editing) "Listo" else "Editar", color = Neon.Cyan)
            }
        })
        val tiles = shortcuts.size + 1
        val columns = 4
        (0 until (tiles + columns - 1) / columns).forEach { row ->
            Row(Modifier.fillMaxWidth().padding(bottom = 10.dp), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                for (col in 0 until columns) {
                    val i = row * columns + col
                    Box(Modifier.weight(1f)) {
                        when {
                            i < shortcuts.size -> {
                                val s = shortcuts[i]
                                ShortcutTile(s, onLongClick = { editing = true }) {
                                    if (!editing) Shortcuts.run(s, scope, nav)
                                }
                                if (editing) EditOverlay(
                                    canLeft = i > 0,
                                    canRight = i < shortcuts.size - 1,
                                    onLeft = { update(shortcuts.toMutableList().apply { add(i - 1, removeAt(i)) }) },
                                    onRight = { update(shortcuts.toMutableList().apply { add(i + 1, removeAt(i)) }) },
                                    onRemove = { update(shortcuts.toMutableList().apply { removeAt(i) }) },
                                )
                            }
                            i == shortcuts.size -> NeonTile("Agregar", icon = Icons.Filled.Add, accent = Neon.TextDim) { adding = true }
                        }
                    }
                }
            }
        }

        // ---------- atajos ----------
        SectionTitle("Teclas rápidas")
        QuickKeyRow()
        Spacer(Modifier.height(24.dp))
    }

    if (adding) AddShortcutSheet(onDismiss = { adding = false }) { s ->
        update(shortcuts + s)
        adding = false
    }
}

/** Favoritos primero, luego los más usados; si no hay historial, una selección por defecto. */
@Composable
private fun topModules(): List<Screen> {
    val favs = ModuleScreens.filter { it.name in Prefs.favorites }
    val used = ModuleScreens
        .filter { it.name !in Prefs.favorites && (Prefs.usage[it.name] ?: 0) > 0 }
        .sortedByDescending { Prefs.usage[it.name] ?: 0 }
    val defaults = listOf(Screen.Touchpad, Screen.Pointer, Screen.Media, Screen.YouTube, Screen.Windows, Screen.Ai)
    return (favs + used + defaults).distinct().take(8)
}

@Composable
private fun EditOverlay(canLeft: Boolean, canRight: Boolean, onLeft: () -> Unit, onRight: () -> Unit, onRemove: () -> Unit) {
    Box(Modifier.fillMaxWidth()) {
        NeonIconButton(Icons.Filled.Close, "Quitar", Modifier.align(Alignment.TopEnd), size = 26.dp, accent = Neon.Red, onClick = onRemove)
        Row(Modifier.align(Alignment.TopStart)) {
            if (canLeft) NeonIconButton(Icons.AutoMirrored.Filled.ArrowBack, "Mover", size = 26.dp, accent = Neon.TextDim, onClick = onLeft)
            if (canRight) NeonIconButton(Icons.AutoMirrored.Filled.ArrowForward, "Mover", size = 26.dp, accent = Neon.TextDim, onClick = onRight)
        }
    }
}

/** Barra para pedirle cosas a la IA, con micrófono. */
@Composable
private fun AiBar(nav: Nav) {
    var prompt by remember { mutableStateOf("") }

    fun ask(text: String) {
        if (text.isBlank()) return
        AiChat.ask(text.trim())
        prompt = ""
        nav.go(Screen.Ai)
    }

    val voice = rememberVoiceInput { ask(it) }

    Row(
        Modifier
            .fillMaxWidth()
            .glass(RoundedCornerShape(28.dp), glow = Neon.Violet, strong = true)
            .padding(start = 16.dp, end = 6.dp, top = 6.dp, bottom = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Icon(Icons.Filled.AutoAwesome, contentDescription = null, tint = Neon.Violet, modifier = Modifier.size(22.dp))
        Spacer(Modifier.width(12.dp))
        Box(Modifier.weight(1f)) {
            if (prompt.isEmpty()) Text("Pídele algo a tu PC…", color = Neon.TextFaint)
            BasicTextField(
                value = prompt,
                onValueChange = { prompt = it },
                singleLine = true,
                textStyle = MaterialTheme.typography.bodyLarge.copy(color = Neon.Text),
                cursorBrush = SolidColor(Neon.Cyan),
                keyboardOptions = KeyboardOptions(imeAction = ImeAction.Send),
                keyboardActions = KeyboardActions(onSend = { ask(prompt) }),
                modifier = Modifier.fillMaxWidth(),
            )
        }
        if (prompt.isBlank()) NeonIconButton(Icons.Filled.Mic, "Hablar", size = 46.dp, accent = Neon.Violet, filled = true, onClick = voice)
        else NeonIconButton(Icons.AutoMirrored.Filled.Send, "Enviar", size = 46.dp, accent = Neon.Cyan, filled = true) { ask(prompt) }
    }
}

/** Tarjeta "ahora suena" con la carátula difuminada de fondo. */
@Composable
fun NowPlayingCard(onOpen: () -> Unit) {
    val media by Pc.media.collectAsState()
    val ctx by Pc.context.collectAsState()
    val m = media
    val title = m?.str("title")
    val playing = m?.optString("status") == "playing"
    val art = rememberBase64Image(m?.str("art"))
    val shape = RoundedCornerShape(26.dp)

    Box(
        Modifier
            .fillMaxWidth()
            .glass(shape, glow = if (playing) Neon.Magenta else null)
    ) {
        if (art != null) {
            Image(
                art,
                contentDescription = null,
                contentScale = ContentScale.Crop,
                modifier = Modifier.matchParentSize().blur(36.dp),
            )
            Box(Modifier.matchParentSize().background(Brush.horizontalGradient(listOf(Neon.Bg.copy(alpha = 0.55f), Neon.Bg.copy(alpha = 0.80f)))))
        }
        Column(Modifier.padding(14.dp)) {
            Text(
                "EN EL PC · " + (ctx?.str("title") ?: "—"),
                style = MaterialTheme.typography.labelSmall,
                color = Neon.TextDim,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Spacer(Modifier.height(10.dp))
            Row(verticalAlignment = Alignment.CenterVertically) {
                Box(
                    Modifier
                        .size(64.dp)
                        .clip(RoundedCornerShape(16.dp))
                        .background(Color.White.copy(alpha = 0.06f)),
                    contentAlignment = Alignment.Center,
                ) {
                    if (art != null) Image(art, contentDescription = null, contentScale = ContentScale.Crop, modifier = Modifier.fillMaxSize())
                    else Icon(Icons.Filled.MusicNote, contentDescription = null, tint = Neon.Magenta, modifier = Modifier.size(30.dp))
                }
                Spacer(Modifier.width(14.dp))
                Column(Modifier.weight(1f)) {
                    Text(
                        title ?: "No hay nada sonando",
                        style = MaterialTheme.typography.titleMedium,
                        color = Neon.Text,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                    Text(
                        m?.str("artist") ?: "Toca para abrir el reproductor",
                        style = MaterialTheme.typography.bodySmall,
                        color = Neon.TextDim,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
            }
            Spacer(Modifier.height(12.dp))
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceEvenly, verticalAlignment = Alignment.CenterVertically) {
                NeonIconButton(Icons.Filled.SkipPrevious, "Anterior", size = 44.dp, accent = Neon.Text) {
                    Pc.send("media.cmd", JSONObject().put("a", "prev"))
                }
                NeonIconButton(
                    if (playing) Icons.Filled.Pause else Icons.Filled.PlayArrow,
                    "Play/pausa",
                    size = 56.dp,
                    accent = Neon.Magenta,
                    filled = true,
                ) { Pc.send("media.cmd", JSONObject().put("a", "playpause")) }
                NeonIconButton(Icons.Filled.SkipNext, "Siguiente", size = 44.dp, accent = Neon.Text) {
                    Pc.send("media.cmd", JSONObject().put("a", "next"))
                }
                NeonIconButton(Icons.Filled.OpenInFull, "Abrir reproductor", size = 44.dp, accent = Neon.Violet, onClick = onOpen)
            }
        }
    }
}
