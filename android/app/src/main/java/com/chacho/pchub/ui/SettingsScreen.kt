package com.chacho.pchub.ui

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.chacho.pchub.Prefs
import com.chacho.pchub.net.Pc
import org.json.JSONObject

@Composable
fun SettingsScreen() {
    val scope = rememberCoroutineScope()
    val pcName by Pc.pcName.collectAsState()
    var info by remember { mutableStateOf<JSONObject?>(null) }
    var confirmForget by remember { mutableStateOf(false) }

    fun loadInfo() = scope.pcCall("app.info") { info = it }

    LaunchedEffect(Unit) { loadInfo() }

    Column(
        Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 16.dp)
    ) {
        // ---------- PC Hub en el PC ----------
        SectionTitle("PC Hub en el PC")
        GlassCard(Modifier.fillMaxWidth()) {
            val i = info
            Text(pcName.ifEmpty { "PC" }, style = MaterialTheme.typography.titleMedium, color = Neon.Text)
            Text(
                if (i == null) "…" else "Versión ${i.optString("version")} · " +
                    (if (i.optBoolean("runningInstalled")) "instalado" else "sin instalar"),
                style = MaterialTheme.typography.bodySmall,
                color = Neon.TextDim,
            )
            if (i != null) Text("IA: ${i.optString("ai")}", style = MaterialTheme.typography.bodySmall, color = Neon.TextDim)
            Spacer(Modifier.height(12.dp))

            Spacer(Modifier.height(6.dp))
            SwitchRow(
                "Iniciar PC Hub con Windows",
                info?.optBoolean("startup") == true,
                enabled = info?.optBoolean("installed") == true,
            ) { enabled ->
                scope.pcCall("app.startup", JSONObject().put("enabled", enabled)) { info = it }
            }
            if (info?.optBoolean("installed") == false) {
                Text(
                    "Para iniciar con Windows primero instálalo: clic derecho en el ícono de PC Hub (abajo a la derecha del PC) → Instalar PC Hub en este PC.",
                    style = MaterialTheme.typography.labelSmall,
                    color = Neon.TextFaint,
                )
            }
        }

        // ---------- comportamiento ----------
        SectionTitle("Comportamiento")
        GlassCard(Modifier.fillMaxWidth(), padding = 12.dp) {
            SwitchRow("Abrir el teclado cuando el PC tenga un campo de texto activo", Prefs.autoKeyboard) { Prefs.autoKeyboard = it }
            SwitchRow("Cerrar el teclado al salir del campo de texto", Prefs.autoHideKeyboard) { Prefs.autoHideKeyboard = it }
            SwitchRow("Cambiar de pantalla según la app activa en el PC (ej. música)", Prefs.autoSwitch) { Prefs.autoSwitch = it }
            SwitchRow("Vibración", Prefs.haptics) { Prefs.haptics = it }
        }

        if (Prefs.autoSwitch) {
            SectionTitle("No salir de estos módulos")
            GlassCard(Modifier.fillMaxWidth(), padding = 12.dp) {
                Text(
                    "Si estás en uno de los marcados, el cambio automático no te saca de ahí.",
                    style = MaterialTheme.typography.labelSmall,
                    color = Neon.TextFaint,
                )
                ModuleScreens.forEach { s ->
                    SwitchRow(s.title, s.name in Prefs.autoSwitchLocked) { Prefs.toggleAutoSwitchLocked(s.name) }
                }
            }
        }

        // ---------- apariencia ----------
        SectionTitle("Apariencia")
        GlassCard(Modifier.fillMaxWidth(), padding = 12.dp) {
            SliderRow("Intensidad del neón (colores y brillo)", Prefs.neonIntensity, 0f..1f, decimals = 2) { Prefs.neonIntensity = it }
            Text("Más bajo = colores más suaves y menos resplandor.", style = MaterialTheme.typography.labelSmall, color = Neon.TextFaint)
        }

        // ---------- touchpad ----------
        SectionTitle("Touchpad")
        GlassCard(Modifier.fillMaxWidth(), padding = 12.dp) {
            SliderRow("Sensibilidad", Prefs.sensitivity, 0.5f..4f) { Prefs.sensitivity = it }
            SliderRow("Aceleración", Prefs.acceleration, 0f..3f) { Prefs.acceleration = it }
            SliderRow("Velocidad de desplazamiento", Prefs.scrollSpeed, 0.5f..8f) { Prefs.scrollSpeed = it }
            SwitchRow("Desplazamiento natural (como en el teléfono)", Prefs.naturalScroll) { Prefs.naturalScroll = it }
        }

        // ---------- puntero ----------
        SectionTitle("Puntero (giroscopio)")
        GlassCard(Modifier.fillMaxWidth(), padding = 12.dp) {
            Text("Modo", style = MaterialTheme.typography.bodyMedium, color = Neon.Text)
            Spacer(Modifier.height(6.dp))
            SegmentedTabs(listOf("Aire (relativo)", "Wii (absoluto)"), if (Prefs.pointerMode == "wii") 1 else 0, Modifier.fillMaxWidth()) {
                Prefs.pointerMode = if (it == 1) "wii" else "air"
            }
            Spacer(Modifier.height(6.dp))
            PointerTuning()
            SwitchRow("Sostener de pie (pantalla hacia mí) en vez de acostado como control de Wii", Prefs.pointerUpright) { Prefs.pointerUpright = it }
            SwitchRow("Apuntar siempre (sin mantener B)", Prefs.pointerAlwaysOn) { Prefs.pointerAlwaysOn = it }
            SwitchRow("Botones de volumen como clic izquierdo / derecho", Prefs.pointerVolumeKeys) { Prefs.pointerVolumeKeys = it }
            SwitchRow("Arrastrar y hacer scroll moviendo el teléfono (mantener A apunta; mantener la franja de scroll e inclinar = scroll)", Prefs.pointerMotionGestures) { Prefs.pointerMotionGestures = it }
            SwitchRow("Modo Wii: mostrar la mini pantalla", Prefs.pointerPreview) { Prefs.pointerPreview = it }
        }

        // ---------- PC vinculado ----------
        SectionTitle("PC vinculado")
        GlassCard(Modifier.fillMaxWidth()) {
            Text(pcName.ifEmpty { "—" }, style = MaterialTheme.typography.titleMedium, color = Neon.Text)
            Text("${Prefs.host}:${Prefs.port}", style = MaterialTheme.typography.bodySmall, color = Neon.TextDim)
            Spacer(Modifier.height(12.dp))
            NeonButton("Desvincular y conectar otro PC", Modifier.fillMaxWidth(), accent = Neon.Red, outlined = true) { confirmForget = true }
        }

        Spacer(Modifier.height(24.dp))
        Text("PC Hub (Android)", style = MaterialTheme.typography.labelSmall, color = Neon.TextFaint)
        Spacer(Modifier.height(32.dp))
    }

    if (confirmForget) {
        AlertDialog(
            onDismissRequest = { confirmForget = false },
            containerColor = Neon.Panel,
            title = { Text("¿Desvincular este PC?") },
            text = { Text("Tendrás que ingresar el PIN otra vez para conectarte.") },
            confirmButton = { TextButton(onClick = { confirmForget = false; Pc.forget() }) { Text("Desvincular", color = Neon.Red) } },
            dismissButton = { TextButton(onClick = { confirmForget = false }) { Text("Cancelar", color = Neon.TextDim) } },
        )
    }
}

@Composable
fun SwitchRow(label: String, checked: Boolean, enabled: Boolean = true, onChange: (Boolean) -> Unit) {
    Row(Modifier.fillMaxWidth().padding(vertical = 6.dp), verticalAlignment = Alignment.CenterVertically) {
        Text(
            label,
            style = MaterialTheme.typography.bodyMedium,
            color = if (enabled) Neon.Text else Neon.TextFaint,
            modifier = Modifier.weight(1f).padding(end = 12.dp),
        )
        Switch(
            checked = checked,
            onCheckedChange = onChange,
            enabled = enabled,
            colors = SwitchDefaults.colors(checkedTrackColor = Neon.Cyan, checkedThumbColor = Neon.Bg),
        )
    }
}

@Composable
fun SliderRow(label: String, value: Float, range: ClosedFloatingPointRange<Float>, decimals: Int = 1, onChange: (Float) -> Unit) {
    Column(Modifier.fillMaxWidth().padding(vertical = 4.dp)) {
        Row {
            Text(label, style = MaterialTheme.typography.bodyMedium, color = Neon.Text, modifier = Modifier.weight(1f))
            Text("%.${decimals}f".format(value), style = MaterialTheme.typography.labelMedium, color = Neon.Cyan)
        }
        Slider(
            value = value,
            onValueChange = onChange,
            valueRange = range,
            colors = SliderDefaults.colors(thumbColor = Neon.Cyan, activeTrackColor = Neon.Cyan, inactiveTrackColor = Neon.Text.copy(alpha = 0.12f)),
        )
    }
}
