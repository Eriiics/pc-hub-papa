package com.chacho.pchub.ui

import androidx.compose.foundation.Image
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Apps
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import org.json.JSONObject
import java.text.Normalizer

private data class PcApp(val id: String, val name: String, val icon: String?)

/** Caché en memoria para no pedir la lista (con íconos) cada vez que se abre la pantalla. */
private object AppsCache {
    var apps: List<PcApp>? = null
}

private fun norm(s: String) =
    Normalizer.normalize(s.lowercase(), Normalizer.Form.NFD).replace(Regex("\\p{Mn}+"), "")

@Composable
fun AppsScreen() {
    val scope = rememberCoroutineScope()
    val haptic = rememberHaptic()
    var apps by remember { mutableStateOf(AppsCache.apps) }
    var query by remember { mutableStateOf("") }

    LaunchedEffect(Unit) {
        scope.pcCall("apps.list", timeoutMs = 30_000) { d ->
            val arr = d.optJSONArray("apps")
            val list = if (arr == null) emptyList() else (0 until arr.length()).map {
                val o = arr.getJSONObject(it)
                PcApp(o.optString("id"), o.optString("name"), o.str("icon"))
            }
            AppsCache.apps = list
            apps = list
        }
    }

    val filtered = remember(apps, query) {
        val q = norm(query.trim())
        apps.orEmpty().filter { q.isEmpty() || norm(it.name).contains(q) }
    }

    Column(Modifier.fillMaxSize()) {
        Row(
            Modifier
                .padding(horizontal = 16.dp, vertical = 8.dp)
                .fillMaxWidth()
                .glass(RoundedCornerShape(26.dp), glow = Neon.Lime, strong = true)
                .padding(horizontal = 16.dp, vertical = 14.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Icon(Icons.Filled.Search, contentDescription = null, tint = Neon.Lime, modifier = Modifier.size(22.dp))
            Spacer(Modifier.width(10.dp))
            Box(Modifier.weight(1f)) {
                if (query.isEmpty()) Text("Buscar aplicación o juego", color = Neon.TextFaint)
                BasicTextField(
                    value = query,
                    onValueChange = { query = it },
                    singleLine = true,
                    textStyle = MaterialTheme.typography.bodyLarge.copy(color = Neon.Text),
                    cursorBrush = SolidColor(Neon.Lime),
                    modifier = Modifier.fillMaxWidth(),
                )
            }
        }

        if (apps == null) {
            Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                CircularProgressIndicator(color = Neon.Lime)
            }
        } else {
            LazyVerticalGrid(
                columns = GridCells.Adaptive(minSize = 88.dp),
                contentPadding = PaddingValues(start = 14.dp, end = 14.dp, top = 6.dp, bottom = 20.dp),
                horizontalArrangement = Arrangement.spacedBy(10.dp),
                verticalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                items(filtered, key = { it.id }) { app ->
                    val icon = rememberBase64Image(app.icon)
                    NeonTile(
                        app.name,
                        image = icon,
                        icon = if (icon == null) Icons.Filled.Apps else null,
                        accent = Neon.Lime,
                    ) {
                        haptic(false)
                        Toaster.show("Abriendo ${app.name}…")
                        scope.pcCall("apps.launch", JSONObject().put("id", app.id))
                    }
                }
            }
        }
    }
}
