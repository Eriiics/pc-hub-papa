package com.chacho.pchub.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
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
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.IntSize
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import com.chacho.pchub.net.Pc
import kotlinx.coroutines.launch
import org.json.JSONObject

/** Abre/cierra el diálogo de captura (lo muestra MainUi). */
object CaptureUi {
    var visible by mutableStateOf(false)

    fun open() {
        visible = true
    }
}

/**
 * Captura de pantalla del PC elegida desde el teléfono: pantalla completa, ventana activa o una zona dibujada sobre
 * una vista previa. La imagen queda en el portapapeles del PC.
 */
@Composable
fun CaptureDialog() {
    if (!CaptureUi.visible) return
    val scope = rememberCoroutineScope()
    var mode by remember { mutableIntStateOf(0) }
    var previewB64 by remember { mutableStateOf<String?>(null) }
    var loading by remember { mutableStateOf(false) }
    var busy by remember { mutableStateOf(false) }
    var start by remember { mutableStateOf<Offset?>(null) }
    var end by remember { mutableStateOf<Offset?>(null) }
    var boxSize by remember { mutableStateOf(IntSize.Zero) }
    val preview = rememberBase64Image(previewB64)

    fun loadPreview() {
        loading = true
        start = null
        end = null
        scope.launch {
            try {
                previewB64 = Pc.call("screen.preview", timeoutMs = 20_000).str("image")
            } catch (e: Exception) {
                Toaster.show(e.message ?: "No se pudo obtener la vista previa")
            } finally {
                loading = false
            }
        }
    }

    fun capture(params: JSONObject) {
        busy = true
        scope.launch {
            try {
                val d = Pc.call("screen.capture", params, 20_000)
                Toaster.show(d.optString("message", "Captura copiada al portapapeles del PC"))
                CaptureUi.visible = false
            } catch (e: Exception) {
                Toaster.show(e.message ?: "No se pudo capturar")
            } finally {
                busy = false
            }
        }
    }

    LaunchedEffect(mode) {
        if (mode == 2 && previewB64 == null && !loading) loadPreview()
    }

    // Zona dibujada, en fracciones (0..1) de la pantalla
    val w = boxSize.width.toFloat().coerceAtLeast(1f)
    val h = boxSize.height.toFloat().coerceAtLeast(1f)
    val a = start
    val b = end
    val hasRect = a != null && b != null && kotlin.math.abs(a.x - b.x) > 8f && kotlin.math.abs(a.y - b.y) > 8f

    Dialog(onDismissRequest = { CaptureUi.visible = false }, properties = DialogProperties(usePlatformDefaultWidth = false)) {
        val shape = RoundedCornerShape(24.dp)
        Column(
            Modifier
                .fillMaxWidth()
                .padding(horizontal = 14.dp)
                .clip(shape)
                .background(Neon.Panel)
                .glass(shape)
                .padding(16.dp),
        ) {
            Text("Captura de pantalla", style = MaterialTheme.typography.titleLarge, color = Neon.Text)
            Text("Se copia al portapapeles del PC (pégala con Ctrl+V).", color = Neon.TextDim, style = MaterialTheme.typography.bodySmall)
            Spacer(Modifier.size(12.dp))
            SegmentedTabs(listOf("Pantalla", "Ventana", "Zona"), mode) { mode = it }
            Spacer(Modifier.size(12.dp))

            when (mode) {
                0 -> Text("Captura toda la pantalla principal del PC.", color = Neon.TextDim, modifier = Modifier.padding(vertical = 8.dp))
                1 -> Text("Captura la ventana que está activa ahora en el PC.", color = Neon.TextDim, modifier = Modifier.padding(vertical = 8.dp))
                else -> {
                    Text("Arrastra el dedo sobre la vista previa para marcar la zona.", color = Neon.TextDim, style = MaterialTheme.typography.bodySmall)
                    Spacer(Modifier.size(8.dp))
                    val ratio = preview?.let { it.width.toFloat() / it.height.toFloat() } ?: (16f / 9f)
                    Box(
                        Modifier
                            .fillMaxWidth()
                            .aspectRatio(ratio)
                            .clip(RoundedCornerShape(12.dp))
                            .background(Neon.Bg)
                            .onSizeChanged { boxSize = it }
                            .pointerInput(preview != null) {
                                detectDragGestures(
                                    onDragStart = { o -> start = o; end = o },
                                    onDrag = { change, _ ->
                                        change.consume()
                                        end = Offset(
                                            change.position.x.coerceIn(0f, size.width.toFloat()),
                                            change.position.y.coerceIn(0f, size.height.toFloat()),
                                        )
                                    },
                                )
                            },
                        contentAlignment = Alignment.Center,
                    ) {
                        if (preview != null) {
                            Image(preview, contentDescription = null, contentScale = ContentScale.FillBounds, modifier = Modifier.fillMaxSize())
                        } else {
                            Text(if (loading) "Cargando vista previa…" else "Sin vista previa", color = Neon.TextDim)
                        }
                        if (a != null && b != null) {
                            Canvas(Modifier.fillMaxSize()) {
                                val topLeft = Offset(minOf(a.x, b.x), minOf(a.y, b.y))
                                val rect = Size(kotlin.math.abs(a.x - b.x), kotlin.math.abs(a.y - b.y))
                                drawRect(Neon.Cyan.copy(alpha = 0.18f), topLeft, rect)
                                drawRect(Neon.Cyan, topLeft, rect, style = Stroke(width = 2.dp.toPx()))
                            }
                        }
                    }
                    Spacer(Modifier.size(8.dp))
                    NeonButton("Actualizar vista previa", Modifier.fillMaxWidth(), outlined = true, enabled = !loading) { loadPreview() }
                }
            }

            Spacer(Modifier.size(12.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                NeonButton("Recorte de Windows", Modifier.weight(1f), outlined = true) {
                    Pc.key("win+shift+s")
                    CaptureUi.visible = false
                }
                NeonButton(
                    if (busy) "Capturando…" else "Capturar",
                    Modifier.weight(1f),
                    enabled = !busy && (mode != 2 || (preview != null && hasRect)),
                ) {
                    when (mode) {
                        0 -> capture(JSONObject().put("mode", "full"))
                        1 -> capture(JSONObject().put("mode", "window"))
                        else -> if (a != null && b != null) capture(
                            JSONObject().put("mode", "region")
                                .put("x", (minOf(a.x, b.x) / w).toDouble())
                                .put("y", (minOf(a.y, b.y) / h).toDouble())
                                .put("w", (kotlin.math.abs(a.x - b.x) / w).toDouble())
                                .put("h", (kotlin.math.abs(a.y - b.y) / h).toDouble())
                        )
                    }
                }
            }
            Text(
                "Cerrar",
                color = Neon.TextDim,
                textAlign = TextAlign.Center,
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(top = 12.dp)
                    .clickable { CaptureUi.visible = false }
                    .padding(vertical = 4.dp),
            )
        }
    }
}
