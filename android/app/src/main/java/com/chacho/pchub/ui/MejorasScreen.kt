package com.chacho.pchub.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Mic
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import com.chacho.pchub.net.Pc
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import org.json.JSONArray
import org.json.JSONObject

private data class Mejora(val id: String, val text: String, val status: String, val note: String)

private fun JSONArray?.toMejoras(): List<Mejora> =
    if (this == null) emptyList() else (0 until length()).map {
        val o = getJSONObject(it)
        Mejora(o.optString("id"), o.optString("text"), o.optString("status"), o.optString("note"))
    }

private fun etiqueta(status: String) = when (status) {
    "nueva" -> "Por hacer"
    "en_curso" -> "Tu Claude la está haciendo…"
    "con_preguntas" -> "Tu Claude tiene una pregunta"
    "lista" -> "Hecha: falta instalar la versión nueva"
    "hecha" -> "Instalada"
    "con_problema" -> "No se pudo"
    "descartada" -> "Descartada"
    else -> status
}

private fun colorDe(status: String): Color = when (status) {
    "nueva" -> Neon.Cyan
    "en_curso" -> Neon.Violet
    "con_preguntas" -> Neon.Amber
    "lista" -> Neon.Lime
    "hecha" -> Neon.TextFaint
    "con_problema" -> Neon.Red
    else -> Neon.TextFaint
}

/** Pide mejoras a tu Claude: una lista; después eliges una o todas y él las programa, arma la versión nueva y la instala. */
@Composable
fun MejorasScreen() {
    val scope = rememberCoroutineScope()
    var items by remember { mutableStateOf<List<Mejora>>(emptyList()) }
    var texto by remember { mutableStateOf("") }
    var loaded by remember { mutableStateOf(false) }
    val run by Pc.mejorasRun.collectAsState()
    val respuestas = remember { mutableStateListOf<Pair<String, String>>() }
    var confirmarDeshacer by remember { mutableStateOf<Mejora?>(null) }

    suspend fun cargar() {
        try {
            items = Pc.call("mejoras.list").optJSONArray("mejoras").toMejoras()
            Pc.mejorasRun.value = Pc.call("mejoras.run.status")
            loaded = true
        } catch (_: Exception) { }
    }

    LaunchedEffect(Unit) {
        cargar()
        // Se actualiza cuando el PC avisa y, por si algún aviso se pierde, cada 5 segundos
        while (true) {
            delay(5_000)
            cargar()
        }
    }
    LaunchedEffect(Unit) {
        Pc.mejorasChanged.collect { cargar() }
    }

    val voz = rememberVoiceInput("¿Qué quieres mejorar?") { texto = (texto + " " + it).trim() }

    fun llamar(method: String, params: JSONObject = JSONObject(), timeoutMs: Long = 15_000, despues: () -> Unit = {}) {
        scope.pcCall(method, params, timeoutMs) { despues(); scope.launch { cargar() } }
    }

    val estado = run?.optString("state").orEmpty()
    val ocupado = estado == "trabajando" || estado == "compilando"
    val nuevas = items.count { it.status == "nueva" }

    LazyColumn(
        contentPadding = PaddingValues(start = 16.dp, end = 16.dp, bottom = 24.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        // ---------- escribir una petición ----------
        item {
            GlassCard(Modifier.fillMaxWidth(), glow = Neon.Violet) {
                Text("¿Qué quieres mejorar?", style = MaterialTheme.typography.titleMedium, color = Neon.Text)
                Text(
                    "Cuéntalo con tus palabras. Se agrega a la lista y después tú decides cuándo lo hace tu Claude.",
                    style = MaterialTheme.typography.bodySmall, color = Neon.TextDim,
                )
                Spacer(Modifier.height(8.dp))
                OutlinedTextField(
                    value = texto,
                    onValueChange = { texto = it },
                    modifier = Modifier.fillMaxWidth(),
                    placeholder = { Text("Ej.: en Excel, un botón para ordenar la columna de menor a mayor") },
                    minLines = 2,
                    maxLines = 6,
                    trailingIcon = {
                        IconButton(onClick = voz) { Icon(Icons.Filled.Mic, contentDescription = "Dictar", tint = Neon.Violet) }
                    },
                )
                Spacer(Modifier.height(8.dp))
                NeonButton("Agregar a la lista", Modifier.fillMaxWidth(), accent = Neon.Violet, enabled = texto.isNotBlank()) {
                    val t = texto
                    llamar("mejoras.add", JSONObject().put("text", t)) { texto = "" }
                }
            }
        }

        // ---------- lo que está pasando ----------
        if (estado == "trabajando" || estado == "compilando") {
            item {
                GlassCard(Modifier.fillMaxWidth(), glow = Neon.Lime) {
                    val total = run?.optInt("total") ?: 0
                    val hechas = run?.optInt("done") ?: 0
                    Text(
                        if (estado == "compilando") "Armando la versión nueva…" else "Tu Claude está trabajando…",
                        style = MaterialTheme.typography.titleMedium, color = Neon.Text,
                    )
                    Text(run?.optString("step").orEmpty(), style = MaterialTheme.typography.bodySmall, color = Neon.TextDim)
                    if (total > 0) Text("Mejoras terminadas: $hechas de $total", style = MaterialTheme.typography.labelSmall, color = Neon.Lime)
                    val log = run?.optJSONArray("log")
                    if (log != null && log.length() > 0) {
                        Spacer(Modifier.height(6.dp))
                        for (i in maxOf(0, log.length() - 6) until log.length())
                            Text(log.optString(i), style = MaterialTheme.typography.labelSmall, color = Neon.TextFaint, maxLines = 2)
                    }
                    Spacer(Modifier.height(8.dp))
                    NeonButton("Cancelar", Modifier.fillMaxWidth(), outlined = true, accent = Neon.Red) { llamar("mejoras.run.cancel") }
                }
            }
        }
        if (estado == "listo") {
            item {
                GlassCard(Modifier.fillMaxWidth(), glow = Neon.Lime) {
                    Text("La versión ${run?.optString("version")} está lista", style = MaterialTheme.typography.titleMedium, color = Neon.Text)
                    Text(
                        if (run?.optBoolean("prueba") == true) "Estás en una sesión de prueba: aquí no se instala."
                        else "PC Hub se reinicia solo (unos segundos). Cuando el teléfono se reconecte, toca «Actualizar la app ahora» en Inicio.",
                        style = MaterialTheme.typography.bodySmall, color = Neon.TextDim,
                    )
                    Spacer(Modifier.height(8.dp))
                    NeonButton("Instalar la versión nueva", Modifier.fillMaxWidth(), accent = Neon.Lime) { llamar("mejoras.install", timeoutMs = 30_000) }
                }
            }
        }
        if (estado == "error") {
            item {
                GlassCard(Modifier.fillMaxWidth(), glow = Neon.Red) {
                    Text("Algo no salió bien", style = MaterialTheme.typography.titleMedium, color = Neon.Red)
                    Text(run?.optString("error").orEmpty(), style = MaterialTheme.typography.bodySmall, color = Neon.Text)
                    Spacer(Modifier.height(8.dp))
                    NeonButton("Entendido", Modifier.fillMaxWidth(), outlined = true) { llamar("mejoras.run.clear") }
                }
            }
        }

        if (!ocupado && estado != "listo") {
            item {
                TextButton(onClick = { llamar("mejoras.sync", timeoutMs = 20_000) }) {
                    Text("Actualizar desde el repositorio", color = Neon.TextDim)
                }
            }
        }

        // ---------- la lista ----------
        item {
            SectionTitle("Tu lista (${items.count { it.status != "hecha" && it.status != "descartada" }})") {
                if (nuevas > 0) {
                    NeonButton("Hacer todas ($nuevas)", accent = Neon.Lime, enabled = !ocupado) {
                        llamar("mejoras.run.start", timeoutMs = 20_000)
                    }
                }
            }
        }
        if (loaded && items.isEmpty()) {
            item { Text("Todavía no hay nada. Escribe arriba lo primero que quieras cambiar.", color = Neon.TextDim, style = MaterialTheme.typography.bodySmall) }
        }
        items(items, key = { it.id }) { m ->
            GlassCard(Modifier.fillMaxWidth()) {
                Text(etiqueta(m.status), style = MaterialTheme.typography.labelMedium, color = colorDe(m.status))
                Text(m.text, style = MaterialTheme.typography.bodyMedium, color = Neon.Text)
                if (m.note.isNotBlank()) Text(m.note, style = MaterialTheme.typography.bodySmall, color = Neon.TextDim)
                when (m.status) {
                    "nueva", "con_problema" -> {
                        Spacer(Modifier.height(8.dp))
                        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            NeonButton(if (m.status == "nueva") "Hacer esta" else "Reintentar", Modifier.weight(1f), accent = Neon.Lime, enabled = !ocupado) {
                                llamar("mejoras.run.start", JSONObject().put("ids", JSONArray().put(m.id)), timeoutMs = 20_000)
                            }
                            NeonButton("Quitar", Modifier.weight(1f), outlined = true, accent = Neon.Red, enabled = !ocupado) {
                                llamar("mejoras.delete", JSONObject().put("id", m.id))
                            }
                        }
                    }
                    "con_preguntas" -> {
                        val actual = respuestas.firstOrNull { it.first == m.id }?.second.orEmpty()
                        Spacer(Modifier.height(8.dp))
                        OutlinedTextField(
                            value = actual,
                            onValueChange = { v ->
                                respuestas.removeAll { it.first == m.id }
                                respuestas.add(m.id to v)
                            },
                            modifier = Modifier.fillMaxWidth(),
                            placeholder = { Text("Tu respuesta") },
                            shape = RoundedCornerShape(20.dp),
                        )
                        Spacer(Modifier.height(6.dp))
                        NeonButton("Responder", Modifier.fillMaxWidth(), accent = Neon.Amber, enabled = actual.isNotBlank()) {
                            llamar("mejoras.answer", JSONObject().put("id", m.id).put("answers", actual)) { respuestas.removeAll { it.first == m.id } }
                        }
                    }
                    "lista", "hecha" -> {
                        Spacer(Modifier.height(6.dp))
                        TextButton(onClick = { confirmarDeshacer = m }, enabled = !ocupado) { Text("Deshacer esta mejora", color = Neon.Red) }
                    }
                    "descartada" -> {
                        TextButton(onClick = { llamar("mejoras.delete", JSONObject().put("id", m.id)) }) { Text("Quitar de la lista", color = Neon.TextDim) }
                    }
                }
            }
        }
    }

    confirmarDeshacer?.let { m ->
        AlertDialog(
            onDismissRequest = { confirmarDeshacer = null },
            title = { Text("¿Deshacer esta mejora?") },
            text = { Text("Se quita este cambio y se arma una versión nueva sin él.\n\n«${m.text}»") },
            confirmButton = {
                TextButton(onClick = {
                    confirmarDeshacer = null
                    llamar("mejoras.undo", JSONObject().put("id", m.id), timeoutMs = 30_000)
                }) { Text("Deshacer") }
            },
            dismissButton = { TextButton(onClick = { confirmarDeshacer = null }) { Text("Cancelar") } },
        )
    }
}
