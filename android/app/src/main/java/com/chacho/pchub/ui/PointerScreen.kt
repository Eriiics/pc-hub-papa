package com.chacho.pchub.ui

import android.view.KeyEvent
import androidx.compose.animation.animateColorAsState
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CenterFocusStrong
import androidx.compose.material.icons.filled.Tune
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.SideEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.chacho.pchub.Prefs
import com.chacho.pchub.input.PointerEngine
import com.chacho.pchub.net.FastInput
import com.chacho.pchub.net.Pc
import kotlinx.coroutines.delay
import org.json.JSONObject
import kotlin.math.abs

/**
 * Botones físicos del teléfono (volumen) para pantallas que los usen. MainActivity le pasa cada tecla;
 * si el handler devuelve true, la tecla no cambia el volumen.
 */
object HardwareKeys {
    @Volatile var handler: ((keyCode: Int, down: Boolean, repeat: Int) -> Boolean)? = null
}

/**
 * Puntero con el teléfono (ver PointerEngine). Pensado para usarse sin mirar:
 *  - A ocupa casi toda la pantalla: tocar = clic, mantener = arrastrar, doble toque = doble clic.
 *  - B abajo: mantener para apuntar (con "Siempre", mantener = pausar para reacomodar la mano).
 *  - Franja derecha: scroll con el dedo. Con "gestos con el movimiento" (Ajustes) mantenerla e inclinar el
 *    teléfono también hace scroll, y mantener A apunta aunque no tengas B (arrastrar moviendo el teléfono).
 *  - Volumen + = clic (mantener = arrastrar) · Volumen − = clic derecho.
 */
@Composable
fun PointerScreen() {
    val context = LocalContext.current
    val haptic = rememberHaptic()
    var bHeld by remember { mutableStateOf(false) }
    var aDown by remember { mutableStateOf(false) }
    var scrollHeld by remember { mutableStateOf(false) }
    var showTuning by remember { mutableStateOf(false) }
    val wii = Prefs.pointerMode == "wii"
    val gestures = Prefs.pointerMotionGestures
    val aiming = (if (Prefs.pointerAlwaysOn) !bHeld else bHeld) || (gestures && aDown)

    // ---------- motor de sensores ----------
    val engine = remember { PointerEngine(context.applicationContext) }
    DisposableEffect(engine) {
        if (!engine.hasGyro) Toaster.show("Este teléfono no tiene giroscopio")
        engine.start()
        onDispose {
            engine.aiming = false
            engine.scrollMode = false
            engine.stop()
        }
    }
    SideEffect {
        engine.mode = if (wii && engine.hasWii) PointerEngine.Mode.Wii else PointerEngine.Mode.Air
        engine.aiming = aiming && !scrollHeld
        engine.scrollMode = gestures && scrollHeld
    }

    // ---------- pantalla siempre encendida mientras se usa el puntero ----------
    val view = LocalView.current
    DisposableEffect(view) {
        view.keepScreenOn = true
        onDispose { view.keepScreenOn = false }
    }

    // ---------- canal y posición (vista previa del modo Wii) ----------
    var fast by remember { mutableStateOf(FastInput.available) }
    var cx by remember { mutableFloatStateOf(0.5f) }
    var cy by remember { mutableFloatStateOf(0.5f) }
    val preview = wii && Prefs.pointerPreview
    LaunchedEffect(preview) {
        var n = 0
        while (true) {
            if (preview) {
                cx = engine.cursorX
                cy = engine.cursorY
            }
            if (n++ % 15 == 0) fast = FastInput.available
            delay(if (preview) 33 else 500)
        }
    }

    // ---------- botones de volumen como gatillos ----------
    DisposableEffect(Unit) {
        HardwareKeys.handler = { code, down, repeat ->
            if (!Prefs.pointerVolumeKeys) false
            else when (code) {
                KeyEvent.KEYCODE_VOLUME_UP -> {
                    if (repeat == 0) Pc.send(if (down) "mouse.down" else "mouse.up", JSONObject().put("b", "left"))
                    true
                }
                KeyEvent.KEYCODE_VOLUME_DOWN -> {
                    if (down && repeat == 0) Pc.click("right")
                    true
                }
                else -> false
            }
        }
        onDispose { HardwareKeys.handler = null }
    }

    val accent = if (wii) Neon.Cyan else Neon.Magenta

    Column(
        Modifier
            .fillMaxSize()
            .padding(horizontal = 12.dp, vertical = 6.dp)
    ) {
        // ---------- barra superior compacta ----------
        Row(verticalAlignment = Alignment.CenterVertically) {
            SegmentedTabs(listOf("Aire", "Wii"), if (wii) 1 else 0, Modifier.weight(1f)) {
                if (it == 1 && !engine.hasWii) Toaster.show("Este teléfono no tiene sensor de orientación")
                else Prefs.pointerMode = if (it == 1) "wii" else "air"
            }
            Spacer(Modifier.width(6.dp))
            Chip(if (Prefs.pointerAlwaysOn) "Siempre" else "Con B", active = Prefs.pointerAlwaysOn, accent = accent) {
                Prefs.pointerAlwaysOn = !Prefs.pointerAlwaysOn
            }
            if (wii) {
                Spacer(Modifier.width(6.dp))
                NeonIconButton(Icons.Filled.CenterFocusStrong, "Centrar", size = 40.dp, accent = Neon.Cyan) {
                    haptic(true)
                    engine.recenter()
                }
            }
            Spacer(Modifier.width(6.dp))
            NeonIconButton(Icons.Filled.Tune, "Ajustes del puntero", size = 40.dp, accent = accent, filled = showTuning) {
                showTuning = !showTuning
            }
        }

        // Estado en una línea
        Row(Modifier.padding(top = 6.dp, start = 4.dp), verticalAlignment = Alignment.CenterVertically) {
            Box(Modifier.size(7.dp).clip(CircleShape).background(if (fast) Neon.Lime else Neon.Amber))
            Spacer(Modifier.width(6.dp))
            Text(
                when {
                    scrollHeld && gestures -> "Inclina para hacer scroll"
                    aiming -> "Apuntando"
                    else -> "En pausa"
                } + if (fast) " · conexión rápida" else " · conexión normal",
                style = MaterialTheme.typography.labelSmall,
                color = if (aiming || scrollHeld) accent else Neon.TextFaint,
            )
        }

        if (showTuning) {
            Spacer(Modifier.height(8.dp))
            GlassCard(Modifier.fillMaxWidth(), padding = 12.dp) {
                PointerTuning()
                if (wii) SwitchRow("Mostrar la mini pantalla (vista previa)", Prefs.pointerPreview) { Prefs.pointerPreview = it }
                SwitchRow("Arrastrar y hacer scroll moviendo el teléfono", Prefs.pointerMotionGestures) { Prefs.pointerMotionGestures = it }
            }
        }

        if (preview) {
            Spacer(Modifier.height(8.dp))
            WiiPreview(cx, cy, aiming, Modifier.fillMaxWidth(0.6f).align(Alignment.CenterHorizontally))
        }

        Spacer(Modifier.height(10.dp))

        // ---------- A (zona grande) + franja de scroll ----------
        Row(Modifier.weight(1f).fillMaxWidth()) {
            val aColor by animateColorAsState(if (aDown) Neon.Cyan else Neon.Cyan.copy(alpha = 0.55f), label = "a")
            Box(
                Modifier
                    .weight(1f)
                    .fillMaxHeight()
                    .neonShadow(if (aDown) 30.dp else 14.dp, RoundedCornerShape(36.dp), Neon.Cyan)
                    .clip(RoundedCornerShape(36.dp))
                    .background(Brush.radialGradient(listOf(aColor, aColor.copy(alpha = 0.18f))))
                    .pointerInput(Unit) {
                        detectTapGestures(
                            onPress = {
                                aDown = true
                                haptic(false)
                                Pc.send("mouse.down", JSONObject().put("b", "left"))
                                tryAwaitRelease()
                                Pc.send("mouse.up", JSONObject().put("b", "left"))
                                aDown = false
                            },
                            onDoubleTap = { Pc.click("left", double = true) },
                        )
                    },
                contentAlignment = Alignment.Center,
            ) {
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Text("A", fontSize = 96.sp, fontWeight = FontWeight.Black, color = Neon.Bg.copy(alpha = 0.85f))
                    Text(
                        if (gestures) "mantén y mueve para arrastrar" else "mantén para arrastrar",
                        style = MaterialTheme.typography.labelSmall,
                        color = Neon.Bg.copy(alpha = 0.7f),
                    )
                }
            }

            Spacer(Modifier.width(10.dp))

            // Franja de scroll: arrastrar el dedo; con gestos, mantener e inclinar el teléfono
            var acc by remember { mutableFloatStateOf(0f) }
            Box(
                Modifier
                    .width(70.dp)
                    .fillMaxHeight()
                    .glass(RoundedCornerShape(35.dp), tint = if (scrollHeld) accent else null, glow = if (scrollHeld) accent else null)
                    .pointerInput(Unit) {
                        awaitEachGesture {
                            val down = awaitFirstDown()
                            scrollHeld = true
                            haptic(false)
                            var lastY = down.position.y
                            try {
                                while (true) {
                                    val ev = awaitPointerEvent()
                                    val ch = ev.changes.firstOrNull { it.id == down.id } ?: break
                                    if (!ch.pressed) break
                                    val dy = ch.position.y - lastY
                                    lastY = ch.position.y
                                    ch.consume()
                                    val dir = if (Prefs.naturalScroll) 1f else -1f
                                    acc += dy * Prefs.scrollSpeed * dir
                                    val step = acc.toInt()
                                    if (abs(step) >= 1) {
                                        FastInput.scroll(step, 0)
                                        acc -= step
                                    }
                                }
                            } finally {
                                scrollHeld = false
                            }
                        }
                    },
                contentAlignment = Alignment.Center,
            ) {
                Text(
                    if (gestures) "⇵\nmantén\ne\ninclina" else "⇵\nscroll",
                    textAlign = TextAlign.Center,
                    color = if (scrollHeld) Neon.Text else Neon.TextFaint,
                    style = MaterialTheme.typography.labelSmall,
                )
            }
        }

        Spacer(Modifier.height(8.dp))

        // ---------- clic derecho / medio (chicos, para no tocarlos sin querer) ----------
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            SmallPadButton("Clic derecho", Modifier.weight(1f)) { Pc.click("right") }
            SmallPadButton("Clic medio", Modifier.weight(1f)) { Pc.click("middle") }
        }

        Spacer(Modifier.height(8.dp))

        // ---------- B ----------
        val bColor by animateColorAsState(if (bHeld) accent else Color.White.copy(alpha = 0.06f), label = "b")
        Box(
            Modifier
                .fillMaxWidth()
                .height(104.dp)
                .then(if (bHeld) Modifier.neonShadow(30.dp, RoundedCornerShape(30.dp), accent) else Modifier)
                .glass(RoundedCornerShape(30.dp), tint = if (bHeld) bColor else null, glow = if (bHeld) accent else null)
                .pointerInput(Unit) {
                    detectTapGestures(onPress = {
                        bHeld = true
                        haptic(true)
                        tryAwaitRelease()
                        bHeld = false
                    })
                },
            contentAlignment = Alignment.Center,
        ) {
            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                Text("B", fontSize = 34.sp, fontWeight = FontWeight.Black, color = if (bHeld) Neon.Text else accent)
                Text(
                    if (Prefs.pointerAlwaysOn) "mantén para pausar" else "mantén para apuntar",
                    style = MaterialTheme.typography.labelSmall,
                    color = Neon.TextDim,
                )
            }
        }
    }
}

@Composable
private fun Chip(label: String, active: Boolean, accent: Color, onClick: () -> Unit) {
    val haptic = rememberHaptic()
    Text(
        label,
        color = if (active) accent else Neon.TextDim,
        fontWeight = FontWeight.Bold,
        style = MaterialTheme.typography.labelMedium,
        modifier = Modifier
            .glass(RoundedCornerShape(50), tint = if (active) accent else null)
            .pointerInput(Unit) { detectTapGestures(onTap = { haptic(false); onClick() }) }
            .padding(horizontal = 12.dp, vertical = 9.dp),
    )
}

/** Mini pantalla del PC con la posición del puntero en modo Wii. */
@Composable
private fun WiiPreview(x: Float, y: Float, aiming: Boolean, modifier: Modifier = Modifier) {
    BoxWithConstraints(
        modifier
            .aspectRatio(16f / 9f)
            .clip(RoundedCornerShape(12.dp))
            .background(Color.Black.copy(alpha = 0.35f))
            .border(1.dp, Neon.Cyan.copy(alpha = 0.3f), RoundedCornerShape(12.dp))
    ) {
        val dot = 12.dp
        Box(Modifier.align(Alignment.Center).size(1.dp, 14.dp).background(Neon.TextFaint.copy(alpha = 0.5f)))
        Box(Modifier.align(Alignment.Center).size(14.dp, 1.dp).background(Neon.TextFaint.copy(alpha = 0.5f)))
        Box(
            Modifier
                .offset(x = (maxWidth - dot) * x, y = (maxHeight - dot) * y)
                .size(dot)
                .clip(CircleShape)
                .background(if (aiming) Neon.Cyan else Neon.TextFaint)
        )
    }
}

/** Ajustes finos del puntero, según el modo. Se usa en esta pantalla y en Ajustes. */
@Composable
fun PointerTuning() {
    if (Prefs.pointerMode == "wii") {
        SliderRow("Alcance (grados hasta el borde)", Prefs.pointerRange, 8f..45f, decimals = 0) { Prefs.pointerRange = it }
    } else {
        SliderRow("Sensibilidad", Prefs.pointerSensitivity, 400f..4000f, decimals = 0) { Prefs.pointerSensitivity = it }
        SliderRow("Precisión al mover lento", Prefs.pointerPrecision, 0f..1f, decimals = 2) { Prefs.pointerPrecision = it }
    }
    SliderRow("Anti-temblor", Prefs.pointerSmoothing, 0f..1f, decimals = 2) { Prefs.pointerSmoothing = it }
}

@Composable
private fun SmallPadButton(label: String, modifier: Modifier = Modifier, onClick: () -> Unit) {
    val haptic = rememberHaptic()
    Box(
        modifier
            .glass(RoundedCornerShape(16.dp))
            .pointerInput(Unit) { detectTapGestures(onTap = { haptic(false); onClick() }) }
            .padding(vertical = 11.dp),
        contentAlignment = Alignment.Center,
    ) {
        Text(label, color = Neon.TextDim, style = MaterialTheme.typography.labelLarge)
    }
}
