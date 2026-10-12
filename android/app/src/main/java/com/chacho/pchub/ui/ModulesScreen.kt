package com.chacho.pchub.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.GridItemSpan
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.chacho.pchub.Prefs

/** Grilla con todos los módulos. Mantener presionado fija/quita un módulo de favoritos (aparece primero en Inicio). */
@Composable
fun ModulesScreen(nav: Nav) {
    LazyVerticalGrid(
        columns = GridCells.Adaptive(minSize = 104.dp),
        contentPadding = PaddingValues(start = 16.dp, end = 16.dp, top = 8.dp, bottom = 24.dp),
        horizontalArrangement = Arrangement.spacedBy(12.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
        modifier = Modifier.fillMaxSize(),
    ) {
        item(span = { GridItemSpan(maxLineSpan) }) {
            Column(Modifier.padding(bottom = 4.dp)) {
                Text(
                    "Todo lo que puedes controlar desde el teléfono.",
                    style = MaterialTheme.typography.bodySmall,
                    color = Neon.TextDim,
                )
            }
        }
        items(ModuleScreens) { s ->
            NeonTile(s.title, icon = s.icon, accent = s.accent) { nav.go(s) }
        }
    }
}
