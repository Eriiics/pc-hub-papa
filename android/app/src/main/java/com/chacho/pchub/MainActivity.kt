package com.chacho.pchub

import android.content.Intent
import android.os.Bundle
import android.view.KeyEvent
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import com.chacho.pchub.net.Pc
import com.chacho.pchub.ui.AppRoot
import com.chacho.pchub.ui.HardwareKeys
import com.chacho.pchub.ui.PcHubTheme
import com.chacho.pchub.ui.Toaster

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        Prefs.init(applicationContext)
        Toaster.init(applicationContext)
        enableEdgeToEdge()
        if (savedInstanceState == null) conectarDesdeEnlace(intent)
        setContent {
            PcHubTheme {
                AppRoot()
            }
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        conectarDesdeEnlace(intent)
    }

    /** Enlace de la página de instalación (pchub://conectar?host=…&port=…&pin=…): conecta y empareja solo. */
    private fun conectarDesdeEnlace(intent: Intent?) {
        val uri = intent?.data ?: return
        if (uri.host != "conectar") return
        val host = uri.getQueryParameter("host").orEmpty()
        val pin = uri.getQueryParameter("pin").orEmpty()
        val port = uri.getQueryParameter("port")?.toIntOrNull() ?: 47800
        if (host.isEmpty() || pin.isEmpty()) return
        Pc.connect(host, port, pin)
    }

    override fun onResume() {
        super.onResume()
        // Al volver a la app, reconectar si se había perdido la conexión
        Pc.ensureConnected()
    }

    /** Los botones físicos (volumen) pueden usarse como gatillos en el puntero. */
    override fun dispatchKeyEvent(event: KeyEvent): Boolean {
        val handler = HardwareKeys.handler
        if (handler != null && (event.action == KeyEvent.ACTION_DOWN || event.action == KeyEvent.ACTION_UP)) {
            if (handler(event.keyCode, event.action == KeyEvent.ACTION_DOWN, event.repeatCount)) return true
        }
        return super.dispatchKeyEvent(event)
    }
}
