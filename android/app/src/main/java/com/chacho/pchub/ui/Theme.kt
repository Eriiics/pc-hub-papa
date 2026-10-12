package com.chacho.pchub.ui

import androidx.compose.material3.LocalContentColor
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import com.chacho.pchub.Prefs
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.sp

/** Colores de acento (estilo One UI de Samsung: azul, menta, ámbar...). En la UI se usan los de [Neon]. */
object NeonRaw {
    val Cyan = Color(0xFF3E91FF)
    val Violet = Color(0xFF8E86FF)
    val Magenta = Color(0xFFFF6FA5)
    val Lime = Color(0xFF3DD68C)
    val Amber = Color(0xFFFFB84D)
    val Red = Color(0xFFFF5D5D)
    val TextDim = Color(0xFFA8A8AE)
}

/**
 * Paleta neón. Los acentos se desaturan según Ajustes → Apariencia → "Intensidad del neón"
 * (1 = colores y brillo a tope, como la primera versión; por defecto más suave).
 */
object Neon {
    val Bg = Color(0xFF000000)
    val BgDeep = Color(0xFF0A0A0B)
    /** Tarjetas: gris casi negro, como One UI en modo oscuro. */
    val Card = Color(0xFF1C1C1F)
    val CardStrong = Color(0xFF28282C)
    val Cyan: Color get() = tone(NeonRaw.Cyan)
    val Violet: Color get() = tone(NeonRaw.Violet)
    val Magenta: Color get() = tone(NeonRaw.Magenta)
    val Lime: Color get() = tone(NeonRaw.Lime)
    val Amber: Color get() = tone(NeonRaw.Amber)
    val Red: Color get() = tone(NeonRaw.Red)
    val Text = Color(0xFFF4F4F6)
    val TextDim = NeonRaw.TextDim
    val TextFaint = Color(0xFF76767C)
    /** Fondo de los diálogos y menús (opaco, para que se lean bien). */
    val Panel = Color(0xFF232326)

    /** Sin resplandor: el estilo Samsung es plano y limpio. */
    val glow: Float get() = 0f

    fun tone(c: Color): Color = c
}

/** Sombra de color (el "brillo" neón), escalada por la intensidad elegida. */
fun Modifier.neonShadow(elevation: Dp, shape: Shape, color: Color): Modifier {
    val g = Neon.glow
    val e = elevation * g
    if (e < 1.dp) return this
    val c = color.copy(alpha = color.alpha * (0.35f + 0.65f * g))
    return this.shadow(e, shape, ambientColor = c, spotColor = c)
}

private fun scheme() = darkColorScheme(
    primary = Neon.Cyan,
    onPrimary = Color.White,
    primaryContainer = Color(0xFF173A66),
    onPrimaryContainer = Color(0xFFD6E6FF),
    secondary = Neon.Violet,
    onSecondary = Color.White,
    secondaryContainer = Color(0xFF2A2850),
    onSecondaryContainer = Color(0xFFE6E4FF),
    tertiary = Neon.Magenta,
    onTertiary = Color.White,
    background = Neon.Bg,
    onBackground = Neon.Text,
    surface = Neon.Bg,
    onSurface = Neon.Text,
    surfaceVariant = Color(0xFF2A2A2E),
    onSurfaceVariant = Neon.TextDim,
    surfaceContainerLowest = Neon.Bg,
    surfaceContainerLow = Color(0xFF111113),
    surfaceContainer = Color(0xFF18181B),
    surfaceContainerHigh = Neon.Panel,
    surfaceContainerHighest = Color(0xFF2E2E32),
    outline = Color(0xFF45454B),
    outlineVariant = Color(0xFF2C2C30),
    error = Neon.Red,
    onError = Color(0xFF2B000A),
)

private val Base = Typography()

private val Type = Base.copy(
    displaySmall = Base.displaySmall.copy(fontWeight = FontWeight.Bold),
    headlineLarge = Base.headlineLarge.copy(fontWeight = FontWeight.Bold),
    headlineMedium = Base.headlineMedium.copy(fontWeight = FontWeight.Bold),
    headlineSmall = Base.headlineSmall.copy(fontWeight = FontWeight.SemiBold),
    titleLarge = Base.titleLarge.copy(fontWeight = FontWeight.Bold),
    titleMedium = Base.titleMedium.copy(fontWeight = FontWeight.SemiBold),
    labelSmall = Base.labelSmall.copy(letterSpacing = 0.2.sp, fontWeight = FontWeight.Medium),
)

@Composable
fun PcHubTheme(content: @Composable () -> Unit) {
    val colors = remember { scheme() }
    MaterialTheme(colorScheme = colors, typography = Type) {
        // Sin esto, los Text sin color propio salen negros sobre las tarjetas oscuras
        CompositionLocalProvider(LocalContentColor provides Neon.Text, content = content)
    }
}

/** Texto en mayúsculas pequeño para encabezados de sección. */
val SectionStyle: TextStyle
    @Composable get() = MaterialTheme.typography.titleSmall.copy(color = Neon.Cyan, fontWeight = FontWeight.SemiBold)
