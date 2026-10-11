package com.chacho.pchub.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Computer
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
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
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.chacho.pchub.Prefs
import com.chacho.pchub.net.Discovery
import com.chacho.pchub.net.FoundPc
import com.chacho.pchub.net.Pc
import kotlinx.coroutines.launch

@Composable
fun ConnectScreen() {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val status by Pc.status.collectAsState()
    val error by Pc.error.collectAsState()

    var found by remember { mutableStateOf<List<FoundPc>>(emptyList()) }
    var searching by remember { mutableStateOf(false) }
    var host by remember { mutableStateOf(Prefs.host) }
    var port by remember { mutableStateOf(Prefs.port.toString()) }
    var pin by remember { mutableStateOf("") }

    fun search() {
        if (searching) return
        searching = true
        scope.launch {
            found = Discovery.search(context)
            searching = false
        }
    }

    fun connectTo(h: String, p: Int) {
        if (h != Prefs.host) Prefs.token = "" // otro PC: hay que emparejar de nuevo
        host = h
        port = p.toString()
        pin = ""
        Pc.connect(h, p)
    }

    LaunchedEffect(Unit) { search() }

    Column(
        Modifier
            .fillMaxSize()
            .safeDrawingPadding()
            .imePadding()
            .verticalScroll(rememberScrollState())
            .padding(20.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Spacer(Modifier.height(24.dp))
        Text("PC Hub", fontSize = 40.sp, fontWeight = FontWeight.Black, color = Neon.Cyan)
        Text(
            "Abre PC Hub Agent en tu computador (queda en la bandeja del sistema) y conecta el teléfono a la misma red Wi-Fi.",
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )

        // ---------------- PIN (cuando el PC ya respondió) ----------------
        if (status == Pc.Status.NeedsPin) {
            Box(Modifier.fillMaxWidth().glass(RoundedCornerShape(24.dp), glow = Neon.Cyan, strong = true)) {
                Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                    Text("Emparejar con ${Prefs.host}", style = MaterialTheme.typography.titleMedium)
                    Text("Ingresa el PIN que aparece en el menú del ícono de PC Hub en la bandeja del sistema.",
                        style = MaterialTheme.typography.bodySmall)
                    OutlinedTextField(
                        value = pin,
                        onValueChange = { v ->
                            pin = v.filter { it.isDigit() }.take(6)
                            if (pin.length == 6) Pc.pair(pin)
                        },
                        label = { Text("PIN de 6 dígitos") },
                        singleLine = true,
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword),
                        textStyle = MaterialTheme.typography.headlineSmall.copy(textAlign = TextAlign.Center, letterSpacing = 6.sp),
                        modifier = Modifier.fillMaxWidth(),
                    )
                    Button(onClick = { Pc.pair(pin) }, enabled = pin.length == 6, modifier = Modifier.fillMaxWidth()) {
                        Text("Vincular")
                    }
                }
            }
        }

        if (status == Pc.Status.Connecting) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                CircularProgressIndicator(modifier = Modifier.size(20.dp), strokeWidth = 2.dp)
                Spacer(Modifier.width(10.dp))
                Text("Conectando con ${Prefs.host}…")
            }
        }

        error?.let {
            Text(it, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodySmall)
        }

        if (status == Pc.Status.Failed && Prefs.token.isNotEmpty()) {
            Button(onClick = { Pc.connect(Prefs.host, Prefs.port) }, modifier = Modifier.fillMaxWidth()) {
                Text("Reintentar con ${Prefs.pcName.ifEmpty { Prefs.host }}")
            }
        }

        // ---------------- PCs encontrados ----------------
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text("PCs en tu red", style = MaterialTheme.typography.titleMedium, modifier = Modifier.weight(1f))
            if (searching) CircularProgressIndicator(modifier = Modifier.size(20.dp), strokeWidth = 2.dp)
            else IconButton(onClick = { search() }) { Icon(Icons.Filled.Refresh, contentDescription = "Buscar de nuevo") }
        }

        if (found.isEmpty() && !searching) {
            Text(
                "No se encontró ningún PC. Revisa que el agente esté abierto, que ambos estén en la misma red y que el firewall de Windows lo permita. También puedes escribir la IP abajo.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }

        found.forEach { pc ->
            Box(
                Modifier
                    .fillMaxWidth()
                    .glass(RoundedCornerShape(20.dp), glow = Neon.Violet)
                    .clickable { connectTo(pc.host, pc.port) },
            ) {
                Row(Modifier.padding(16.dp), verticalAlignment = Alignment.CenterVertically) {
                    Icon(Icons.Filled.Computer, contentDescription = null, tint = MaterialTheme.colorScheme.primary)
                    Spacer(Modifier.width(14.dp))
                    Column {
                        Text(pc.name, style = MaterialTheme.typography.titleMedium)
                        Text("${pc.host}:${pc.port}", style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                }
            }
        }

        // ---------------- Manual ----------------
        Text("Conexión manual", style = MaterialTheme.typography.titleMedium, modifier = Modifier.padding(top = 12.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            OutlinedTextField(
                value = host,
                onValueChange = { host = it.trim() },
                label = { Text("IP del PC") },
                placeholder = { Text("192.168.1.20") },
                singleLine = true,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri),
                modifier = Modifier.weight(1f),
            )
            OutlinedTextField(
                value = port,
                onValueChange = { port = it.filter(Char::isDigit).take(5) },
                label = { Text("Puerto") },
                singleLine = true,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                modifier = Modifier.width(100.dp),
            )
        }
        OutlinedButton(
            onClick = { connectTo(host, port.toIntOrNull() ?: 47800) },
            enabled = host.isNotBlank(),
            modifier = Modifier.fillMaxWidth(),
        ) { Text("Conectar") }

        Spacer(Modifier.height(40.dp))
    }
}
