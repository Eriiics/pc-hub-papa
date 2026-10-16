package com.chacho.pchub.net

import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.provider.Settings
import kotlinx.coroutines.delay

/**
 * Depuración inalámbrica (adb por Wi-Fi) para que el PC instale la app sin cable ni toques.
 * La app la enciende sola si tiene el permiso WRITE_SECURE_SETTINGS, que el PC le da por adb cada vez que la instala
 * ("pm grant"). La primera vez se vincula el teléfono desde el PC (menú del ícono → Instalar en el teléfono por Wi-Fi).
 */
object WirelessDebug {
    private const val KEY = "adb_wifi_enabled"

    fun canToggle(ctx: Context): Boolean =
        ctx.checkSelfPermission("android.permission.WRITE_SECURE_SETTINGS") == PackageManager.PERMISSION_GRANTED

    fun isOn(ctx: Context): Boolean =
        runCatching { Settings.Global.getInt(ctx.contentResolver, KEY, 0) == 1 }.getOrDefault(false)

    /** Enciende la depuración inalámbrica. false si no hay permiso o el sistema no lo dejó. */
    fun enable(ctx: Context): Boolean {
        if (isOn(ctx)) return true
        if (!canToggle(ctx)) return false
        return runCatching { Settings.Global.putInt(ctx.contentResolver, KEY, 1) }.getOrDefault(false)
    }

    /** Abre Opciones de desarrollador (ahí está «Depuración inalámbrica»). */
    fun openSettings(ctx: Context) {
        runCatching {
            ctx.startActivity(Intent(Settings.ACTION_APPLICATION_DEVELOPMENT_SETTINGS).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
        }
    }

    /** ¿Se puede actualizar por Wi-Fi sin tocar nada (depuración ya encendida o encendible por la app)? */
    fun ready(ctx: Context): Boolean = isOn(ctx) || canToggle(ctx)

    /**
     * Enciende la depuración inalámbrica (si se puede) y le pide al PC que instale la app por adb.
     * Devuelve (ok, mensaje). Si sale bien, el teléfono reemplaza la app y la vuelve a abrir solo, así que esta
     * función quizá no llegue a devolver nada.
     */
    suspend fun update(ctx: Context): Pair<Boolean, String> {
        val wasOn = isOn(ctx)
        if (!wasOn && !enable(ctx)) {
            return false to "Para actualizar sin tocar nada hay que vincular el teléfono una vez desde el PC (clic derecho en el ícono de PC Hub → «Instalar en el teléfono por Wi-Fi»)."
        }
        if (!wasOn) delay(3_000) // el teléfono tarda un poco en anunciarse en la red
        val d = Pc.call("phone.install", timeoutMs = 240_000)
        return d.optBoolean("ok") to d.optString("message")
    }
}
