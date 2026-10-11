package com.chacho.pchub.share

import android.app.Activity
import android.app.AlertDialog
import android.content.Intent
import android.os.Bundle
import com.chacho.pchub.Prefs
import com.chacho.pchub.net.Pc
import com.chacho.pchub.ui.Toaster
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch
import kotlinx.coroutines.withTimeoutOrNull
import org.json.JSONObject

/**
 * "Compartir" → "Reproducir en el PC" desde la app de YouTube o YouTube Music del teléfono.
 * No muestra pantalla: lee el enlace, lo manda al PC (se reproduce en la pestaña de YouTube / YouTube Music
 * que esté abierta) y avisa con un mensaje corto. Te quedas en la app desde la que compartiste.
 */
class ShareActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        Prefs.init(applicationContext)
        Toaster.init(applicationContext)

        val text = if (intent?.action == Intent.ACTION_SEND)
            listOfNotNull(intent.getStringExtra(Intent.EXTRA_TEXT), intent.getStringExtra(Intent.EXTRA_SUBJECT)).joinToString(" ")
        else intent?.dataString
        val link = YouTubeLink.parse(text)

        val otherUrl = if (link == null) YouTubeLink.firstUrl(text) else null
        when {
            link == null && otherUrl == null -> Toaster.show("No encontré un enlace para enviar")
            Prefs.host.isEmpty() || Prefs.token.isEmpty() -> Toaster.show("Primero vincula tu PC en PC Hub")
            link != null -> ShareSender.send(link)
            else -> {
                // Enlace que no es de YouTube: se pregunta, para no pisar lo que esté sonando en la pestaña de YouTube
                AlertDialog.Builder(this)
                    .setTitle("Enviar enlace al PC")
                    .setMessage(otherUrl)
                    .setPositiveButton("Abrir en pestaña nueva") { _, _ -> ShareSender.openUrl(otherUrl!!) }
                    .setNegativeButton("Cancelar", null)
                    .setOnDismissListener { finish() }
                    .show()
                return
            }
        }
        finish()
    }
}

/** Envía en segundo plano (la pantalla de compartir ya se cerró). */
object ShareSender {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    /** Abre un enlace en una pestaña nueva del navegador predeterminado del PC (no toca la pestaña de YouTube). */
    fun openUrl(url: String) {
        Toaster.show("Enviando al PC…")
        scope.launch {
            try {
                Pc.ensureConnected()
                val ok = withTimeoutOrNull(8_000) { Pc.status.first { it == Pc.Status.Connected } }
                if (ok == null) {
                    Toaster.show("No se pudo conectar con el PC")
                    return@launch
                }
                Pc.call("web.open", JSONObject().put("url", url), timeoutMs = 15_000)
                Toaster.show("Abierto en una pestaña nueva del PC")
            } catch (e: Exception) {
                Toaster.show(e.message ?: "No se pudo enviar al PC")
            }
        }
    }

    fun send(link: YouTubeLink) {
        Toaster.show("Enviando al PC…")
        scope.launch {
            try {
                Pc.ensureConnected()
                val ok = withTimeoutOrNull(8_000) { Pc.status.first { it == Pc.Status.Connected } }
                if (ok == null) {
                    Toaster.show("No se pudo conectar con el PC")
                    return@launch
                }
                val d = Pc.call(
                    "yt.play",
                    JSONObject().put("id", link.id).put("list", link.list).put("music", link.music),
                    timeoutMs = 15_000,
                )
                Toaster.show(d.optString("message", "Reproduciendo en el PC"))
            } catch (e: Exception) {
                Toaster.show(e.message ?: "No se pudo enviar al PC")
            }
        }
    }
}
