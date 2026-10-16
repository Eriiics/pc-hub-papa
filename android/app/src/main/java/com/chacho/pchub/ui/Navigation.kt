package com.chacho.pchub.ui

import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Apps
import androidx.compose.material.icons.filled.AutoFixHigh
import androidx.compose.material.icons.filled.AutoAwesome
import androidx.compose.material.icons.filled.Dashboard
import androidx.compose.material.icons.filled.Home
import androidx.compose.material.icons.filled.Mouse
import androidx.compose.material.icons.filled.MusicNote
import androidx.compose.material.icons.filled.PowerSettingsNew
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material.icons.filled.SmartDisplay
import androidx.compose.material.icons.filled.TableChart
import androidx.compose.material.icons.filled.SportsEsports
import androidx.compose.material.icons.filled.Tab
import androidx.compose.material.icons.filled.Terminal
import androidx.compose.material.icons.filled.TouchApp
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector

/** Pestañas de la barra inferior. */
enum class Tab(val label: String, val icon: ImageVector) {
    Home("Inicio", Icons.Filled.Home),
    Control("Control", Icons.Filled.Mouse),
    Music("Música", Icons.Filled.MusicNote),
    Ai("IA", Icons.Filled.AutoAwesome),
    Modules("Módulos", Icons.Filled.Dashboard),
}

/**
 * Todas las pantallas. [tab] es la pestaña que queda marcada en la barra inferior.
 * [module] = aparece en la grilla de Módulos y puede ser favorito / "más usado".
 */
enum class Screen(
    val title: String,
    val tab: Tab,
    val icon: ImageVector,
    private val rawAccent: Color,
    val description: String,
    val module: Boolean = true,
) {
    Home("Inicio", Tab.Home, Icons.Filled.Home, NeonRaw.Cyan, "", module = false),
    Touchpad("Touchpad", Tab.Control, Icons.Filled.TouchApp, NeonRaw.Cyan, "Mouse con el dedo"),
    Pointer("Puntero", Tab.Control, Icons.Filled.SportsEsports, NeonRaw.Magenta, "Apunta moviendo el teléfono", module = false),
    Media("Reproductor", Tab.Music, Icons.Filled.MusicNote, NeonRaw.Violet, "Lo que suena en el PC"),
    YouTube("YouTube", Tab.Music, Icons.Filled.SmartDisplay, NeonRaw.Red, "Buscar y reproducir"),
    Ai("Asistente", Tab.Ai, Icons.Filled.AutoAwesome, NeonRaw.Violet, "Pídele cosas al PC"),
    Modules("Módulos", Tab.Modules, Icons.Filled.Dashboard, NeonRaw.Cyan, "", module = false),
    Windows("Ventanas", Tab.Modules, Icons.Filled.Tab, NeonRaw.Cyan, "Cambiar y cerrar ventanas"),
    Apps("Aplicaciones", Tab.Modules, Icons.Filled.Apps, NeonRaw.Lime, "Abrir apps y juegos"),
    System("Sistema", Tab.Modules, Icons.Filled.PowerSettingsNew, NeonRaw.Amber, "Energía y portapapeles"),
    Terminal("Terminal", Tab.Modules, Icons.Filled.Terminal, NeonRaw.Lime, "PowerShell completa, también en el PC", module = false),
    Excel("Excel", Tab.Modules, Icons.Filled.TableChart, NeonRaw.Lime, "Ver la hoja, escribir y guardar"),
    Mejoras("Mejoras", Tab.Modules, Icons.Filled.AutoFixHigh, NeonRaw.Violet, "Pídele cambios a tu Claude"),
    Settings("Ajustes", Tab.Modules, Icons.Filled.Settings, NeonRaw.TextDim, "Tu PC y el mouse"),
    ;

    /** Color de acento con la intensidad de neón elegida en Ajustes. */
    val accent: Color get() = Neon.tone(rawAccent)
}

val ModuleScreens: List<Screen> get() = Screen.entries.filter { it.module }

/** Convierte el "module" de una regla de contexto del PC en una pantalla de la app. */
fun moduleToScreen(module: String?): Screen? = when (module?.lowercase()) {
    "media", "music", "musica" -> Screen.Media
    "youtube" -> Screen.YouTube
    "touchpad", "mouse" -> Screen.Touchpad
    "pointer", "puntero" -> Screen.Pointer
    "windows", "ventanas" -> Screen.Windows
    "apps" -> Screen.Apps
    "system", "sistema" -> Screen.System
    "ai", "ia" -> Screen.Ai
    "excel" -> Screen.Excel
    "mejoras" -> Screen.Mejoras
    "terminal" -> Screen.Terminal
    else -> null
}
