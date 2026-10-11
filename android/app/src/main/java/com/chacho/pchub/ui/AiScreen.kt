package com.chacho.pchub.ui

import androidx.compose.ui.graphics.SolidColor
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.Send
import androidx.compose.material.icons.filled.DeleteSweep
import androidx.compose.material.icons.filled.Mic
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.unit.dp
import com.chacho.pchub.net.Pc
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import org.json.JSONObject

data class ChatMsg(val role: Role, val text: String) {
    enum class Role { User, Ai, Step, Error }
}

/** Conversación con la IA; vive fuera de la pantalla para no perderse al navegar. */
object AiChat {
    val messages = mutableStateListOf<ChatMsg>()
    var busy by mutableStateOf(false)
        private set

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)

    init {
        scope.launch {
            Pc.aiSteps.collect { step -> if (busy) messages.add(ChatMsg(ChatMsg.Role.Step, step)) }
        }
    }

    fun ask(text: String) {
        if (busy || text.isBlank()) return
        messages.add(ChatMsg(ChatMsg.Role.User, text))
        busy = true
        scope.launch {
            try {
                // La IA puede tardar (varios pasos + esperar tu aprobación), por eso el tiempo largo
                val d = Pc.call("ai.ask", JSONObject().put("text", text), timeoutMs = 240_000)
                messages.add(ChatMsg(ChatMsg.Role.Ai, d.optString("reply", "Listo.")))
            } catch (e: Exception) {
                messages.add(ChatMsg(ChatMsg.Role.Error, e.message ?: "Error"))
            } finally {
                busy = false
            }
        }
    }

    fun reset() {
        messages.clear()
        Pc.send("ai.reset")
    }
}

private val Examples = listOf(
    "Pon lofi para estudiar en YouTube",
    "Pon Bohemian Rhapsody en YouTube Music",
    "Sube el volumen al 40%",
    "Abre Discord",
    "¿Qué tengo abierto?",
    "Anota una idea: modo cine que baje las luces",
)

@Composable
fun AiScreen() {
    var input by remember { mutableStateOf("") }
    val listState = rememberLazyListState()

    fun send(text: String = input) {
        if (text.isBlank()) return
        AiChat.ask(text.trim())
        input = ""
    }

    val voice = rememberVoiceInput { send(it) }

    LaunchedEffect(AiChat.messages.size, AiChat.busy) {
        val count = AiChat.messages.size + (if (AiChat.busy) 1 else 0)
        if (count > 0) listState.animateScrollToItem(count - 1)
    }

    Column(Modifier.fillMaxSize()) {
        Box(Modifier.weight(1f)) {
            if (AiChat.messages.isEmpty()) {
                Column(Modifier.fillMaxSize().padding(20.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                    Text(
                        "Pídele cosas a tu PC con tus palabras, escribiendo o con el micrófono.",
                        style = MaterialTheme.typography.bodyMedium,
                        color = Neon.TextDim,
                    )
                    Examples.forEach { ex ->
                        Text(
                            ex,
                            color = Neon.Text,
                            modifier = Modifier
                                .glass(RoundedCornerShape(50))
                                .clickable { send(ex) }
                                .padding(horizontal = 16.dp, vertical = 10.dp),
                        )
                    }
                }
            } else {
                LazyColumn(
                    state = listState,
                    contentPadding = PaddingValues(16.dp),
                    verticalArrangement = Arrangement.spacedBy(8.dp),
                    modifier = Modifier.fillMaxSize(),
                ) {
                    itemsIndexed(AiChat.messages) { _, msg -> ChatBubble(msg) }
                    if (AiChat.busy) {
                        item {
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                CircularProgressIndicator(modifier = Modifier.size(16.dp), strokeWidth = 2.dp, color = Neon.Violet)
                                Text("  Trabajando…", style = MaterialTheme.typography.bodySmall, color = Neon.TextDim)
                            }
                        }
                    }
                }
            }
        }

        Row(
            Modifier
                .fillMaxWidth()
                .padding(horizontal = 14.dp, vertical = 8.dp)
                .glass(RoundedCornerShape(28.dp), glow = Neon.Violet, strong = true)
                .padding(start = 6.dp, end = 6.dp, top = 6.dp, bottom = 6.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            NeonIconButton(Icons.Filled.DeleteSweep, "Nueva conversación", size = 42.dp, accent = Neon.TextDim, enabled = !AiChat.busy) {
                AiChat.reset()
            }
            Spacer(Modifier.width(8.dp))
            Box(Modifier.weight(1f)) {
                if (input.isEmpty()) Text("Escribe una orden…", color = Neon.TextFaint)
                BasicTextField(
                    value = input,
                    onValueChange = { input = it },
                    maxLines = 4,
                    textStyle = MaterialTheme.typography.bodyLarge.copy(color = Neon.Text),
                    cursorBrush = SolidColor(Neon.Violet),
                    keyboardOptions = KeyboardOptions(imeAction = ImeAction.Send),
                    keyboardActions = KeyboardActions(onSend = { send() }),
                    modifier = Modifier.fillMaxWidth(),
                )
            }
            Spacer(Modifier.width(8.dp))
            if (input.isBlank()) {
                NeonIconButton(Icons.Filled.Mic, "Hablar", size = 46.dp, accent = Neon.Violet, filled = true, enabled = !AiChat.busy, onClick = voice)
            } else {
                NeonIconButton(Icons.AutoMirrored.Filled.Send, "Enviar", size = 46.dp, accent = Neon.Cyan, filled = true, enabled = !AiChat.busy) { send() }
            }
        }
    }
}

@Composable
private fun ChatBubble(msg: ChatMsg) {
    when (msg.role) {
        ChatMsg.Role.Step -> Text(
            "▸ ${msg.text}",
            style = MaterialTheme.typography.bodySmall,
            fontStyle = FontStyle.Italic,
            color = Neon.TextFaint,
            modifier = Modifier.padding(start = 8.dp),
        )
        else -> {
            val mine = msg.role == ChatMsg.Role.User
            val accent = when (msg.role) {
                ChatMsg.Role.User -> Neon.Cyan
                ChatMsg.Role.Error -> Neon.Red
                else -> null
            }
            Box(Modifier.fillMaxWidth(), contentAlignment = if (mine) Alignment.CenterEnd else Alignment.CenterStart) {
                Text(
                    msg.text,
                    style = MaterialTheme.typography.bodyMedium,
                    color = Neon.Text,
                    modifier = Modifier
                        .widthIn(max = 320.dp)
                        .glass(RoundedCornerShape(20.dp), tint = accent)
                        .padding(horizontal = 14.dp, vertical = 10.dp),
                )
            }
        }
    }
}
