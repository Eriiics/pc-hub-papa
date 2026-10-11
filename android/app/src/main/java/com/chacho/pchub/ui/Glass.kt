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
import androidx.compose.ui.graphics.compositeOver
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

/** Fondo negro liso (como One UI en modo oscuro); las tarjetas grises se ven encima. */
@Composable
fun NeonBackground(modifier: Modifier = Modifier) {
    Box(modifier.fillMaxSize().background(Neon.Bg))
}

// ============================================================ vidrio

/**
 * Tarjeta lisa de esquinas muy redondeadas (estilo One UI). El nombre se conserva por compatibilidad:
 * [glow] ya no dibuja resplandor, [tint] tiñe suavemente la tarjeta y [strong] la aclara un poco.
 */
fun Modifier.glass(
    shape: Shape = RoundedCornerShape(28.dp),
    glow: Color? = null,
    strong: Boolean = false,
    tint: Color? = null,
): Modifier {
    val base = if (strong) Neon.CardStrong else Neon.Card
    val fill = if (tint != null) tint.copy(alpha = 0.16f).compositeOver(base) else base
    return this.clip(shape).background(fill)
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
fun GlassCard(
    modifier: Modifier = Modifier,
    shape: Shape = RoundedCornerShape(28.dp),
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
            .glass(RoundedCornerShape(28.dp), tint = if (highlighted) accent else null)
            .combinedClickable(
                onClick = { haptic(false); onClick() },
                onLongClick = onLongClick?.let { { haptic(true); it() } },
            )
            .padding(10.dp),
    ) {
        Column(Modifier.fillMaxSize(), verticalArrangement = Arrangement.Center, horizontalAlignment = Alignment.CenterHorizontally) {
            Box(
                Modifier
                    .size(52.dp)
                    .clip(CircleShape)
                    .background(accent.copy(alpha = 0.20f)),
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
                    .clip(CircleShape)
                    .background(accent)
                else Modifier.glass(CircleShape)
            )
            .clickable(enabled = enabled) { haptic(false); onClick() },
        contentAlignment = Alignment.Center,
    ) {
        Icon(
            icon,
            contentDescription = contentDescription,
            tint = if (filled) Color.White else if (enabled) accent else Neon.TextFaint,
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
    val shape = RoundedCornerShape(50)
    Row(
        modifier
            .height(52.dp)
            .then(
                if (outlined || !enabled) Modifier.glass(shape)
                else Modifier
                    .clip(shape)
                    .background(accent)
            )
            .clickable(enabled = enabled) { haptic(false); onClick() }
            .padding(horizontal = 18.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.Center,
    ) {
        val color = when {
            !enabled -> Neon.TextFaint
            outlined -> accent
            else -> Color.White
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
            val bg by animateColorAsState(if (active) Neon.Cyan else Color.Transparent, label = "seg")
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
                    color = if (active) Color.White else Neon.TextDim,
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
    Row(modifier.fillMaxWidth().padding(start = 8.dp, top = 20.dp, bottom = 8.dp), verticalAlignment = Alignment.CenterVertically) {
        Text(text, style = SectionStyle, modifier = Modifier.weight(1f))
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
