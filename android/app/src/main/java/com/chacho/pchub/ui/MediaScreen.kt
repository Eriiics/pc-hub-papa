package com.chacho.pchub.ui

import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.VolumeOff
import androidx.compose.material.icons.automirrored.filled.VolumeUp
import androidx.compose.material.icons.filled.MusicNote
import androidx.compose.material.icons.filled.Pause
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.SkipNext
import androidx.compose.material.icons.filled.SkipPrevious
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.blur
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.chacho.pchub.net.Pc
import kotlinx.coroutines.delay
import org.json.JSONObject

private data class AppVolume(val pid: Long, val name: String, val level: Float, val muted: Boolean)

@Composable
private fun neonSlider(accent: androidx.compose.ui.graphics.Color) = SliderDefaults.colors(
    thumbColor = accent,
    activeTrackColor = accent,
    inactiveTrackColor = Neon.Text.copy(alpha = 0.12f),
)

@Composable
fun MediaScreen() {
    val scope = rememberCoroutineScope()
    val haptic = rememberHaptic()
    val media by Pc.media.collectAsState()

    var volume by remember { mutableFloatStateOf(0.5f) }
    var muted by remember { mutableStateOf(false) }
    var device by remember { mutableStateOf("") }
    var lastVolSend by remember { mutableLongStateOf(0L) }
    var appVolumes by remember { mutableStateOf<List<AppVolume>?>(null) }

    // Posición interpolada entre actualizaciones del PC
    val receivedAt = remember(media) { System.currentTimeMillis() }
    var now by remember { mutableLongStateOf(System.currentTimeMillis()) }
    var seeking by remember { mutableStateOf<Float?>(null) }

    fun loadVolume() = scope.pcCall("vol.get") { d ->
        volume = d.optDouble("level", 0.5).toFloat()
        muted = d.optBoolean("muted")
        device = d.optString("device")
    }

    fun loadApps() = scope.pcCall("vol.apps") { d ->
        val arr = d.optJSONArray("apps")
        appVolumes = if (arr == null) emptyList() else (0 until arr.length()).map {
            val o = arr.getJSONObject(it)
            AppVolume(o.optLong("pid"), o.optString("name"), o.optDouble("level", 1.0).toFloat(), o.optBoolean("muted"))
        }
    }

    fun cmd(a: String) {
        haptic(false)
        Pc.send("media.cmd", JSONObject().put("a", a))
    }

    LaunchedEffect(Unit) {
        scope.pcCall("media.get") { Pc.media.value = it }
        loadVolume()
    }
    LaunchedEffect(Unit) {
        while (true) {
            now = System.currentTimeMillis()
            delay(500)
        }
    }

    val m = media
    val status = m?.optString("status") ?: "none"
    val playing = status == "playing"
    val duration = (m?.optDouble("duration", 0.0) ?: 0.0).toFloat()
    val basePos = (m?.optDouble("position", 0.0) ?: 0.0).toFloat()
    val position = if (playing) (basePos + (now - receivedAt) / 1000f).coerceAtMost(duration.coerceAtLeast(basePos)) else basePos
    val art = rememberBase64Image(m?.str("art"))

    Box(Modifier.fillMaxSize()) {
        // Carátula difuminada de fondo
        if (art != null) {
            Image(
                art,
                contentDescription = null,
                contentScale = ContentScale.Crop,
                modifier = Modifier.fillMaxSize().blur(60.dp),
            )
            Box(Modifier.fillMaxSize().background(Brush.verticalGradient(listOf(Neon.Bg.copy(alpha = 0.55f), Neon.Bg.copy(alpha = 0.92f)))))
        }

        Column(
            Modifier
                .fillMaxSize()
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 20.dp, vertical = 12.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            // Carátula
            Box(
                Modifier
                    .widthIn(max = 320.dp)
                    .fillMaxWidth()
                    .aspectRatio(1f)
                    .neonShadow(if (playing) 40.dp else 16.dp, RoundedCornerShape(30.dp), Neon.Magenta)
                    .glass(RoundedCornerShape(30.dp)),
                contentAlignment = Alignment.Center,
            ) {
                if (art != null) {
                    Image(art, contentDescription = "Carátula", contentScale = ContentScale.Crop, modifier = Modifier.fillMaxSize().clip(RoundedCornerShape(30.dp)))
                } else {
                    Icon(Icons.Filled.MusicNote, contentDescription = null, modifier = Modifier.size(96.dp), tint = Neon.Magenta.copy(alpha = 0.6f))
                }
            }

            Spacer(Modifier.height(22.dp))
            Text(
                m?.str("title") ?: if (status == "none") "No hay nada sonando" else "—",
                style = MaterialTheme.typography.headlineSmall,
                fontWeight = FontWeight.Bold,
                color = Neon.Text,
                textAlign = TextAlign.Center,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                listOfNotNull(m?.str("artist"), m?.str("album")).joinToString(" · "),
                style = MaterialTheme.typography.bodyMedium,
                color = Neon.TextDim,
                textAlign = TextAlign.Center,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )

            // Progreso
            if (duration > 0) {
                Spacer(Modifier.height(10.dp))
                Slider(
                    value = (seeking ?: position).coerceIn(0f, duration),
                    onValueChange = { seeking = it },
                    onValueChangeFinished = {
                        seeking?.let { Pc.send("media.cmd", JSONObject().put("a", "seek").put("pos", it.toDouble())) }
                        seeking = null
                    },
                    valueRange = 0f..duration,
                    colors = neonSlider(Neon.Magenta),
                    modifier = Modifier.fillMaxWidth(),
                )
                Row(Modifier.fillMaxWidth()) {
                    Text(fmtTime(seeking ?: position), style = MaterialTheme.typography.labelSmall, color = Neon.TextDim, modifier = Modifier.weight(1f))
                    Text(fmtTime(duration), style = MaterialTheme.typography.labelSmall, color = Neon.TextDim)
                }
            }

            // Controles
            Spacer(Modifier.height(10.dp))
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(26.dp)) {
                NeonIconButton(Icons.Filled.SkipPrevious, "Anterior", size = 62.dp, accent = Neon.Text) { cmd("prev") }
                NeonIconButton(
                    if (playing) Icons.Filled.Pause else Icons.Filled.PlayArrow,
                    "Play/pausa",
                    size = 84.dp,
                    accent = Neon.Magenta,
                    filled = true,
                ) { cmd("playpause") }
                NeonIconButton(Icons.Filled.SkipNext, "Siguiente", size = 62.dp, accent = Neon.Text) { cmd("next") }
            }

            // Volumen general
            Spacer(Modifier.height(22.dp))
            GlassCard(Modifier.fillMaxWidth(), padding = 12.dp) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    NeonIconButton(
                        if (muted) Icons.AutoMirrored.Filled.VolumeOff else Icons.AutoMirrored.Filled.VolumeUp,
                        "Silenciar",
                        size = 40.dp,
                        accent = if (muted) Neon.Red else Neon.Cyan,
                    ) {
                        muted = !muted
                        Pc.send("vol.mute", JSONObject().put("muted", muted))
                    }
                    Slider(
                        value = volume,
                        onValueChange = {
                            volume = it
                            val t = System.currentTimeMillis()
                            if (t - lastVolSend > 70) {
                                lastVolSend = t
                                Pc.send("vol.set", JSONObject().put("level", it.toDouble()))
                            }
                        },
                        onValueChangeFinished = { Pc.send("vol.set", JSONObject().put("level", volume.toDouble())) },
                        colors = neonSlider(Neon.Cyan),
                        modifier = Modifier.weight(1f).padding(horizontal = 8.dp),
                    )
                    Text("${(volume * 100).toInt()}%", style = MaterialTheme.typography.labelLarge, color = Neon.Text)
                }
                if (device.isNotEmpty()) {
                    Text(device, style = MaterialTheme.typography.labelSmall, color = Neon.TextFaint, modifier = Modifier.padding(start = 4.dp, top = 2.dp))
                }
            }

            // Volumen por aplicación
            Spacer(Modifier.height(6.dp))
            TextButton(onClick = { if (appVolumes == null) loadApps() else appVolumes = null }) {
                Text(if (appVolumes == null) "Volumen por aplicación" else "Ocultar volumen por aplicación", color = Neon.Cyan)
            }
            appVolumes?.let { list ->
                GlassCard(Modifier.fillMaxWidth(), padding = 12.dp) {
                    list.forEach { app ->
                        var level by remember(app.pid) { mutableFloatStateOf(app.level) }
                        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                            Text(app.name, style = MaterialTheme.typography.bodySmall, color = Neon.Text, maxLines = 1, overflow = TextOverflow.Ellipsis,
                                modifier = Modifier.weight(0.35f))
                            Slider(
                                value = level,
                                onValueChange = { level = it },
                                onValueChangeFinished = {
                                    Pc.send("vol.app.set", JSONObject().put("pid", app.pid).put("level", level.toDouble()))
                                },
                                colors = neonSlider(Neon.Violet),
                                modifier = Modifier.weight(0.65f),
                            )
                        }
                    }
                }
            }
            Spacer(Modifier.height(24.dp))
        }
    }
}

private fun fmtTime(seconds: Float): String {
    val s = seconds.toInt().coerceAtLeast(0)
    return if (s >= 3600) "%d:%02d:%02d".format(s / 3600, (s % 3600) / 60, s % 60) else "%d:%02d".format(s / 60, s % 60)
}
