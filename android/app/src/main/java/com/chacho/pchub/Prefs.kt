package com.chacho.pchub

import android.content.Context
import android.content.SharedPreferences
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import org.json.JSONArray
import org.json.JSONObject

/** Ajustes persistentes. Los valores observables (mutableStateOf) actualizan la UI al cambiar. */
object Prefs {
    private lateinit var sp: SharedPreferences

    fun init(context: Context) {
        sp = context.getSharedPreferences("pchub", Context.MODE_PRIVATE)
        autoKeyboard = sp.getBoolean("autoKeyboard", true)
        autoHideKeyboard = sp.getBoolean("autoHideKeyboard", false)
        autoSwitch = sp.getBoolean("autoSwitch", true)
        autoSwitchLocked = sp.getString("autoSwitchLocked", "")!!.split(',').filter { it.isNotBlank() }.toSet()
        sensitivity = sp.getFloat("sensitivity", 1.6f)
        acceleration = sp.getFloat("acceleration", 0.54f)
        scrollSpeed = sp.getFloat("scrollSpeed", 2.5f)
        naturalScroll = sp.getBoolean("naturalScroll", true)
        haptics = sp.getBoolean("haptics", true)
        pointerSensitivity = sp.getFloat("pointerSensitivity", 1500f)
        pointerUpright = sp.getBoolean("pointerUpright", false)
        pointerAlwaysOn = sp.getBoolean("pointerAlwaysOn", true)
        pointerVolumeKeys = sp.getBoolean("pointerVolumeKeys", true)
        pointerMode = sp.getString("pointerMode", "air") ?: "air"
        pointerPrecision = sp.getFloat("pointerPrecision", 0.5f)
        pointerSmoothing = sp.getFloat("pointerSmoothing", 0.28f)
        pointerRange = sp.getFloat("pointerRange", 31.9f)
        neonIntensity = sp.getFloat("neonIntensity", 0.4f)
        terminalFont = sp.getInt("terminalFont", 12)
        pointerMotionGestures = sp.getBoolean("pointerMotionGestures", true)
        pointerPreview = sp.getBoolean("pointerPreview", true)
        favorites = sp.getString("favorites", "")!!.split(',').filter { it.isNotBlank() }.toSet()
        usage = parseUsage(sp.getString("usage", "{}")!!)
        shortcutsJson = sp.getString("shortcuts", "") ?: ""
        lastControl = sp.getString("lastControl", "Touchpad") ?: "Touchpad"
        lastMusic = sp.getString("lastMusic", "Media") ?: "Media"
    }

    // --- PC vinculado ---
    var host: String
        get() = sp.getString("host", "") ?: ""
        set(v) = sp.edit().putString("host", v).apply()

    var port: Int
        get() = sp.getInt("port", 47800)
        set(v) = sp.edit().putInt("port", v).apply()

    var token: String
        get() = sp.getString("token", "") ?: ""
        set(v) = sp.edit().putString("token", v).apply()

    /** Última versión del PC para la que ya se intentó actualizar sola por Wi-Fi (para no repetir en bucle). */
    var autoUpdateTried: String
        get() = sp.getString("autoUpdateTried", "") ?: ""
        set(v) = sp.edit().putString("autoUpdateTried", v).apply()

    var pcName: String
        get() = sp.getString("pcName", "") ?: ""
        set(v) = sp.edit().putString("pcName", v).apply()

    fun forgetPc() {
        sp.edit().remove("host").remove("token").remove("pcName").apply()
    }

    private val ready get() = ::sp.isInitialized

    // --- Comportamiento ---
    private var _autoKeyboard by mutableStateOf(true)
    var autoKeyboard: Boolean
        get() = _autoKeyboard
        set(v) { _autoKeyboard = v; if (ready) sp.edit().putBoolean("autoKeyboard", v).apply() }

    private var _autoHideKeyboard by mutableStateOf(false)
    var autoHideKeyboard: Boolean
        get() = _autoHideKeyboard
        set(v) { _autoHideKeyboard = v; if (ready) sp.edit().putBoolean("autoHideKeyboard", v).apply() }

    private var _autoSwitch by mutableStateOf(true)
    var autoSwitch: Boolean
        get() = _autoSwitch
        set(v) { _autoSwitch = v; if (ready) sp.edit().putBoolean("autoSwitch", v).apply() }

    /** Pantallas (nombres de `Screen`) de las que el cambio automático no te saca. */
    var autoSwitchLocked by mutableStateOf(emptySet<String>())
        private set

    fun toggleAutoSwitchLocked(screen: String) {
        autoSwitchLocked = if (screen in autoSwitchLocked) autoSwitchLocked - screen else autoSwitchLocked + screen
        if (ready) sp.edit().putString("autoSwitchLocked", autoSwitchLocked.joinToString(",")).apply()
    }

    private var _haptics by mutableStateOf(true)
    var haptics: Boolean
        get() = _haptics
        set(v) { _haptics = v; if (ready) sp.edit().putBoolean("haptics", v).apply() }

    // --- Touchpad ---
    private var _sensitivity by mutableFloatStateOf(1.6f)
    var sensitivity: Float
        get() = _sensitivity
        set(v) { _sensitivity = v; if (ready) sp.edit().putFloat("sensitivity", v).apply() }

    private var _acceleration by mutableFloatStateOf(0.54f)
    var acceleration: Float
        get() = _acceleration
        set(v) { _acceleration = v; if (ready) sp.edit().putFloat("acceleration", v).apply() }

    private var _scrollSpeed by mutableFloatStateOf(2.5f)
    var scrollSpeed: Float
        get() = _scrollSpeed
        set(v) { _scrollSpeed = v; if (ready) sp.edit().putFloat("scrollSpeed", v).apply() }

    private var _naturalScroll by mutableStateOf(true)
    var naturalScroll: Boolean
        get() = _naturalScroll
        set(v) { _naturalScroll = v; if (ready) sp.edit().putBoolean("naturalScroll", v).apply() }

    // --- Puntero (giroscopio, estilo Wii) ---
    private var _pointerSensitivity by mutableFloatStateOf(1500f)
    /** Píxeles por radián girado. */
    var pointerSensitivity: Float
        get() = _pointerSensitivity
        set(v) { _pointerSensitivity = v; if (ready) sp.edit().putFloat("pointerSensitivity", v).apply() }

    private var _pointerUpright by mutableStateOf(false)
    /** false = teléfono acostado como control de Wii (pantalla hacia arriba); true = de pie, pantalla hacia ti. */
    var pointerUpright: Boolean
        get() = _pointerUpright
        set(v) { _pointerUpright = v; if (ready) sp.edit().putBoolean("pointerUpright", v).apply() }

    private var _pointerAlwaysOn by mutableStateOf(true)
    /** true = apunta siempre; false = solo mientras mantienes el botón B. */
    var pointerAlwaysOn: Boolean
        get() = _pointerAlwaysOn
        set(v) { _pointerAlwaysOn = v; if (ready) sp.edit().putBoolean("pointerAlwaysOn", v).apply() }

    private var _pointerVolumeKeys by mutableStateOf(true)
    /** Botones de volumen = clic izquierdo / derecho mientras está abierto el puntero. */
    var pointerVolumeKeys: Boolean
        get() = _pointerVolumeKeys
        set(v) { _pointerVolumeKeys = v; if (ready) sp.edit().putBoolean("pointerVolumeKeys", v).apply() }

    private var _pointerMode by mutableStateOf("air")
    /** "air" = relativo con giroscopio (mouse aéreo); "wii" = absoluto, el puntero va donde apuntas. */
    var pointerMode: String
        get() = _pointerMode
        set(v) { _pointerMode = v; if (ready) sp.edit().putString("pointerMode", v).apply() }

    private var _pointerPrecision by mutableFloatStateOf(0.5f)
    /** 0..1: cuánto más fino se mueve el puntero cuando giras lento. */
    var pointerPrecision: Float
        get() = _pointerPrecision
        set(v) { _pointerPrecision = v; if (ready) sp.edit().putFloat("pointerPrecision", v).apply() }

    private var _pointerSmoothing by mutableFloatStateOf(0.28f)
    /** 0..1: filtro anti-temblor (más = más estable, un poco más de retraso al moverse lento). */
    var pointerSmoothing: Float
        get() = _pointerSmoothing
        set(v) { _pointerSmoothing = v; if (ready) sp.edit().putFloat("pointerSmoothing", v).apply() }

    private var _pointerRange by mutableFloatStateOf(31.9f)
    /** Modo Wii: grados de giro desde el centro hasta el borde izquierdo/derecho de la pantalla. */
    var pointerRange: Float
        get() = _pointerRange
        set(v) { _pointerRange = v; if (ready) sp.edit().putFloat("pointerRange", v).apply() }

    // --- Apariencia ---
    private var _neonIntensity by mutableFloatStateOf(0.4f)
    /** 0..1: saturación de los colores y fuerza del brillo neón (1 = como la primera versión). */
    var neonIntensity: Float
        get() = _neonIntensity
        set(v) { _neonIntensity = v; if (ready) sp.edit().putFloat("neonIntensity", v).apply() }


    // --- Terminal ---
    var terminalFont: Int = 12
        set(v) { field = v; if (ready) sp.edit().putInt("terminalFont", v).apply() }

    // --- Puntero: gestos con el movimiento ---
    private var _pointerMotionGestures by mutableStateOf(true)
    /**
     * true = mantener A apunta aunque no tengas B (arrastrar moviendo el teléfono) y mantener el botón de scroll
     * desplaza inclinando el teléfono arriba/abajo.
     */
    var pointerMotionGestures: Boolean
        get() = _pointerMotionGestures
        set(v) { _pointerMotionGestures = v; if (ready) sp.edit().putBoolean("pointerMotionGestures", v).apply() }

    private var _pointerPreview by mutableStateOf(true)
    /** Modo Wii: mostrar la mini pantalla con la posición del puntero. */
    var pointerPreview: Boolean
        get() = _pointerPreview
        set(v) { _pointerPreview = v; if (ready) sp.edit().putBoolean("pointerPreview", v).apply() }

    // --- Inicio: favoritos, más usados y accesos ---
    var favorites by mutableStateOf(emptySet<String>())
        private set

    fun toggleFavorite(screen: String) {
        favorites = if (screen in favorites) favorites - screen else favorites + screen
        if (ready) sp.edit().putString("favorites", favorites.joinToString(",")).apply()
    }

    var usage by mutableStateOf(emptyMap<String, Int>())
        private set

    fun recordUse(screen: String) {
        usage = usage + (screen to (usage[screen] ?: 0) + 1)
        if (ready) sp.edit().putString("usage", JSONObject(usage.mapValues { it.value }).toString()).apply()
    }

    private fun parseUsage(json: String): Map<String, Int> = try {
        val o = JSONObject(json)
        o.keys().asSequence().associateWith { o.optInt(it) }
    } catch (_: Exception) {
        emptyMap()
    }

    /** Accesos del inicio en JSON (ver Shortcuts.kt). Vacío = usar los de fábrica. */
    var shortcutsJson by mutableStateOf("")
        private set

    fun saveShortcuts(arr: JSONArray) {
        shortcutsJson = arr.toString()
        if (ready) sp.edit().putString("shortcuts", shortcutsJson).apply()
    }

    /** Última pestaña usada en "Control" y "Música", para volver a la misma. */
    var lastControl: String = "Touchpad"
        set(v) { field = v; if (ready) sp.edit().putString("lastControl", v).apply() }

    var lastMusic: String = "Media"
        set(v) { field = v; if (ready) sp.edit().putString("lastMusic", v).apply() }
}
