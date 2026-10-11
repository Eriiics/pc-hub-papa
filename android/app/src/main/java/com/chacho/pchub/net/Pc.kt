package com.chacho.pchub.net

import android.os.Build
import com.chacho.pchub.Prefs
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.withTimeoutOrNull
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import org.json.JSONObject
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicInteger

class PcError(message: String) : Exception(message)

/**
 * Conexión única con el agente del PC (ver PROTOCOL.md).
 * Toda la app comparte este objeto: las pantallas observan sus StateFlow y llaman a send()/call().
 */
object Pc {
    enum class Status { Idle, Connecting, NeedsPin, Connected, Failed }

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val client = OkHttpClient.Builder()
        // Sin el algoritmo de Nagle: cada mensaje sale al tiro en vez de esperar a juntar varios (menos tirones)
        .socketFactory(NoDelaySocketFactory)
        .pingInterval(15, TimeUnit.SECONDS)
        .connectTimeout(4, TimeUnit.SECONDS)
        .readTimeout(0, TimeUnit.MILLISECONDS)
        .build()

    val status = MutableStateFlow(Status.Idle)
    val error = MutableStateFlow<String?>(null)
    val pcName = MutableStateFlow("")

    /** Ventana activa del PC: process, title, module. */
    val context = MutableStateFlow<JSONObject?>(null)
    /** Lo que está sonando en el PC. */
    val media = MutableStateFlow<JSONObject?>(null)
    /** Se emite cuando el foco del PC entra o sale de un campo de texto. */
    val textFocus = MutableSharedFlow<JSONObject>(extraBufferCapacity = 8)
    /** Pasos que va ejecutando la IA. */
    val aiSteps = MutableSharedFlow<String>(extraBufferCapacity = 32)
    /** Confirmación pendiente pedida por el PC (PowerShell, apagar...). */
    val confirm = MutableStateFlow<JSONObject?>(null)
    /** Salida de la terminal que está mirando este teléfono: (pestaña, bytes en base64). */
    val termOut = MutableSharedFlow<Pair<String, String>>(extraBufferCapacity = 1024)
    /** Cambios de una pestaña de terminal: id, title, running, onPc (o closed = true). */
    val termState = MutableSharedFlow<JSONObject>(extraBufferCapacity = 64)

    private var ws: WebSocket? = null
    @Volatile private var generation = 0
    private val nextId = AtomicInteger(1)
    private val pending = ConcurrentHashMap<Int, CompletableDeferred<JSONObject>>()
    @Volatile private var wantConnected = false
    @Volatile private var pendingPin: String? = null
    private var reconnectJob: Job? = null
    private var attempts = 0

    // ------------------------------------------------------------------ conexión

    fun connect(host: String, port: Int, pin: String? = null) {
        Prefs.host = host
        Prefs.port = port
        pendingPin = pin
        wantConnected = true
        attempts = 0
        open()
    }

    /** Reconecta si hay un PC guardado y no hay conexión activa (al volver a la app, por ejemplo). */
    fun ensureConnected() {
        if (Prefs.host.isEmpty() || Prefs.token.isEmpty()) return
        if (status.value == Status.Connected || status.value == Status.Connecting) return
        wantConnected = true
        attempts = 0
        open()
    }

    fun disconnect() {
        FastInput.close()
        wantConnected = false
        reconnectJob?.cancel()
        generation++
        ws?.close(1000, "bye")
        ws = null
        failPending()
        status.value = Status.Idle
    }

    fun forget() {
        disconnect()
        Prefs.forgetPc()
        pcName.value = ""
        context.value = null
        media.value = null
    }

    fun pair(pin: String) {
        pendingPin = pin
        val gen = generation
        if (ws == null) open() else scope.launch { authenticate(gen) }
    }

    private fun open() {
        reconnectJob?.cancel()
        ws?.cancel()
        val gen = ++generation
        status.value = Status.Connecting
        val request = Request.Builder().url("ws://${Prefs.host}:${Prefs.port}/ws").build()
        ws = client.newWebSocket(request, object : WebSocketListener() {
            override fun onOpen(webSocket: WebSocket, response: Response) {
                if (gen != generation) return
                attempts = 0
                scope.launch { authenticate(gen) }
            }

            override fun onMessage(webSocket: WebSocket, text: String) {
                if (gen != generation) return
                handle(text)
            }

            override fun onClosed(webSocket: WebSocket, code: Int, reason: String) = lost(gen, null)

            override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) =
                lost(gen, t.message ?: t.javaClass.simpleName)
        })
    }

    private fun lost(gen: Int, reason: String?) {
        if (gen != generation) return
        FastInput.close()
        ws = null
        failPending()
        if (!wantConnected) {
            status.value = Status.Idle
            return
        }
        attempts++
        val hasToken = Prefs.token.isNotEmpty()
        error.value = if (reason != null) "No se pudo conectar con ${Prefs.host}: $reason" else "Conexión cerrada"
        if (!hasToken || attempts > 20) {
            status.value = Status.Failed
            return
        }
        status.value = Status.Connecting
        reconnectJob = scope.launch {
            delay(minOf(10_000L, 600L * attempts))
            if (wantConnected && gen == generation) open()
        }
    }

    private suspend fun authenticate(gen: Int) {
        val pin = pendingPin
        try {
            when {
                pin != null -> {
                    val d = call("auth.pair", JSONObject().put("pin", pin).put("device", deviceName()))
                    Prefs.token = d.getString("token")
                    pendingPin = null
                    onAuthenticated(d)
                }
                Prefs.token.isNotEmpty() -> {
                    val d = call("auth.hello", JSONObject().put("token", Prefs.token))
                    onAuthenticated(d)
                }
                else -> if (gen == generation) status.value = Status.NeedsPin
            }
        } catch (e: Exception) {
            if (gen != generation) return
            when {
                e.message == "token_invalido" -> {
                    Prefs.token = ""
                    error.value = "El PC olvidó este teléfono. Ingresa el PIN de nuevo."
                    status.value = Status.NeedsPin
                }
                pin != null -> {
                    pendingPin = null
                    error.value = e.message
                    status.value = Status.NeedsPin
                }
                else -> {
                    error.value = e.message
                    status.value = Status.Failed
                }
            }
        }
    }

    private fun onAuthenticated(d: JSONObject) {
        val name = d.optString("pcName", Prefs.host)
        pcName.value = name
        Prefs.pcName = name
        error.value = null
        status.value = Status.Connected
        scope.launch {
            FastInput.setup(Prefs.host)
        }
    }

    private fun deviceName(): String =
        "${Build.MANUFACTURER.replaceFirstChar { it.uppercase() }} ${Build.MODEL}"

    // ------------------------------------------------------------------ mensajes

    private fun handle(text: String) {
        val msg = try { JSONObject(text) } catch (_: Exception) { return }
        when (msg.optString("t")) {
            "res" -> pending.remove(msg.optInt("id"))?.complete(msg)
            "evt" -> {
                val d = msg.optJSONObject("d") ?: JSONObject()
                when (msg.optString("e")) {
                    "ctx.changed" -> context.value = d
                    "media.state" -> {
                        // Si no viene la carátula es porque no cambió: se conserva la anterior
                        val prev = media.value
                        if (!d.has("art") && prev != null && prev.has("art")) d.put("art", prev.opt("art"))
                        media.value = d
                    }
                    "input.focus" -> textFocus.tryEmit(d)
                    "ai.step" -> aiSteps.tryEmit(d.optString("text"))
                    "ai.confirm_req" -> confirm.value = d
                    "term.out" -> termOut.tryEmit(d.optString("id") to d.optString("data"))
                    "term.state" -> termState.tryEmit(d)
                    "term.closed" -> termState.tryEmit(JSONObject().put("id", d.optString("id")).put("closed", true))
                }
            }
        }
    }

    private fun failPending() {
        val all = pending.values.toList()
        pending.clear()
        all.forEach { it.complete(JSONObject().put("ok", false).put("err", "Sin conexión con el PC")) }
    }

    /** Envía sin esperar respuesta (mouse, teclas). Devuelve false si no hay conexión. */
    fun send(method: String, params: JSONObject = JSONObject()): Boolean {
        val socket = ws ?: return false
        return socket.send(JSONObject().put("t", "req").put("m", method).put("p", params).toString())
    }

    /** Envía y espera la respuesta del PC. Lanza PcError si falla. */
    suspend fun call(method: String, params: JSONObject = JSONObject(), timeoutMs: Long = 10_000): JSONObject {
        val socket = ws ?: throw PcError("Sin conexión con el PC")
        val id = nextId.getAndIncrement()
        val deferred = CompletableDeferred<JSONObject>()
        pending[id] = deferred
        val sent = socket.send(JSONObject().put("t", "req").put("id", id).put("m", method).put("p", params).toString())
        if (!sent) {
            pending.remove(id)
            throw PcError("Sin conexión con el PC")
        }
        val res = withTimeoutOrNull(timeoutMs) { deferred.await() }
        if (res == null) {
            pending.remove(id)
            throw PcError("El PC no respondió a tiempo")
        }
        if (res.optBoolean("ok")) return res.optJSONObject("d") ?: JSONObject()
        throw PcError(res.optString("err", "Error desconocido"))
    }

    // ------------------------------------------------------------------ atajos

    fun key(combo: String, repeat: Int = 1) =
        send("kb.key", JSONObject().put("combo", combo).put("repeat", repeat))

    fun text(t: String) = send("kb.text", JSONObject().put("text", t))

    fun click(button: String = "left", double: Boolean = false) =
        send("mouse.click", JSONObject().put("b", button).put("double", double))

    fun answerConfirm(id: String, ok: Boolean) {
        send("ai.confirm", JSONObject().put("id", id).put("ok", ok))
        confirm.value = null
    }
}

/** Fábrica de sockets TCP con TCP_NODELAY (mensajes chicos sin demora). */
private object NoDelaySocketFactory : javax.net.SocketFactory() {
    private val base = javax.net.SocketFactory.getDefault()
    private fun tune(s: java.net.Socket) = s.apply { tcpNoDelay = true }
    override fun createSocket(): java.net.Socket = tune(base.createSocket())
    override fun createSocket(host: String?, port: Int): java.net.Socket = tune(base.createSocket(host, port))
    override fun createSocket(host: String?, port: Int, localHost: java.net.InetAddress?, localPort: Int): java.net.Socket =
        tune(base.createSocket(host, port, localHost, localPort))
    override fun createSocket(host: java.net.InetAddress?, port: Int): java.net.Socket = tune(base.createSocket(host, port))
    override fun createSocket(address: java.net.InetAddress?, port: Int, localAddress: java.net.InetAddress?, localPort: Int): java.net.Socket =
        tune(base.createSocket(address, port, localAddress, localPort))
}
