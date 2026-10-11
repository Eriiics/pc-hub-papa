package com.chacho.pchub.ui

import android.graphics.BitmapFactory
import android.util.LruCache
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxScope
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.produceState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request
import kotlin.math.cos
import kotlin.math.sin

// ============================================================ fondo

/** Fondo oscuro con manchas de luz neón que se mueven lento (los paneles de vidrio se ven encima). */
@Composable
fun NeonBackground(modifier: Modifier = Modifier) {
    val t = rememberInfiniteTransition(label = "bg")
    val phase by t.animateFloat(
        initialValue = 0f,
        targetValue = (2 * Math.PI).toFloat(),
        animationSpec = infiniteRepeatable(tween(26_000, easing = LinearEasing), RepeatMode.Restart),
        label = "phase",
    )
    // Se leen fuera del Canvas para que cambiar la intensidad en Ajustes redibuje el fondo
    val k = 0.45f + 0.55f * Neon.glow
    val violet = Neon.Violet
    val cyan = Neon.Cyan
    val magenta = Neon.Magenta
    Canvas(modifier.fillMaxSize().background(Neon.Bg)) {
        val w = size.width
        val h = size.height
        fun blob(color: Color, cx: Float, cy: Float, r: Float) {
            drawCircle(
                brush = Brush.radialGradient(listOf(color, Color.Transparent), center = Offset(cx, cy), radius = r),
                radius = r,
                center = Offset(cx, cy),
            )
        }
        blob(violet.copy(alpha = 0.34f * k), w * (0.18f + 0.10f * cos(phase)), h * (0.14f + 0.05f * sin(phase)), w * 0.85f)
        blob(cyan.copy(alpha = 0.20f * k), w * (0.92f + 0.08f * sin(phase * 1.3f)), h * (0.48f + 0.08f * cos(phase)), w * 0.75f)
        blob(magenta.copy(alpha = 0.18f * k), w * (0.25f + 0.12f * sin(phase * 0.7f)), h * (0.92f + 0.04f * cos(phase * 1.1f)), w * 0.80f)
    }
}

// ============================================================ vidrio

/**
 * Panel de vidrio: relleno translúcido con degradado, borde fino iluminado arriba y, opcional, brillo neón.
 * Sobre NeonBackground da el efecto "glass".
 */
fun Modifier.glass(
    shape: Shape = RoundedCornerShape(24.dp),
    glow: Color? = null,
    strong: Boolean = false,
    tint: Color? = null,
): Modifier {
    val top = if (strong) 0.15f else 0.085f
    val bottom = if (strong) 0.07f else 0.035f
    val fill = Brush.verticalGradient(
        listOf(
            (tint ?: Color.White).copy(alpha = if (tint != null) top * 1.6f else top),
            (tint ?: Color.White).copy(alpha = if (tint != null) bottom * 1.6f else bottom),
        )
    )
    val edge = Brush.linearGradient(
        listOf(Color.White.copy(alpha = 0.30f), Color.White.copy(alpha = 0.05f), (glow ?: Color.White).copy(alpha = if (glow != null) 0.12f + 0.33f * Neon.glow else 0.10f))
    )
    return this
        .then(if (glow != null) Modifier.neonShadow(22.dp, shape, glow) else Modifier)
        .clip(shape)
        .background(fill)
        .border(1.dp, edge, shape)
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
fun GlassCard(
    modifier: Modifier = Modifier,
    shape: Shape = RoundedCornerShape(24.dp),
    glow: Color? = null,
    strong: Boolean = false,
    tint: Color? = null,
    onClick: (() -> Unit)? = null,
    onLongClick: (() -> Unit)? = null,
    padding: Dp = 16.dp,
    content: @Composable ColumnScope.() -> Unit,
) {
    val haptic = rememberHaptic()
    val click = if (onClick != null || onLongClick != null) Modifier.combinedClickable(
        onClick = { haptic(false); onClick?.invoke() },
        onLongClick = onLongClick?.let { { haptic(true); it() } },
    ) else Modifier
    Column(modifier.glass(shape, glow, strong, tint).then(click).padding(padding), content = content)
}

// ============================================================ tiles y botones

/** Tile cuadrado de un módulo o acceso. */
@OptIn(ExperimentalFoundationApi::class)
@Composable
fun NeonTile(
    label: String,
    modifier: Modifier = Modifier,
    icon: ImageVector? = null,
    emoji: String? = null,
    image: ImageBitmap? = null,
    accent: Color = Neon.Cyan,
    highlighted: Boolean = false,
    badge: String? = null,
    onLongClick: (() -> Unit)? = null,
    onClick: () -> Unit,
) {
    val haptic = rememberHaptic()
    Box(
        modifier
            .aspectRatio(1f)
            .glass(RoundedCornerShape(22.dp), glow = if (highlighted) accent else null, tint = if (highlighted) accent else null)
            .combinedClickable(
                onClick = { haptic(false); onClick() },
                onLongClick = onLongClick?.let { { haptic(true); it() } },
            )
            .padding(10.dp),
    ) {
        Column(Modifier.fillMaxSize(), verticalArrangement = Arrangement.Center, horizontalAlignment = Alignment.CenterHorizontally) {
            Box(
                Modifier
                    .size(44.dp)
                    .clip(CircleShape)
                    .background(Brush.radialGradient(listOf(accent.copy(alpha = 0.32f), Color.Transparent))),
                contentAlignment = Alignment.Center,
            ) {
                when {
                    image != null -> Image(image, contentDescription = null, modifier = Modifier.size(30.dp))
                    icon != null -> Icon(icon, contentDescription = null, tint = accent, modifier = Modifier.size(28.dp))
                    emoji != null -> Text(emoji, fontSize = 24.sp)
                }
            }
            Spacer(Modifier.height(8.dp))
            Text(
                label,
                style = MaterialTheme.typography.labelMedium,
                color = Neon.Text,
                textAlign = TextAlign.Center,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
            )
        }
        if (badge != null) {
            Text(
                badge,
                fontSize = 10.sp,
                color = accent,
                modifier = Modifier.align(Alignment.TopEnd),
            )
        }
    }
}

/** Botón redondo con ícono y brillo. */
@Composable
fun NeonIconButton(
    icon: ImageVector,
    contentDescription: String?,
    modifier: Modifier = Modifier,
    size: Dp = 48.dp,
    accent: Color = Neon.Cyan,
    filled: Boolean = false,
    enabled: Boolean = true,
    onClick: () -> Unit,
) {
    val haptic = rememberHaptic()
    Box(
        modifier
            .size(size)
            .then(
                if (filled) Modifier
                    .neonShadow(18.dp, CircleShape, accent)
                    .clip(CircleShape)
                    .background(Brush.linearGradient(listOf(accent, accent.copy(alpha = 0.65f))))
                else Modifier.glass(CircleShape)
            )
            .clickable(enabled = enabled) { haptic(false); onClick() },
        contentAlignment = Alignment.Center,
    ) {
        Icon(
            icon,
            contentDescription = contentDescription,
            tint = if (filled) Neon.Bg else if (enabled) accent else Neon.TextFaint,
            modifier = Modifier.size(size * 0.5f),
        )
    }
}

/** Botón ancho con degradado neón. */
@Composable
fun NeonButton(
    text: String,
    modifier: Modifier = Modifier,
    icon: ImageVector? = null,
    accent: Color = Neon.Cyan,
    enabled: Boolean = true,
    outlined: Boolean = false,
    onClick: () -> Unit,
) {
    val haptic = rememberHaptic()
    val shape = RoundedCornerShape(18.dp)
    Row(
        modifier
            .height(50.dp)
            .then(
                if (outlined || !enabled) Modifier.glass(shape)
                else Modifier
                    .neonShadow(16.dp, shape, accent)
                    .clip(shape)
                    .background(Brush.horizontalGradient(listOf(accent, Neon.Violet)))
            )
            .clickable(enabled = enabled) { haptic(false); onClick() }
            .padding(horizontal = 18.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.Center,
    ) {
        val color = when {
            !enabled -> Neon.TextFaint
            outlined -> accent
            else -> Neon.Bg
        }
        if (icon != null) {
            Icon(icon, contentDescription = null, tint = color, modifier = Modifier.size(20.dp))
            Spacer(Modifier.size(8.dp))
        }
        Text(text, color = color, fontWeight = FontWeight.Bold, maxLines = 1, overflow = TextOverflow.Ellipsis)
    }
}

/** Selector de pestañas tipo píldora (ej. Touchpad | Puntero). */
@Composable
fun SegmentedTabs(options: List<String>, selected: Int, modifier: Modifier = Modifier, onSelect: (Int) -> Unit) {
    val haptic = rememberHaptic()
    Row(modifier.glass(RoundedCornerShape(50)).padding(4.dp)) {
        options.forEachIndexed { i, label ->
            val active = i == selected
            val bg by animateColorAsState(if (active) Neon.Cyan.copy(alpha = 0.22f) else Color.Transparent, label = "seg")
            Box(
                Modifier
                    .weight(1f)
                    .clip(RoundedCornerShape(50))
                    .background(bg)
                    .clickable { haptic(false); onSelect(i) }
                    .padding(vertical = 10.dp),
                contentAlignment = Alignment.Center,
            ) {
                Text(
                    label,
                    color = if (active) Neon.Cyan else Neon.TextDim,
                    fontWeight = if (active) FontWeight.Bold else FontWeight.Medium,
                    style = MaterialTheme.typography.labelLarge,
                )
            }
        }
    }
}

/** Título de sección en mayúsculas. */
@Composable
fun SectionTitle(text: String, modifier: Modifier = Modifier, trailing: (@Composable () -> Unit)? = null) {
    Row(modifier.fillMaxWidth().padding(start = 4.dp, top = 18.dp, bottom = 8.dp), verticalAlignment = Alignment.CenterVertically) {
        Text(text.uppercase(), style = SectionStyle, modifier = Modifier.weight(1f))
        trailing?.invoke()
    }
}

/** Contenedor con el ancho de lectura y margen estándar. */
@Composable
fun ScreenBox(modifier: Modifier = Modifier, content: @Composable BoxScope.() -> Unit) {
    Box(modifier.fillMaxSize().padding(horizontal = 16.dp), content = content)
}

// ============================================================ imágenes de internet (miniaturas de YouTube)

private val ImageCache = object : LruCache<String, ImageBitmap>(80) {}
private val ImageClient = OkHttpClient()

@Composable
fun rememberNetImage(url: String?): ImageBitmap? {
    val state by produceState<ImageBitmap?>(initialValue = url?.let { ImageCache.get(it) }, url) {
        if (url.isNullOrEmpty() || value != null) return@produceState
        value = withContext(Dispatchers.IO) {
            runCatching {
                ImageClient.newCall(Request.Builder().url(url).build()).execute().use { resp ->
                    val bytes = resp.body?.bytes() ?: return@use null
                    BitmapFactory.decodeByteArray(bytes, 0, bytes.size)?.asImageBitmap()
                }
            }.getOrNull()
        }?.also { ImageCache.put(url, it) }
    }
    return state
}

@Composable
fun NetImage(url: String?, modifier: Modifier = Modifier, contentScale: ContentScale = ContentScale.Crop) {
    val img = rememberNetImage(url)
    Box(modifier.background(Color.White.copy(alpha = 0.05f))) {
        if (img != null) Image(img, contentDescription = null, contentScale = contentScale, modifier = Modifier.fillMaxSize())
    }
}
