package com.chacho.pchub.net

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.provider.Settings
import androidx.core.content.FileProvider
import com.chacho.pchub.Prefs
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request
import java.io.File
import java.util.concurrent.TimeUnit

/**
 * Actualiza esta app descargando la APK nueva desde el programa del PC (la misma que sale en el QR)
 * y abriendo el instalador de Android. Android siempre pide un toque en "Instalar"; no se puede evitar.
 */
object AppUpdater {
    private val client = OkHttpClient.Builder()
        .connectTimeout(5, TimeUnit.SECONDS)
        .readTimeout(60, TimeUnit.SECONDS)
        .build()

    /** ¿Android ya dejó a PC Hub instalar apps? Si no, hay que mandar a la persona a darle el permiso (una vez). */
    fun canInstall(context: Context) = context.packageManager.canRequestPackageInstalls()

    /** Abre la pantalla de Android donde se da el permiso para instalar desde PC Hub. */
    fun askPermission(context: Context) {
        context.startActivity(
            Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:${context.packageName}"))
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        )
    }

    /** Descarga la APK del PC y abre el instalador. Lanza una excepción con un mensaje claro si algo falla. */
    suspend fun downloadAndInstall(context: Context) = withContext(Dispatchers.IO) {
        val dir = File(context.cacheDir, "updates").apply { mkdirs() }
        val file = File(dir, "PcHub.apk")
        val request = Request.Builder().url("http://${Prefs.host}:${Prefs.port}/apk").build()
        try {
            client.newCall(request).execute().use { response ->
                if (!response.isSuccessful) throw PcError("El PC no tiene la app nueva a mano (error ${response.code}). Ábrelo desde la carpeta nueva y vuelve a intentar.")
                val body = response.body ?: throw PcError("El PC no mandó la app.")
                file.outputStream().use { out -> body.byteStream().copyTo(out) }
            }
        } catch (e: PcError) {
            throw e
        } catch (e: Exception) {
            throw PcError("No pude descargar la app desde el PC. Revisa que el teléfono siga en el mismo Wi-Fi.")
        }
        val uri = FileProvider.getUriForFile(context, "${context.packageName}.update", file)
        context.startActivity(
            Intent(Intent.ACTION_VIEW)
                .setDataAndType(uri, "application/vnd.android.package-archive")
                .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_ACTIVITY_NEW_TASK)
        )
    }
}
