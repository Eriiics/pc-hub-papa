package com.chacho.pchub.ui

import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.isImeVisible
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Close
import androidx.compose.material3.AssistChip
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.text.TextRange
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.input.TextFieldValue
import androidx.compose.ui.unit.dp
import com.chacho.pchub.net.Pc
import kotlinx.coroutines.delay

/** Caracter invisible que siempre queda en el campo para poder detectar "borrar" aunque esté vacío. */
private const val SENTINEL = "​"

/**
 * Teclado remoto: lo que escribes con el teclado de Android se envía al PC al instante.
 * Se calcula la diferencia entre el texto anterior y el nuevo, así funcionan el autocorrector y el dictado.
 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun KeyboardBar(onClose: () -> Unit) {
    var field by remember { mutableStateOf(TextFieldValue(SENTINEL, TextRange(1))) }
    var lastSent by remember { mutableStateOf("") }
    val focus = remember { FocusRequester() }
    val keyboard = LocalSoftwareKeyboardController.current
    val haptic = rememberHaptic()

    var ctrl by remember { mutableStateOf(false) }
    var alt by remember { mutableStateOf(false) }
    var shift by remember { mutableStateOf(false) }
    var win by remember { mutableStateOf(false) }

    fun mods(): String = buildList {
        if (ctrl) add("ctrl")
        if (alt) add("alt")
        if (shift) add("shift")
        if (win) add("win")
    }.joinToString("+")

    fun clearMods() {
        ctrl = false; alt = false; shift = false; win = false
    }

    /** Envía una tecla especial combinándola con los modificadores activos. */
    fun special(key: String) {
        haptic(false)
        val m = mods()
        Pc.key(if (m.isEmpty()) key else "$m+$key")
        clearMods()
    }

    fun onChange(nv: TextFieldValue) {
        val old = field.text
        val new = nv.text
        val prefix = old.commonPrefixWith(new).length
        val deleted = old.length - prefix
        val inserted = new.substring(prefix).replace(SENTINEL, "")

        if (deleted > 0) Pc.key("backspace", deleted)
        if (inserted.isNotEmpty()) {
            val m = mods()
            if (m.isNotEmpty() && inserted.length == 1 && inserted[0].isLetterOrDigit()) {
                Pc.key("$m+${inserted.lowercase()}")
                clearMods()
            } else {
                Pc.text(inserted)
            }
            lastSent = (lastSent + inserted).takeLast(40)
        } else if (deleted > 0) {
            lastSent = lastSent.dropLast(deleted)
        }

        var result = if (new.startsWith(SENTINEL)) new else SENTINEL + new.replace(SENTINEL, "")
        // Vaciar el búfer tras un espacio para que no crezca (sin romper la palabra que se está componiendo)
        if (result.length > 48 && inserted.endsWith(" ")) result = SENTINEL
        field = if (result == new) nv else TextFieldValue(result, TextRange(result.length))
    }

    LaunchedEffect(Unit) {
        delay(120)
        focus.requestFocus()
        keyboard?.show()
    }

    // Si cierras el teclado de Android (gesto atrás, etc.), la barra también se cierra para no dejar el campo abierto
    val imeVisible = WindowInsets.isImeVisible
    var imeSeen by remember { mutableStateOf(false) }
    LaunchedEffect(imeVisible) {
        if (imeVisible) imeSeen = true
        else if (imeSeen) onClose()
    }

    Box(
        Modifier
            .navigationBarsPadding()
            .padding(horizontal = 10.dp, vertical = 8.dp)
            .glass(RoundedCornerShape(24.dp), glow = Neon.Cyan, strong = true)
    ) {
        Column(Modifier.fillMaxWidth().padding(vertical = 6.dp)) {
            Row(
                Modifier
                    .fillMaxWidth()
                    .horizontalScroll(rememberScrollState())
                    .padding(horizontal = 8.dp),
                horizontalArrangement = Arrangement.spacedBy(6.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                FilterChip(selected = ctrl, onClick = { ctrl = !ctrl }, label = { Text("Ctrl") })
                FilterChip(selected = alt, onClick = { alt = !alt }, label = { Text("Alt") })
                FilterChip(selected = shift, onClick = { shift = !shift }, label = { Text("Shift") })
                FilterChip(selected = win, onClick = { win = !win }, label = { Text("Win") })
                listOf(
                    "Esc" to "esc", "Tab" to "tab", "←" to "left", "↑" to "up", "↓" to "down", "→" to "right",
                    "Supr" to "delete", "Inicio" to "home", "Fin" to "end", "RePág" to "pgup", "AvPág" to "pgdn",
                    "F5" to "f5", "F11" to "f11",
                ).forEach { (label, key) ->
                    AssistChip(onClick = { special(key) }, label = { Text(label) })
                }
            }
            Row(
                Modifier.fillMaxWidth().padding(start = 12.dp, end = 4.dp, top = 4.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Box(
                    Modifier
                        .weight(1f)
                        .glass(RoundedCornerShape(14.dp))
                        .padding(horizontal = 12.dp, vertical = 10.dp)
                ) {
                    if (lastSent.isEmpty()) {
                        Text("Escribe para enviar al PC…", color = MaterialTheme.colorScheme.onSurfaceVariant,
                            style = MaterialTheme.typography.bodyMedium)
                    }
                    BasicTextField(
                        value = field,
                        onValueChange = ::onChange,
                        singleLine = true,
                        textStyle = MaterialTheme.typography.bodyMedium.copy(color = MaterialTheme.colorScheme.onSurface),
                        cursorBrush = SolidColor(MaterialTheme.colorScheme.primary),
                        keyboardOptions = KeyboardOptions(
                            capitalization = KeyboardCapitalization.None,
                            imeAction = ImeAction.Send,
                        ),
                        keyboardActions = KeyboardActions(onSend = {
                            Pc.key("enter")
                            lastSent = ""
                            field = TextFieldValue(SENTINEL, TextRange(1))
                        }),
                        modifier = Modifier
                            .fillMaxWidth()
                            .focusRequester(focus),
                    )
                }
                Spacer(Modifier.size(4.dp))
                IconButton(onClick = { keyboard?.hide(); onClose() }) {
                    Icon(Icons.Filled.Close, contentDescription = "Cerrar teclado")
                }
            }
        }
    }
}
