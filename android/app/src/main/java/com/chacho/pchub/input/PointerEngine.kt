package com.chacho.pchub.input

import android.content.Context
import android.hardware.Sensor
import android.hardware.SensorEvent
import android.hardware.SensorEventListener
import android.hardware.SensorManager
import android.os.Handler
import android.os.HandlerThread
import com.chacho.pchub.Prefs
import com.chacho.pchub.net.FastInput
import kotlin.math.PI
import kotlin.math.abs
import kotlin.math.asin
import kotlin.math.atan2
import kotlin.math.sqrt

/**
 * Filtro 1€ (Casiez et al., 2012): suaviza mucho cuando el movimiento es lento (quita el temblor de la mano)
 * y casi nada cuando es rápido (sin retraso). Es el estándar para punteros.
 */
class OneEuroFilter(var minCutoff: Double, var beta: Double, private val dCutoff: Double = 1.0) {
    private var x: Double? = null
    private var dx = 0.0
    private var lastT = 0.0

    private fun alpha(cutoff: Double, dt: Double): Double {
        val tau = 1.0 / (2 * PI * cutoff)
        return 1.0 / (1.0 + tau / dt)
    }

    fun reset(value: Double? = null) {
        x = value
        dx = 0.0
        lastT = 0.0
    }

    fun filter(value: Double, tSeconds: Double): Double {
        val prev = x
        if (prev == null || lastT == 0.0) {
            x = value
            lastT = tSeconds
            return value
        }
        val dt = (tSeconds - lastT).coerceIn(1e-4, 0.1)
        lastT = tSeconds
        val rawDx = (value - prev) / dt
        dx += alpha(dCutoff, dt) * (rawDx - dx)
        val cutoff = minCutoff + beta * abs(dx)
        val out = prev + alpha(cutoff, dt) * (value - prev)
        x = out
        return out
    }
}

/**
 * Motor del puntero con los sensores del teléfono. Corre en un hilo propio a ~200 Hz y manda el movimiento
 * por el canal rápido (UDP).
 *
 * Modo "Aire" (relativo, como un mouse aéreo):
 *   Usa el giroscopio, pero mide el giro respecto de la GRAVEDAD y no de los ejes del teléfono: girar a los lados
 *   siempre mueve en horizontal aunque tengas la muñeca inclinada. Lleva una curva de precisión (lento = fino,
 *   rápido = cruza la pantalla) y el filtro 1€ contra el temblor.
 *
 * Modo "Wii" (absoluto):
 *   Usa la orientación del teléfono (vector de rotación, sin brújula) para que el puntero esté donde apuntas.
 *   "Centrar" fija hacia dónde está el centro de la pantalla; si te sales por un borde, el centro se corre solo.
 *   Al volver a apuntar (soltar y mantener B) el puntero sigue desde donde quedó, sin saltar.
 */
class PointerEngine(context: Context) : SensorEventListener {
    enum class Mode { Air, Wii }

    private val sm = context.getSystemService(Context.SENSOR_SERVICE) as SensorManager
    private val gyro: Sensor? = sm.getDefaultSensor(Sensor.TYPE_GYROSCOPE)
    private val gravity: Sensor? = sm.getDefaultSensor(Sensor.TYPE_GRAVITY)
    private val rotation: Sensor? = sm.getDefaultSensor(Sensor.TYPE_GAME_ROTATION_VECTOR)

    private val thread = HandlerThread("pchub-pointer").apply { start() }
    private val handler = Handler(thread.looper)

    val hasGyro get() = gyro != null
    val hasWii get() = rotation != null

    @Volatile var mode: Mode = Mode.Air
        set(v) {
            if (v == field) return
            field = v
            handler.post { resetState() }
        }

    @Volatile var aiming = false
        set(v) {
            if (v && !field) handler.post { resetState() }
            field = v
        }

    /**
     * Mientras es true, inclinar el teléfono arriba/abajo hace scroll y el puntero se queda quieto
     * (botón de scroll mantenido con "gestos con el movimiento").
     */
    @Volatile var scrollMode = false
        set(v) {
            if (v != field) handler.post { scrollAcc = 0.0; lastScrollTs = 0L; if (!v) { lastGyroTs = 0L; anchorToCursor = true } }
            field = v
        }

    /** Posición del puntero en modo Wii (0..1), para dibujarla en la pantalla del teléfono. */
    @Volatile var cursorX = 0.5f
        private set
    @Volatile var cursorY = 0.5f
        private set

    // --- estado (solo se toca desde el hilo del motor) ---
    private val up = FloatArray(3)
    private var hasUp = false
    private var lastGyroTs = 0L

    private var virtX = 0.0
    private var virtY = 0.0
    private var sentX = 0.0
    private var sentY = 0.0
    private val fx = OneEuroFilter(1.0, 0.004)
    private val fy = OneEuroFilter(1.0, 0.004)

    private var needCenter = true
    private var anchorToCursor = false
    private var yaw0 = 0.0
    private var pitch0 = 0.0
    private val rot = FloatArray(9)
    private val wx = OneEuroFilter(1.5, 1.5)
    private val wy = OneEuroFilter(1.5, 1.5)
    private var lastAbsSend = 0L
    private var scrollAcc = 0.0
    private var lastScrollTs = 0L
    private var lastAbsX = -1f
    private var lastAbsY = -1f

    fun start() {
        // 5000 µs ≈ 200 Hz. El hilo propio evita que la UI frene a los sensores.
        gyro?.let { sm.registerListener(this, it, 5_000, handler) }
        gravity?.let { sm.registerListener(this, it, 10_000, handler) }
        rotation?.let { sm.registerListener(this, it, 5_000, handler) }
    }

    fun stop() {
        sm.unregisterListener(this)
        thread.quitSafely()
    }

    /** Modo Wii: la dirección actual pasa a ser el centro de la pantalla. */
    fun recenter() = handler.post { needCenter = true }

    private fun resetState() {
        lastGyroTs = 0L
        virtX = 0.0; virtY = 0.0; sentX = 0.0; sentY = 0.0
        fx.reset(0.0); fy.reset(0.0)
        wx.reset(); wy.reset()
        anchorToCursor = true
    }

    override fun onAccuracyChanged(sensor: Sensor?, accuracy: Int) = Unit

    override fun onSensorChanged(e: SensorEvent) {
        when (e.sensor.type) {
            Sensor.TYPE_GRAVITY -> {
                val n = sqrt(e.values[0] * e.values[0] + e.values[1] * e.values[1] + e.values[2] * e.values[2])
                if (n > 1f) {
                    up[0] = e.values[0] / n; up[1] = e.values[1] / n; up[2] = e.values[2] / n
                    hasUp = true
                }
            }
            Sensor.TYPE_GYROSCOPE -> when {
                scrollMode -> onScrollGyro(e)
                mode == Mode.Air || rotation == null -> onGyro(e)
            }
            Sensor.TYPE_GAME_ROTATION_VECTOR -> if (mode == Mode.Wii && !scrollMode) onRotation(e)
        }
    }

    // ------------------------------------------------------------------ modo Aire

    /** Dirección a la que "apunta" el teléfono, en sus propios ejes. */
    private fun pointing(): FloatArray =
        if (Prefs.pointerUpright) floatArrayOf(0f, 0f, -1f) // de pie: apunta la parte de atrás (cámara)
        else floatArrayOf(0f, 1f, 0f)                        // acostado: apunta el borde de arriba

    private fun onGyro(e: SensorEvent) {
        val ts = e.timestamp
        val dt = if (lastGyroTs == 0L) 0.0 else (ts - lastGyroTs) / 1e9
        lastGyroTs = ts
        if (!aiming || dt <= 0.0 || dt > 0.05) return

        val w0 = e.values[0]; val w1 = e.values[1]; val w2 = e.values[2]
        val p = pointing()

        // Giro alrededor de la vertical real (yaw) y del eje horizontal perpendicular a donde apuntas (pitch)
        var yawRate: Double
        var pitchRate: Double
        if (hasUp) {
            // r = p × up (eje "derecha" respecto del suelo)
            var rx = p[1] * up[2] - p[2] * up[1]
            var ry = p[2] * up[0] - p[0] * up[2]
            var rz = p[0] * up[1] - p[1] * up[0]
            val rn = sqrt(rx * rx + ry * ry + rz * rz)
            if (rn > 0.35f) {
                rx /= rn; ry /= rn; rz /= rn
                yawRate = (w0 * up[0] + w1 * up[1] + w2 * up[2]).toDouble()
                pitchRate = (w0 * rx + w1 * ry + w2 * rz).toDouble()
            } else {
                // Apuntando casi recto arriba o abajo: no hay "horizontal" clara, se usan los ejes del teléfono
                yawRate = (if (Prefs.pointerUpright) w1 else w2).toDouble()
                pitchRate = w0.toDouble()
            }
        } else {
            yawRate = (if (Prefs.pointerUpright) w1 else w2).toDouble()
            pitchRate = w0.toDouble()
        }

        // Zona muerta suave (ruido del sensor)
        fun soft(v: Double, dz: Double = 0.003) = if (abs(v) <= dz) 0.0 else v - dz * kotlin.math.sign(v)
        yawRate = soft(yawRate)
        pitchRate = soft(pitchRate)

        // Curva de precisión: lento = más fino, rápido = ganancia completa (un poco más)
        val speed = sqrt(yawRate * yawRate + pitchRate * pitchRate)
        val t = smoothstep(0.03, 0.9, speed)
        val low = 1.0 - 0.7 * Prefs.pointerPrecision
        val gain = Prefs.pointerSensitivity * (low + (1.15 - low) * t)

        virtX += -yawRate * dt * gain
        virtY += -pitchRate * dt * gain

        // 1€ sobre la posición virtual en píxeles
        val smooth = Prefs.pointerSmoothing.toDouble()
        val minCut = 3.0 + (0.5 - 3.0) * smooth
        fx.minCutoff = minCut; fy.minCutoff = minCut
        val tSec = ts / 1e9
        val sx = fx.filter(virtX, tSec)
        val sy = fy.filter(virtY, tSec)

        val dx = (sx - sentX).toInt()
        val dy = (sy - sentY).toInt()
        if (dx != 0 || dy != 0) {
            sentX += dx
            sentY += dy
            FastInput.move(dx, dy)
        }
    }

    // ------------------------------------------------------------------ scroll inclinando

    /** Inclinar hacia arriba = subir, hacia abajo = bajar. Más rápido el giro, más rápido el scroll. */
    private fun onScrollGyro(e: SensorEvent) {
        val ts = e.timestamp
        val dt = if (lastScrollTs == 0L) 0.0 else (ts - lastScrollTs) / 1e9
        lastScrollTs = ts
        if (dt <= 0.0 || dt > 0.05) return
        val rate = e.values[0].toDouble() // giro sobre el eje horizontal del teléfono
        if (abs(rate) < 0.02) return
        // ~1 "clic" de rueda (120) por cada 5° girados, con un poco de aceleración
        val gain = 1400.0 * (1.0 + 0.8 * smoothstep(0.3, 2.5, abs(rate)))
        scrollAcc += rate * dt * gain
        val step = scrollAcc.toInt()
        if (abs(step) >= 15) {
            scrollAcc -= step
            FastInput.scroll(step, 0)
        }
    }

    // ------------------------------------------------------------------ modo Wii

    private fun onRotation(e: SensorEvent) {
        if (!aiming) return
        SensorManager.getRotationMatrixFromVector(rot, e.values)
        val p = pointing()
        // Dirección en el mundo (X = este, Y = norte, Z = arriba)
        val px = rot[0] * p[0] + rot[1] * p[1] + rot[2] * p[2]
        val py = rot[3] * p[0] + rot[4] * p[1] + rot[5] * p[2]
        val pz = rot[6] * p[0] + rot[7] * p[1] + rot[8] * p[2]
        val yaw = atan2(px.toDouble(), py.toDouble())
        val pitch = asin(pz.toDouble().coerceIn(-1.0, 1.0))

        val rangeH = Math.toRadians(Prefs.pointerRange.toDouble())
        val rangeV = rangeH * 0.6
        if (needCenter) {
            yaw0 = yaw
            pitch0 = pitch
            needCenter = false
            anchorToCursor = false
            wx.reset(); wy.reset()
        } else if (anchorToCursor) {
            // La dirección actual corresponde a donde quedó el puntero
            yaw0 = yaw - (cursorX - 0.5) * 2 * rangeH
            pitch0 = pitch + (cursorY - 0.5) * 2 * rangeV
            anchorToCursor = false
        }

        var x = 0.5 + wrapPi(yaw - yaw0) / (2 * rangeH)
        var y = 0.5 - (pitch - pitch0) / (2 * rangeV)

        // Si te sales por un borde, el centro se corre contigo (como arrastrar el marco)
        val cx = x.coerceIn(0.0, 1.0)
        val cy = y.coerceIn(0.0, 1.0)
        if (cx != x) yaw0 = yaw - (cx - 0.5) * 2 * rangeH
        if (cy != y) pitch0 = pitch + (cy - 0.5) * 2 * rangeV
        x = cx; y = cy

        val smooth = Prefs.pointerSmoothing.toDouble()
        val minCut = 3.0 + (0.6 - 3.0) * smooth
        wx.minCutoff = minCut; wy.minCutoff = minCut
        val tSec = e.timestamp / 1e9
        val sx = wx.filter(x, tSec).toFloat()
        val sy = wy.filter(y, tSec).toFloat()
        cursorX = sx
        cursorY = sy

        val now = e.timestamp
        if (now - lastAbsSend < 5_000_000L) return // máx. ~200 por segundo
        if (abs(sx - lastAbsX) < 0.00002f && abs(sy - lastAbsY) < 0.00002f) return
        lastAbsSend = now
        lastAbsX = sx
        lastAbsY = sy
        FastInput.absolute(sx, sy)
    }

    private fun wrapPi(a: Double): Double {
        var v = a
        while (v > PI) v -= 2 * PI
        while (v < -PI) v += 2 * PI
        return v
    }

    private fun smoothstep(e0: Double, e1: Double, x: Double): Double {
        val t = ((x - e0) / (e1 - e0)).coerceIn(0.0, 1.0)
        return t * t * (3 - 2 * t)
    }

}
