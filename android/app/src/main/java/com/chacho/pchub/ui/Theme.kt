package com.chacho.pchub.ui

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
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

/** Colores neón originales (máxima intensidad). En la UI se usan los de [Neon], que respetan el ajuste de intensidad. */
object NeonRaw {
    val Cyan = Color(0xFF00E5FF)
    val Violet = Color(0xFF9B5CFF)
    val Magenta = Color(0xFFFF2BD6)
    val Lime = Color(0xFFB6FF3B)
    val Amber = Color(0xFFFFB547)
    val Red = Color(0xFFFF4D6D)
    val TextDim = Color(0xFFA9A3C2)
}

/**
 * Paleta neón. Los acentos se desaturan según Ajustes → Apariencia → "Intensidad del neón"
 * (1 = colores y brillo a tope, como la primera versión; por defecto más suave).
 */
object Neon {
    val Bg = Color(0xFF07060D)
    val BgDeep = Color(0xFF0C0A16)
    val Cyan: Color get() = tone(NeonRaw.Cyan)
    val Violet: Color get() = tone(NeonRaw.Violet)
    val Magenta: Color get() = tone(NeonRaw.Magenta)
    val Lime: Color get() = tone(NeonRaw.Lime)
    val Amber: Color get() = tone(NeonRaw.Amber)
    val Red: Color get() = tone(NeonRaw.Red)
    val Text = Color(0xFFF1EEFF)
    val TextDim = NeonRaw.TextDim
    val TextFaint = Color(0xFF6E6889)
    /** Fondo de los diálogos y menús (opaco, para que se lean bien). */
    val Panel = Color(0xFF161327)

    /** 0..1, cuánto brillo (sombras de color, manchas del fondo, bordes iluminados). */
    val glow: Float get() = 0.15f + 0.85f * Prefs.neonIntensity

    /** Acerca el color a un gris de la misma luminosidad según la intensidad elegida. */
    fun tone(c: Color): Color {
        val s = 0.35f + 0.65f * Prefs.neonIntensity
        val l = 0.299f * c.red + 0.587f * c.green + 0.114f * c.blue
        fun ch(v: Float) = (l + (v - l) * s).coerceIn(0f, 1f)
        return Color(ch(c.red), ch(c.green), ch(c.blue), c.alpha)
    }
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
    onPrimary = Color(0xFF00141A),
    primaryContainer = Color(0xFF0C3A46),
    onPrimaryContainer = Color(0xFFB8F6FF),
    secondary = Neon.Violet,
    onSecondary = Color(0xFF16062E),
    secondaryContainer = Color(0xFF2C1A55),
    onSecondaryContainer = Color(0xFFE6DAFF),
    tertiary = Neon.Magenta,
    onTertiary = Color(0xFF2A0022),
    background = Neon.Bg,
    onBackground = Neon.Text,
    surface = Neon.Bg,
    onSurface = Neon.Text,
    surfaceVariant = Color(0xFF221E36),
    onSurfaceVariant = Neon.TextDim,
    surfaceContainerLowest = Neon.Bg,
    surfaceContainerLow = Color(0xFF110E1D),
    surfaceContainer = Color(0xFF141124),
    surfaceContainerHigh = Neon.Panel,
    surfaceContainerHighest = Color(0xFF221E36),
    outline = Color(0xFF3B3557),
    outlineVariant = Color(0xFF2A2540),
    error = Neon.Red,
    onError = Color(0xFF2B000A),
)

private val Base = Typography()

private val Type = Base.copy(
    displaySmall = Base.displaySmall.copy(fontWeight = FontWeight.Black, letterSpacing = (-0.5).sp),
    headlineLarge = Base.headlineLarge.copy(fontWeight = FontWeight.Black, letterSpacing = (-0.5).sp),
    headlineMedium = Base.headlineMedium.copy(fontWeight = FontWeight.ExtraBold, letterSpacing = (-0.3).sp),
    headlineSmall = Base.headlineSmall.copy(fontWeight = FontWeight.Bold),
    titleLarge = Base.titleLarge.copy(fontWeight = FontWeight.Bold),
    titleMedium = Base.titleMedium.copy(fontWeight = FontWeight.SemiBold),
    labelSmall = Base.labelSmall.copy(letterSpacing = 1.sp, fontWeight = FontWeight.SemiBold),
)

@Composable
fun PcHubTheme(content: @Composable () -> Unit) {
    val intensity = Prefs.neonIntensity
    val colors = remember(intensity) { scheme() }
    MaterialTheme(colorScheme = colors, typography = Type, content = content)
}

/** Texto en mayúsculas pequeño para encabezados de sección. */
val SectionStyle: TextStyle
    @Composable get() = MaterialTheme.typography.labelSmall.copy(color = Neon.TextDim)
