package com.chacho.pchub.ui

import android.content.ActivityNotFoundException
import android.content.Context
import android.content.Intent
import android.graphics.BitmapFactory
import android.os.Handler
import android.os.Looper
import android.speech.RecognizerIntent
import android.util.Base64
import android.widget.Toast
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.chacho.pchub.Prefs
import com.chacho.pchub.net.Pc
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.launch
import org.json.JSONObject
import java.util.Locale

/** Toasts desde cualquier hilo. */
object Toaster {
    private lateinit var app: Context
    private val main = Handler(Looper.getMainLooper())

    fun init(context: Context) {
        app = context.applicationContext
    }

    fun show(msg: String) {
        if (!::app.isInitialized) return
        main.post { Toast.makeText(app, msg, Toast.LENGTH_SHORT).show() }
    }
}

/** Devuelve el string o null si la clave falta o es JSON null. */
fun JSONObject.str(key: String): String? =
    if (!has(key) || isNull(key)) null else optString(key).takeIf { it.isNotEmpty() }

/** Llama al PC desde una corrutina de la UI; muestra un toast si falla. */
fun CoroutineScope.pcCall(
    method: String,
    params: JSONObject = JSONObject(),
    timeoutMs: Long = 10_000,
    onOk: (JSONObject) -> Unit = {},
) = launch {
    try {
        onOk(Pc.call(method, params, timeoutMs))
    } catch (e: Exception) {
        Toaster.show(e.message ?: "Error")
    }
}

@Composable
fun rememberBase64Image(b64: String?): ImageBitmap? = remember(b64) {
    if (b64.isNullOrEmpty()) null
    else runCatching {
        val bytes = Base64.decode(b64, Base64.DEFAULT)
        BitmapFactory.decodeByteArray(bytes, 0, bytes.size)?.asImageBitmap()
    }.getOrNull()
}

/** Vibración corta (respeta el ajuste de háptica). */
@Composable
fun rememberHaptic(): (Boolean) -> Unit {
    val haptic = LocalHapticFeedback.current
    return remember(haptic) {
        { strong: Boolean ->
            if (Prefs.haptics) haptic.performHapticFeedback(
                if (strong) HapticFeedbackType.LongPress else HapticFeedbackType.TextHandleMove
            )
        }
    }
}

/** Abre el reconocimiento de voz del sistema y entrega el texto reconocido. */
@Composable
fun rememberVoiceInput(prompt: String = "¿Qué hago en el PC?", onResult: (String) -> Unit): () -> Unit {
    val launcher = rememberLauncherForActivityResult(ActivityResultContracts.StartActivityForResult()) { result ->
        val text = result.data?.getStringArrayListExtra(RecognizerIntent.EXTRA_RESULTS)?.firstOrNull()
        if (!text.isNullOrBlank()) onResult(text)
    }
    return {
        val intent = Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH).apply {
            putExtra(RecognizerIntent.EXTRA_LANGUAGE_MODEL, RecognizerIntent.LANGUAGE_MODEL_FREE_FORM)
            putExtra(RecognizerIntent.EXTRA_LANGUAGE, Locale.getDefault().toLanguageTag())
            putExtra(RecognizerIntent.EXTRA_PROMPT, prompt)
        }
        try {
            launcher.launch(intent)
        } catch (_: ActivityNotFoundException) {
            Toaster.show("Este teléfono no tiene reconocimiento de voz instalado")
        }
    }
}

data class QuickKey(val label: String, val combo: String)

val DefaultQuickKeys = listOf(
    QuickKey("Win", "win"),
    QuickKey("Alt+Tab", "alt+tab"),
    QuickKey("Esc", "esc"),
    QuickKey("Tab", "tab"),
    QuickKey("Enter", "enter"),
    QuickKey("Copiar", "ctrl+c"),
    QuickKey("Pegar", "ctrl+v"),
    QuickKey("Deshacer", "ctrl+z"),
    QuickKey("Escritorio", "win+d"),
    QuickKey("Captura", "win+shift+s"),
    QuickKey("Alt+F4", "alt+f4"),
)

/** Pulsa una combinación en el PC; los botones de "Captura" abren primero el selector de captura. */
fun pressShortcutKey(combo: String) {
    if (combo.equals("win+shift+s", ignoreCase = true)) CaptureUi.open() else Pc.key(combo)
}

/** Fila deslizable de atajos de teclado (píldoras de vidrio). */
@Composable
fun QuickKeyRow(keys: List<QuickKey> = DefaultQuickKeys, modifier: Modifier = Modifier) {
    val haptic = rememberHaptic()
    Row(
        modifier
            .fillMaxWidth()
            .horizontalScroll(rememberScrollState()),
        horizontalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Spacer(Modifier.size(2.dp))
        keys.forEach { k ->
            Text(
                k.label,
                color = Neon.Text,
                fontWeight = FontWeight.Medium,
                modifier = Modifier
                    .glass(RoundedCornerShape(50))
                    .clickable { haptic(false); pressShortcutKey(k.combo) }
                    .padding(horizontal = 14.dp, vertical = 9.dp),
            )
        }
        Spacer(Modifier.size(2.dp))
    }
}
