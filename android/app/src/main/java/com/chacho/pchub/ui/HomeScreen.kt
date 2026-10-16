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
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.ui.platform.LocalContext
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
import com.chacho.pchub.net.AppUpdater
import com.chacho.pchub.net.Pc
import com.chacho.pchub.net.WirelessDebug
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import org.json.JSONObject

/** Lo que más se usa, a la vista y en botones grandes. */
private val MainScreens = listOf(Screen.Excel, Screen.Touchpad, Screen.Media, Screen.Windows, Screen.Apps, Screen.System)

@Composable
fun HomeScreen(nav: Nav) {
    Column(
        Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 16.dp)
    ) {
        Spacer(Modifier.height(6.dp))
        VersionNotice()
        AiBar(nav)

        Spacer(Modifier.height(14.dp))
        NowPlayingCard(onOpen = { nav.go(Screen.Media) })

        SectionTitle("¿Qué quieres hacer?")
        MainScreens.chunked(2).forEach { pair ->
            Row(Modifier.fillMaxWidth().padding(bottom = 10.dp), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                pair.forEach { s ->
                    NeonTile(s.title, Modifier.weight(1f), icon = s.icon, accent = s.accent, highlighted = true, aspect = 1.45f) { nav.go(s) }
                }
            }
        }
        GlassCard(Modifier.fillMaxWidth().padding(bottom = 10.dp), glow = Neon.Violet, onClick = { nav.go(Screen.Mejoras) }) {
            Text("¿Quieres cambiar algo de PC Hub?", style = MaterialTheme.typography.titleSmall, color = Neon.Text)
            Text("Pídeselo a tu Claude: anota lo que quieres y él lo programa e instala.", style = MaterialTheme.typography.bodySmall, color = Neon.TextDim)
        }
        Spacer(Modifier.height(24.dp))
    }
}

/** Aviso amable si el programa del PC y la app del teléfono tienen versiones distintas. */
@Composable
private fun VersionNotice() {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    var message by remember { mutableStateOf<String?>(null) }
    var phoneIsOlder by remember { mutableStateOf(false) }
    var working by remember { mutableStateOf(false) }
    var problem by remember { mutableStateOf<String?>(null) }
    var agentVersion by remember { mutableStateOf("") }

    // Actualiza por Wi-Fi con el PC (adb, sin tocar nada); si no se puede, descarga la app y Android pide «Instalar»
    fun actualizar(porWifi: Boolean) {
        problem = null
        working = true
        scope.launch {
            try {
                if (porWifi) {
                    val (ok, msg) = WirelessDebug.update(context)
                    if (!ok) { problem = msg; working = false; return@launch }
                } else if (!AppUpdater.canInstall(context)) {
                    problem = "Android necesita que le des permiso a PC Hub para instalar. Actívalo en la pantalla que se abre y vuelve a tocar el botón."
                    AppUpdater.askPermission(context)
                } else {
                    AppUpdater.downloadAndInstall(context)
                }
            } catch (e: Exception) {
                problem = e.message ?: "No se pudo actualizar."
            }
            working = false
        }
    }

    val status by Pc.status.collectAsState()
    // Se vuelve a revisar al conectarse (por ejemplo después de actualizar el PC) y cada 20 segundos
    LaunchedEffect(status) {
        if (status != Pc.Status.Connected) return@LaunchedEffect
        while (true) {
            try {
                val info = Pc.call("app.info")
                val agent = info.optString("number")
                val app = context.packageManager.getPackageInfo(context.packageName, 0).versionName.orEmpty()
                fun parts(v: String) = v.split('.').map { it.toIntOrNull() ?: 0 }
                fun cmp(a: String, b: String): Int {
                    val x = parts(a); val y = parts(b)
                    for (i in 0 until maxOf(x.size, y.size)) {
                        val d = x.getOrElse(i) { 0 }.compareTo(y.getOrElse(i) { 0 })
                        if (d != 0) return d
                    }
                    return 0
                }
                val c = if (agent.isEmpty()) -1 else cmp(agent, app)
                phoneIsOlder = c > 0
                agentVersion = agent
                message = when {
                    c > 0 -> "Hay una versión nueva de PC Hub: tu teléfono tiene la $app y el PC ya tiene la $agent."
                    c < 0 -> "El teléfono tiene la versión $app, pero el programa del PC es más antiguo (${agent.ifEmpty { "desconocida" }}). " +
                        "En el PC abre el PcHub.Agent.exe de la carpeta nueva y elige «Sí» para actualizarlo."
                    else -> null
                }
            } catch (_: Exception) {
                // Sin conexión: se conserva lo último que se supo
            }
            // Con el teléfono ya vinculado al PC, se actualiza sola una vez por versión nueva
            if (phoneIsOlder && !working && agentVersion.isNotEmpty() && WirelessDebug.ready(context) && Prefs.autoUpdateTried != agentVersion) {
                Prefs.autoUpdateTried = agentVersion
                actualizar(true)
            }
            delay(20_000)
        }
    }
    message?.let {
        GlassCard(Modifier.fillMaxWidth().padding(bottom = 10.dp), glow = Neon.Amber) {
            Text("Hay una actualización pendiente", style = MaterialTheme.typography.titleSmall, color = Neon.Amber)
            Text(it, style = MaterialTheme.typography.bodySmall, color = Neon.Text)
            if (phoneIsOlder) {
                val wifi = WirelessDebug.ready(context)
                Spacer(Modifier.height(8.dp))
                NeonButton(if (working) "Actualizando…" else "Actualizar la app ahora", Modifier.fillMaxWidth(), enabled = !working) { actualizar(wifi) }
                Text(
                    if (wifi) "El PC la instala por Wi-Fi y la app se reinicia sola (no hay que tocar nada más)."
                    else "Al terminar de descargar, Android te pedirá tocar «Instalar».",
                    style = MaterialTheme.typography.bodySmall, color = Neon.TextFaint,
                )
                if (wifi) {
                    TextButton(onClick = { actualizar(false) }, enabled = !working) {
                        Text("Mejor descargarla e instalarla yo", color = Neon.TextDim)
                    }
                }
            }
            problem?.let { p -> Text(p, style = MaterialTheme.typography.bodySmall, color = Neon.Amber) }
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
