package com.chacho.pchub.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Mic
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.chacho.pchub.net.Pc
import kotlinx.coroutines.launch
import org.json.JSONObject

private data class YtResult(val id: String, val title: String, val channel: String, val duration: String, val views: String, val thumb: String)

/** Resultados en memoria para no perderlos al cambiar de pestaña. */
private object YtCache {
    var music = false
    var query = ""
    var results: List<YtResult> = emptyList()
}

/**
 * Buscar en YouTube o YouTube Music y reproducir en la pestaña de YouTube que está abierta en el PC.
 * La búsqueda la hace el PC (sin API key); el teléfono solo muestra los resultados.
 */
@Composable
fun YouTubeScreen() {
    val scope = rememberCoroutineScope()
    val keyboard = LocalSoftwareKeyboardController.current
    var music by rememberSaveable { mutableStateOf(YtCache.music) }
    var query by rememberSaveable { mutableStateOf(YtCache.query) }
    var results by remember { mutableStateOf(YtCache.results) }
    var loading by remember { mutableStateOf(false) }
    var playing by remember { mutableStateOf<String?>(null) }
    var searchedFor by remember { mutableIntStateOf(0) }

    fun search(q: String = query) {
        if (q.isBlank()) return
        query = q
        keyboard?.hide()
        loading = true
        scope.launch {
            try {
                val d = Pc.call("yt.search", JSONObject().put("q", q).put("music", music), timeoutMs = 25_000)
                val arr = d.optJSONArray("results")
                results = if (arr == null) emptyList() else (0 until arr.length()).map {
                    val o = arr.getJSONObject(it)
                    YtResult(o.optString("id"), o.optString("title"), o.optString("channel"), o.optString("duration"), o.optString("views"), o.optString("thumb"))
                }.distinctBy { it.id }
                YtCache.results = results
                YtCache.query = q
                YtCache.music = music
                searchedFor++
            } catch (e: Exception) {
                Toaster.show(e.message ?: "Error")
            } finally {
                loading = false
            }
        }
    }

    fun play(r: YtResult) {
        playing = r.id
        scope.pcCall("yt.play", JSONObject().put("id", r.id).put("music", music), timeoutMs = 15_000) { d ->
            Toaster.show(d.optString("message", "Reproduciendo"))
        }
    }

    val voice = rememberVoiceInput("¿Qué quieres escuchar?") { search(it) }
    val accent = if (music) Neon.Magenta else Neon.Red

    Column(Modifier.fillMaxSize().padding(horizontal = 16.dp)) {
        Spacer(Modifier.height(8.dp))
        SegmentedTabs(listOf("YouTube", "YouTube Music"), if (music) 1 else 0) {
            val m = it == 1
            if (m != music) {
                music = m
                if (query.isNotBlank()) search()
            }
        }
        Spacer(Modifier.height(10.dp))

        // ---------- barra de búsqueda ----------
        Row(
            Modifier
                .fillMaxWidth()
                .glass(RoundedCornerShape(26.dp), glow = accent, strong = true)
                .padding(start = 16.dp, end = 6.dp, top = 6.dp, bottom = 6.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Icon(Icons.Filled.Search, contentDescription = null, tint = accent, modifier = Modifier.size(22.dp))
            Spacer(Modifier.width(10.dp))
            Box(Modifier.weight(1f)) {
                if (query.isEmpty()) Text(if (music) "Canción, artista…" else "Buscar en YouTube", color = Neon.TextFaint)
                BasicTextField(
                    value = query,
                    onValueChange = { query = it },
                    singleLine = true,
                    textStyle = MaterialTheme.typography.bodyLarge.copy(color = Neon.Text),
                    cursorBrush = SolidColor(accent),
                    keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
                    keyboardActions = KeyboardActions(onSearch = { search() }),
                    modifier = Modifier.fillMaxWidth(),
                )
            }
            NeonIconButton(Icons.Filled.Mic, "Buscar por voz", size = 44.dp, accent = accent, filled = true, onClick = voice)
        }

        Spacer(Modifier.height(10.dp))

        Box(Modifier.weight(1f).fillMaxWidth()) {
            when {
                loading -> CircularProgressIndicator(color = accent, modifier = Modifier.align(Alignment.Center))
                results.isEmpty() -> Text(
                    if (searchedFor > 0) "Sin resultados." else
                        "Busca un video o canción y tócalo: se abre en la pestaña de ${if (music) "YouTube Music" else "YouTube"} que tengas abierta en el PC (o en una nueva).",
                    color = Neon.TextDim,
                    style = MaterialTheme.typography.bodyMedium,
                    modifier = Modifier.align(Alignment.Center).padding(24.dp),
                )
                else -> LazyColumn(
                    contentPadding = PaddingValues(bottom = 16.dp),
                    verticalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    items(results, key = { it.id }) { r ->
                        val active = r.id == playing
                        Row(
                            Modifier
                                .fillMaxWidth()
                                .glass(RoundedCornerShape(18.dp), glow = if (active) accent else null, tint = if (active) accent else null)
                                .clickable { play(r) }
                                .padding(8.dp),
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            Box {
                                NetImage(
                                    r.thumb,
                                    Modifier
                                        .width(if (music) 64.dp else 128.dp)
                                        .aspectRatio(if (music) 1f else 16f / 9f)
                                        .clip(RoundedCornerShape(12.dp)),
                                )
                                if (r.duration.isNotEmpty()) Text(
                                    r.duration,
                                    style = MaterialTheme.typography.labelSmall,
                                    color = Color.White,
                                    modifier = Modifier
                                        .align(Alignment.BottomEnd)
                                        .padding(4.dp)
                                        .clip(RoundedCornerShape(6.dp))
                                        .background(Color.Black.copy(alpha = 0.7f))
                                        .padding(horizontal = 4.dp, vertical = 1.dp),
                                )
                            }
                            Spacer(Modifier.width(12.dp))
                            Column(Modifier.weight(1f)) {
                                Text(r.title, color = Neon.Text, style = MaterialTheme.typography.bodyMedium, maxLines = 2, overflow = TextOverflow.Ellipsis)
                                Text(
                                    listOf(r.channel, r.views).filter { it.isNotBlank() }.joinToString(" · "),
                                    color = Neon.TextDim,
                                    style = MaterialTheme.typography.labelSmall,
                                    maxLines = 1,
                                    overflow = TextOverflow.Ellipsis,
                                )
                            }
                            if (active) Icon(Icons.Filled.PlayArrow, contentDescription = null, tint = accent, modifier = Modifier.size(22.dp))
                        }
                    }
                }
            }
        }
    }
}
