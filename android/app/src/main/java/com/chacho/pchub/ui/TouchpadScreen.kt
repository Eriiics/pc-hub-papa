package com.chacho.pchub.ui

import android.os.SystemClock
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.input.pointer.positionChange
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.chacho.pchub.Prefs
import com.chacho.pchub.net.FastInput
import com.chacho.pchub.net.Pc
import org.json.JSONObject
import kotlin.math.abs
import kotlin.math.max
import kotlin.math.min

/**
 * Gestos:
 *  - 1 dedo: mover el mouse        - toque: clic izquierdo
 *  - 2 dedos: desplazar (scroll)   - toque con 2 dedos: clic derecho
 *  - mantener 1 dedo quieto ~0,4 s y luego mover: arrastrar
 *  - toque con 3 dedos: clic central
 */
@Composable
fun TouchpadScreen() {
    val haptic = rememberHaptic()
    var dragging by remember { mutableStateOf(false) }

    Column(Modifier.fillMaxSize().padding(vertical = 8.dp)) {
        Box(Modifier.padding(horizontal = 14.dp)) { QuickKeyRow() }

        Box(
            Modifier
                .weight(1f)
                .fillMaxWidth()
                .padding(14.dp)
                .glass(RoundedCornerShape(30.dp), glow = if (dragging) Neon.Cyan else null, tint = if (dragging) Neon.Cyan else null)
                .pointerInput(Unit) { touchpadGestures(haptic) { dragging = it } },
            contentAlignment = Alignment.Center,
        ) {
            Text(
                if (dragging) "Arrastrando…" else "Desliza para mover · 2 dedos para desplazar\nToca para clic · 2 dedos para clic derecho\nMantén y mueve para arrastrar",
                textAlign = TextAlign.Center,
                style = MaterialTheme.typography.bodySmall,
                color = Neon.TextFaint,
            )
        }

        Row(
            Modifier
                .fillMaxWidth()
                .height(66.dp)
                .padding(horizontal = 14.dp),
            horizontalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            MouseButton("Izquierdo", "left", Modifier.weight(2f))
            MouseButton("Medio", "middle", Modifier.weight(1f))
            MouseButton("Derecho", "right", Modifier.weight(2f))
        }
        Spacer(Modifier.height(8.dp))
    }
}

/** Botón físico: mientras lo mantienes, el botón del mouse sigue presionado (sirve para arrastrar). */
@Composable
private fun MouseButton(label: String, button: String, modifier: Modifier) {
    val haptic = rememberHaptic()
    var pressed by remember { mutableStateOf(false) }
    Box(
        modifier
            .fillMaxSize()
            .glass(RoundedCornerShape(20.dp), glow = if (pressed) Neon.Cyan else null, tint = if (pressed) Neon.Cyan else null)
            .pointerInput(button) {
                detectTapGestures(onPress = {
                    pressed = true
                    haptic(false)
                    Pc.send("mouse.down", JSONObject().put("b", button))
                    tryAwaitRelease()
                    Pc.send("mouse.up", JSONObject().put("b", button))
                    pressed = false
                })
            },
        contentAlignment = Alignment.Center,
    ) {
        Text(label, style = MaterialTheme.typography.labelLarge, color = if (pressed) Neon.Cyan else Neon.Text)
    }
}

private suspend fun androidx.compose.ui.input.pointer.PointerInputScope.touchpadGestures(
    haptic: (Boolean) -> Unit,
    onDragging: (Boolean) -> Unit,
) {
    val slop = viewConfiguration.touchSlop
    val holdToDragMs = 420L
    val tapMaxMs = 300L

    awaitEachGesture {
        val first = awaitFirstDown(requireUnconsumed = false)
        val start = first.uptimeMillis
        var maxPointers = 1
        var travel = 0f
        var dragging = false
        var accX = 0f
        var accY = 0f
        var accScrollY = 0f
        var accScrollX = 0f

        fun maybeStartDrag(now: Long) {
            if (!dragging && maxPointers == 1 && travel < slop && now - start > holdToDragMs) {
                dragging = true
                onDragging(true)
                haptic(true)
                Pc.send("mouse.down", JSONObject().put("b", "left"))
            }
        }

        while (true) {
            // Si el dedo está quieto no llegan eventos: el timeout permite detectar "mantener"
            val event = withTimeoutOrNull(60L) { awaitPointerEvent() }
            if (event == null) {
                maybeStartDrag(SystemClock.uptimeMillis())
                continue
            }
            val pressed = event.changes.filter { it.pressed }
            if (pressed.isEmpty()) {
                event.changes.forEach { it.consume() }
                break
            }
            maxPointers = max(maxPointers, pressed.size)

            if (pressed.size == 1 && maxPointers == 1) {
                val change = pressed[0]
                val d = change.positionChange()
                travel += d.getDistance()
                maybeStartDrag(change.uptimeMillis)
                if (travel >= slop || dragging) {
                    val speed = d.getDistance()
                    val factor = Prefs.sensitivity * (1f + Prefs.acceleration * min(speed, 30f) / 30f)
                    accX += d.x * factor
                    accY += d.y * factor
                    val ix = accX.toInt()
                    val iy = accY.toInt()
                    if (ix != 0 || iy != 0) {
                        FastInput.move(ix, iy)
                        accX -= ix
                        accY -= iy
                    }
                }
            } else if (pressed.size >= 2) {
                val dx = pressed.map { it.positionChange().x }.average().toFloat()
                val dy = pressed.map { it.positionChange().y }.average().toFloat()
                travel += abs(dx) + abs(dy)
                // Natural (como en el teléfono): subir los dedos baja el contenido
                val dir = if (Prefs.naturalScroll) 1f else -1f
                accScrollY += dy * Prefs.scrollSpeed * dir
                accScrollX += -dx * Prefs.scrollSpeed * dir
                val iy = accScrollY.toInt()
                val ix = accScrollX.toInt()
                if (iy != 0 || ix != 0) {
                    FastInput.scroll(iy, ix)
                    accScrollY -= iy
                    accScrollX -= ix
                }
            }
            event.changes.forEach { it.consume() }
        }

        val duration = SystemClock.uptimeMillis() - start
        if (dragging) {
            Pc.send("mouse.up", JSONObject().put("b", "left"))
            onDragging(false)
        } else if (travel < slop * 1.5f && duration < tapMaxMs + 100 * (maxPointers - 1)) {
            haptic(false)
            when (maxPointers) {
                1 -> Pc.click("left")
                2 -> Pc.click("right")
                else -> Pc.click("middle")
            }
        }
    }
}
