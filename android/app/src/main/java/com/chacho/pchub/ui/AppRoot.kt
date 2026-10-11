package com.chacho.pchub.ui

import androidx.activity.compose.BackHandler
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.selection.SelectionContainer
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Keyboard
import androidx.compose.material.icons.filled.KeyboardHide
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.chacho.pchub.Prefs
import com.chacho.pchub.net.Pc

@Composable
fun AppRoot() {
    val status by Pc.status.collectAsState()
    val reconnecting = status == Pc.Status.Connecting && Prefs.token.isNotEmpty()

    Box(Modifier.fillMaxSize()) {
        NeonBackground()
        if (status == Pc.Status.Connected || reconnecting) MainUi(reconnecting) else ConnectScreen()
    }
}

/** Estado de navegación compartido (para que Inicio y Módulos puedan abrir pantallas). */
class Nav(initial: Screen) {
    var screen by mutableStateOf(initial)
        private set

    /** Pantalla a la que volver si la actual se abrió sola por contexto (ej. al poner música en el PC). */
    var autoFrom: Screen? = null

    fun go(to: Screen) {
        autoFrom = null
        screen = to
        if (to.module) Prefs.recordUse(to.name)
        when (to) {
            Screen.Touchpad, Screen.Pointer -> Prefs.lastControl = to.name
            Screen.Media, Screen.YouTube -> Prefs.lastMusic = to.name
            else -> Unit
        }
    }

    fun goTab(tab: Tab) = go(
        when (tab) {
            Tab.Home -> Screen.Home
            Tab.Control -> runCatching { Screen.valueOf(Prefs.lastControl) }.getOrDefault(Screen.Touchpad)
            Tab.Music -> runCatching { Screen.valueOf(Prefs.lastMusic) }.getOrDefault(Screen.Media)
            Tab.Ai -> Screen.Ai
            Tab.Modules -> Screen.Modules
        }
    )

    fun autoSwitch(to: Screen) {
        if (to == screen) return
        autoFrom = screen
        screen = to
    }

    fun autoReturn() {
        autoFrom?.let { screen = it }
        autoFrom = null
    }

    /** Atrás: sub-pantalla de Módulos → Módulos; cualquier otra → Inicio. */
    fun back(): Boolean {
        if (screen == Screen.Home) return false
        screen = if (screen.tab == Tab.Modules && screen != Screen.Modules) Screen.Modules else Screen.Home
        return true
    }
}

@Composable
private fun MainUi(reconnecting: Boolean) {
    val saved = rememberSaveable { mutableStateOf(Screen.Home.name) }
    val nav = remember { Nav(Screen.valueOf(saved.value)) }
    LaunchedEffect(nav.screen) { saved.value = nav.screen.name }

    var kbOpen by rememberSaveable { mutableStateOf(false) }
    val ctx by Pc.context.collectAsState()

    BackHandler(enabled = nav.screen != Screen.Home) { nav.back() }

    // Cambio automático de pantalla según lo que está en primer plano en el PC
    LaunchedEffect(ctx) {
        if (!Prefs.autoSwitch) return@LaunchedEffect
        // Módulos marcados en Ajustes: mientras estés en uno, no te saca
        if (nav.screen.name in Prefs.autoSwitchLocked) return@LaunchedEffect
        val target = moduleToScreen(ctx?.str("module"))
        if (target != null) nav.autoSwitch(target) else nav.autoReturn()
    }

    // Teclado automático cuando el PC tiene el foco en un campo de texto
    LaunchedEffect(Unit) {
        Pc.textFocus.collect { f ->
            if (f.optBoolean("editable")) {
                if (Prefs.autoKeyboard) kbOpen = true
            } else if (Prefs.autoHideKeyboard) {
                kbOpen = false
            }
        }
    }

    Column(
        Modifier
            .fillMaxSize()
            .statusBarsPadding()
            .imePadding()
    ) {
        TopBar(nav, reconnecting, kbOpen) { kbOpen = !kbOpen }

        Box(Modifier.weight(1f).fillMaxWidth()) {
            AnimatedContent(
                targetState = nav.screen,
                transitionSpec = { fadeIn() togetherWith fadeOut() },
                label = "screen",
            ) { screen ->
                when (screen) {
                    Screen.Home -> HomeScreen(nav)
                    Screen.Touchpad -> TouchpadScreen()
                    Screen.Pointer -> PointerScreen()
                    Screen.Media -> MediaScreen()
                    Screen.YouTube -> YouTubeScreen()
                    Screen.Ai -> AiScreen()
                    Screen.Modules -> ModulesScreen(nav)
                    Screen.Windows -> WindowsScreen()
                    Screen.Apps -> AppsScreen()
                    Screen.System -> SystemScreen()
                    Screen.Terminal -> TerminalScreen()
                    Screen.Excel -> ExcelScreen()
                    Screen.Settings -> SettingsScreen()
                }
            }
        }

        if (kbOpen) KeyboardBar(onClose = { kbOpen = false })
        else BottomBar(nav)
    }

    ConfirmFromPcDialog()
    CaptureDialog()
}

@Composable
private fun TopBar(nav: Nav, reconnecting: Boolean, kbOpen: Boolean, onKeyboard: () -> Unit) {
    val pcName by Pc.pcName.collectAsState()
    val screen = nav.screen
    Column {
        Row(
            Modifier
                .fillMaxWidth()
                .padding(start = 12.dp, end = 12.dp, top = 6.dp, bottom = 6.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            if (screen != Screen.Home && screen.tab == Tab.Modules && screen != Screen.Modules) {
                NeonIconButton(Icons.AutoMirrored.Filled.ArrowBack, "Volver", size = 40.dp, accent = Neon.TextDim) { nav.back() }
                Spacer(Modifier.width(10.dp))
            } else {
                Spacer(Modifier.width(4.dp))
            }
            Column(Modifier.weight(1f)) {
                Text(
                    if (screen == Screen.Home) "PC Hub" else screen.title,
                    style = MaterialTheme.typography.headlineSmall,
                    color = Neon.Text,
                )
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Box(
                        Modifier
                            .size(7.dp)
                            .clip(CircleShape)
                            .background(if (reconnecting) Neon.Amber else Neon.Lime)
                    )
                    Spacer(Modifier.width(6.dp))
                    Text(
                        if (reconnecting) "Reconectando…" else pcName.ifEmpty { "Conectado" },
                        style = MaterialTheme.typography.labelMedium,
                        color = Neon.TextDim,
                    )
                }
            }
            NeonIconButton(
                if (kbOpen) Icons.Filled.KeyboardHide else Icons.Filled.Keyboard,
                "Teclado",
                size = 42.dp,
                filled = kbOpen,
                onClick = onKeyboard,
            )
        }
        if (reconnecting) LinearProgressIndicator(modifier = Modifier.fillMaxWidth().height(2.dp), color = Neon.Amber)

        // Sub-pestañas de Control y Música
        when (screen.tab) {
            Tab.Control -> SegmentedTabs(
                listOf("Touchpad", "Puntero"),
                if (screen == Screen.Pointer) 1 else 0,
                Modifier.padding(horizontal = 16.dp, vertical = 4.dp),
            ) { nav.go(if (it == 1) Screen.Pointer else Screen.Touchpad) }
            Tab.Music -> SegmentedTabs(
                listOf("Reproductor", "YouTube"),
                if (screen == Screen.YouTube) 1 else 0,
                Modifier.padding(horizontal = 16.dp, vertical = 4.dp),
            ) { nav.go(if (it == 1) Screen.YouTube else Screen.Media) }
            else -> Unit
        }
    }
}

/** Barra inferior flotante de vidrio. */
@Composable
private fun BottomBar(nav: Nav) {
    val haptic = rememberHaptic()
    Row(
        Modifier
            .navigationBarsPadding()
            .fillMaxWidth()
            .glass(RoundedCornerShape(topStart = 28.dp, topEnd = 28.dp))
            .padding(top = 8.dp, bottom = 6.dp, start = 4.dp, end = 4.dp),
        horizontalArrangement = Arrangement.SpaceAround,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Tab.entries.forEach { tab ->
            val active = nav.screen.tab == tab
            val color by animateColorAsState(if (active) Neon.Cyan else Neon.TextFaint, label = "tab")
            Column(
                Modifier
                    .weight(1f)
                    .clip(RoundedCornerShape(20.dp))
                    .clickable { haptic(false); nav.goTab(tab) }
                    .padding(vertical = 6.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
            ) {
                Box(
                    Modifier
                        .size(width = 46.dp, height = 30.dp)
                        .then(
                            if (active) Modifier
                                .clip(RoundedCornerShape(50))
                                .background(Neon.Cyan.copy(alpha = 0.22f))
                            else Modifier
                        ),
                    contentAlignment = Alignment.Center,
                ) {
                    Icon(tab.icon, contentDescription = tab.label, tint = color, modifier = Modifier.size(22.dp))
                }
                Spacer(Modifier.height(2.dp))
                Text(
                    tab.label,
                    style = MaterialTheme.typography.labelSmall,
                    color = color,
                    fontWeight = if (active) FontWeight.Bold else FontWeight.Medium,
                )
            }
        }
    }
}

/** El PC pide permiso (la IA quiere ejecutar PowerShell, apagar, etc.). */
@Composable
private fun ConfirmFromPcDialog() {
    val req by Pc.confirm.collectAsState()
    val r = req ?: return
    val id = r.optString("id")
    val haptic = rememberHaptic()
    LaunchedEffect(id) { haptic(true) }
    AlertDialog(
        onDismissRequest = { Pc.answerConfirm(id, false) },
        containerColor = Neon.Panel,
        title = { Text(r.optString("title", "Confirmar")) },
        text = {
            SelectionContainer {
                Text(
                    r.optString("detail"),
                    fontFamily = FontFamily.Monospace,
                    style = MaterialTheme.typography.bodySmall,
                    modifier = Modifier.verticalScroll(rememberScrollState()).padding(top = 4.dp),
                )
            }
        },
        confirmButton = { TextButton(onClick = { Pc.answerConfirm(id, true) }) { Text("Permitir", color = Neon.Cyan) } },
        dismissButton = { TextButton(onClick = { Pc.answerConfirm(id, false) }) { Text("Rechazar", color = Neon.TextDim) } },
    )
}

/** Colores de acento para chips de estado. */
fun statusColor(ok: Boolean?): Color = when (ok) {
    true -> Neon.Lime
    false -> Neon.Red
    null -> Neon.TextDim
}
