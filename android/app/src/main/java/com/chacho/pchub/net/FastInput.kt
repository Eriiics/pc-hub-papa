package com.chacho.pchub.net

import android.os.Handler
import android.os.HandlerThread
import org.json.JSONObject
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.nio.ByteBuffer
import java.nio.ByteOrder

/**
 * Canal rápido de mouse por UDP (ver UdpInput.cs en el agente). Mover el mouse manda muchos paquetes chicos por
 * segundo: por UDP un paquete perdido en el Wi-Fi no frena a los siguientes, y no pasa por JSON.
 * Si el PC no ofrece el canal, todo cae al WebSocket de siempre.
 *
 * Todos los envíos van a un hilo propio (la red no se puede usar desde el hilo de la UI).
 */
object FastInput {
    private val thread = HandlerThread("pchub-fastinput").apply { start() }
    val handler = Handler(thread.looper)

    @Volatile private var socket: DatagramSocket? = null
    @Volatile private var address: InetAddress? = null
    @Volatile private var port = 0
    @Volatile private var key = 0L
    private var seq = 0

    val available: Boolean get() = socket != null

    /** Se llama al autenticarse: pide la clave al PC y abre el socket. */
    suspend fun setup(host: String) {
        close()
        try {
            val d = Pc.call("input.udp", timeoutMs = 5_000)
            val p = d.optInt("port")
            val k = java.lang.Long.parseUnsignedLong(d.optString("key"), 16)
            val addr = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) { InetAddress.getByName(host) }
            handler.post {
                runCatching {
                    socket = DatagramSocket()
                    address = addr
                    port = p
                    key = k
                }
            }
        } catch (_: Exception) {
            // PC con versión anterior o puerto bloqueado: se sigue usando el WebSocket
        }
    }

    fun close() {
        handler.post {
            runCatching { socket?.close() }
            socket = null
        }
    }

    private fun packet(type: Int, a: Int, b: Int): ByteArray {
        val buf = ByteBuffer.allocate(15).order(ByteOrder.LITTLE_ENDIAN)
        buf.putLong(key)
        buf.put(type.toByte())
        buf.putShort((seq++ and 0xFFFF).toShort())
        buf.putShort(a.toShort())
        buf.putShort(b.toShort())
        return buf.array()
    }

    /** Debe llamarse desde el hilo de FastInput (o con [post]). */
    private fun sendNow(type: Int, a: Int, b: Int): Boolean {
        val s = socket ?: return false
        val addr = address ?: return false
        return try {
            val bytes = packet(type, a, b)
            s.send(DatagramPacket(bytes, bytes.size, addr, port))
            true
        } catch (_: Exception) {
            false
        }
    }

    private fun clampShort(v: Int) = v.coerceIn(-32768, 32767)

    /** Movimiento relativo. Seguro desde cualquier hilo. */
    fun move(dx: Int, dy: Int) {
        if (dx == 0 && dy == 0) return
        if (socket == null) {
            Pc.send("mouse.move", JSONObject().put("dx", dx).put("dy", dy))
            return
        }
        handler.post {
            if (!sendNow(1, clampShort(dx), clampShort(dy))) Pc.send("mouse.move", JSONObject().put("dx", dx).put("dy", dy))
        }
    }

    fun scroll(dy: Int, dx: Int) {
        if (dx == 0 && dy == 0) return
        if (socket == null) {
            Pc.send("mouse.scroll", JSONObject().put("dy", dy).put("dx", dx))
            return
        }
        handler.post {
            if (!sendNow(2, clampShort(dy), clampShort(dx))) Pc.send("mouse.scroll", JSONObject().put("dy", dy).put("dx", dx))
        }
    }

    /** Posición absoluta: x, y entre 0 y 1 sobre la pantalla principal del PC (modo Wii). */
    fun absolute(x: Float, y: Float) {
        val ix = (x.coerceIn(0f, 1f) * 65535f).toInt()
        val iy = (y.coerceIn(0f, 1f) * 65535f).toInt()
        if (socket == null) {
            Pc.send("mouse.abs", JSONObject().put("x", x.toDouble()).put("y", y.toDouble()))
            return
        }
        handler.post {
            // uint16 en el PC: los valores > 32767 viajan con el mismo patrón de bits
            if (!sendNow(3, ix, iy)) Pc.send("mouse.abs", JSONObject().put("x", x.toDouble()).put("y", y.toDouble()))
        }
    }
}
