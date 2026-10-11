package com.chacho.pchub.net

import android.content.Context
import android.net.wifi.WifiManager
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.SocketTimeoutException

data class FoundPc(val name: String, val host: String, val port: Int)

/** Busca agentes de PC Hub en la red local enviando "PCHUB?" por UDP broadcast. */
object Discovery {
    private const val PORT = 47801

    suspend fun search(context: Context, timeoutMs: Long = 2000): List<FoundPc> = withContext(Dispatchers.IO) {
        val found = LinkedHashMap<String, FoundPc>()
        val wifi = context.applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager
        val lock = wifi.createMulticastLock("pchub-discovery").apply {
            setReferenceCounted(false)
            acquire()
        }
        try {
            DatagramSocket().use { socket ->
                socket.broadcast = true
                socket.soTimeout = 250
                val msg = "PCHUB?".toByteArray()
                val targets = buildList {
                    add(InetAddress.getByName("255.255.255.255"))
                    subnetBroadcast(wifi)?.let { add(it) }
                }
                val buf = ByteArray(2048)
                val deadline = System.currentTimeMillis() + timeoutMs
                var lastSend = 0L
                while (System.currentTimeMillis() < deadline) {
                    val now = System.currentTimeMillis()
                    if (now - lastSend > 600) {
                        for (t in targets) runCatching { socket.send(DatagramPacket(msg, msg.size, t, PORT)) }
                        lastSend = now
                    }
                    val packet = DatagramPacket(buf, buf.size)
                    try {
                        socket.receive(packet)
                        val json = JSONObject(String(packet.data, 0, packet.length, Charsets.UTF_8))
                        val host = packet.address.hostAddress ?: continue
                        found[host] = FoundPc(json.optString("name", host), host, json.optInt("port", 47800))
                    } catch (_: SocketTimeoutException) {
                    } catch (_: Exception) {
                    }
                }
            }
        } finally {
            lock.release()
        }
        found.values.toList()
    }

    @Suppress("DEPRECATION")
    private fun subnetBroadcast(wifi: WifiManager): InetAddress? {
        val dhcp = wifi.dhcpInfo ?: return null
        if (dhcp.ipAddress == 0 || dhcp.netmask == 0) return null
        val bc = (dhcp.ipAddress and dhcp.netmask) or dhcp.netmask.inv()
        val bytes = ByteArray(4) { k -> ((bc shr (k * 8)) and 0xFF).toByte() }
        return runCatching { InetAddress.getByAddress(bytes) }.getOrNull()
    }
}
