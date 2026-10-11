package com.chacho.pchub

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
        setContent {
            PcHubTheme {
                AppRoot()
            }
        }
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
