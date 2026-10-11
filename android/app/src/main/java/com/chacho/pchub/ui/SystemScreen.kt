package com.chacho.pchub.ui

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.GridItemSpan
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Bedtime
import androidx.compose.material.icons.filled.Cancel
import androidx.compose.material.icons.filled.ContentPaste
import androidx.compose.material.icons.filled.DesktopAccessDisabled
import androidx.compose.material.icons.filled.Download
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material.icons.filled.PowerSettingsNew
import androidx.compose.material.icons.filled.RestartAlt
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import org.json.JSONObject

private data class SysAction(val label: String, val action: String, val confirm: String?)

@Composable
fun SystemScreen() {
    val scope = rememberCoroutineScope()
    val context = LocalContext.current
    var pending by remember { mutableStateOf<SysAction?>(null) }

    fun run(a: SysAction) = scope.pcCall("sys.action", JSONObject().put("a", a.action)) { Toaster.show("✓ ${a.label}") }

    fun request(a: SysAction) {
        if (a.confirm != null) pending = a else run(a)
    }

    val clipboard = context.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager

    LazyVerticalGrid(
        columns = GridCells.Adaptive(minSize = 104.dp),
        contentPadding = PaddingValues(start = 16.dp, end = 16.dp, top = 0.dp, bottom = 24.dp),
        horizontalArrangement = Arrangement.spacedBy(12.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        item(span = { GridItemSpan(maxLineSpan) }) { SectionTitle("Energía") }
        item { NeonTile("Bloquear", icon = Icons.Filled.Lock, accent = Neon.Cyan) { request(SysAction("Bloquear", "lock", null)) } }
        item { NeonTile("Apagar pantalla", icon = Icons.Filled.DesktopAccessDisabled, accent = Neon.Violet) { request(SysAction("Apagar pantalla", "monitor_off", null)) } }
        item { NeonTile("Suspender", icon = Icons.Filled.Bedtime, accent = Neon.Violet) { request(SysAction("Suspender", "sleep", "¿Suspender el PC? Se cortará la conexión.")) } }
        item { NeonTile("Reiniciar", icon = Icons.Filled.RestartAlt, accent = Neon.Amber) { request(SysAction("Reiniciar", "restart", "¿Reiniciar el PC en 5 segundos?")) } }
        item { NeonTile("Apagar", icon = Icons.Filled.PowerSettingsNew, accent = Neon.Red, highlighted = true) { request(SysAction("Apagar", "shutdown", "¿Apagar el PC en 5 segundos?")) } }
        item { NeonTile("Cancelar apagado", icon = Icons.Filled.Cancel, accent = Neon.TextDim) { request(SysAction("Cancelar apagado", "cancel_shutdown", null)) } }

        item(span = { GridItemSpan(maxLineSpan) }) { SectionTitle("Portapapeles") }
        item {
            NeonTile("Enviar al PC", icon = Icons.Filled.ContentPaste, accent = Neon.Lime) {
                val text = clipboard.primaryClip?.takeIf { it.itemCount > 0 }?.getItemAt(0)?.coerceToText(context)?.toString()
                if (text.isNullOrEmpty()) Toaster.show("El portapapeles del teléfono está vacío")
                else scope.pcCall("clip.set", JSONObject().put("text", text)) { Toaster.show("Copiado en el PC") }
            }
        }
        item {
            NeonTile("Traer del PC", icon = Icons.Filled.Download, accent = Neon.Lime) {
                scope.pcCall("clip.get") { d ->
                    val text = d.optString("text")
                    if (text.isEmpty()) Toaster.show("El portapapeles del PC está vacío")
                    else {
                        clipboard.setPrimaryClip(ClipData.newPlainText("PC", text))
                        Toaster.show("Copiado en el teléfono")
                    }
                }
            }
        }
    }

    pending?.let { a ->
        AlertDialog(
            onDismissRequest = { pending = null },
            containerColor = Neon.Panel,
            title = { Text(a.label) },
            text = { Text(a.confirm ?: "") },
            confirmButton = { TextButton(onClick = { pending = null; run(a) }) { Text("Sí", color = Neon.Cyan) } },
            dismissButton = { TextButton(onClick = { pending = null }) { Text("Cancelar", color = Neon.TextDim) } },
        )
    }
}
