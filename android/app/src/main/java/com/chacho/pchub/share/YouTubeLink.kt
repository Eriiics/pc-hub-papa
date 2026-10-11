package com.chacho.pchub.share

import java.net.URI

/** Enlace de YouTube / YouTube Music compartido desde otra app. */
data class YouTubeLink(val id: String, val list: String, val music: Boolean) {
    companion object {
        private val urlRx = Regex("""https?://[^\s<>"']+""")
        private val idRx = Regex("""^[A-Za-z0-9_-]{6,20}$""")
        private val listRx = Regex("""^[A-Za-z0-9_-]{8,80}$""")

        /**
         * Busca el primer enlace de YouTube en el texto compartido. Acepta:
         * youtu.be/ID · youtube.com/watch?v=ID · /shorts/ID · /live/ID · /embed/ID · /playlist?list=…
         * y lo mismo en music.youtube.com (y m.youtube.com).
         */
        fun parse(text: String?): YouTubeLink? {
            if (text.isNullOrBlank()) return null
            for (m in urlRx.findAll(text)) {
                val link = parseUrl(m.value.trimEnd('.', ',', ')', ']')) ?: continue
                return link
            }
            return null
        }

        /** Primer enlace http(s) del texto compartido (sea de YouTube o no). */
        fun firstUrl(text: String?): String? {
            if (text.isNullOrBlank()) return null
            return urlRx.find(text)?.value?.trimEnd('.', ',', ')', ']')
        }

        private fun parseUrl(raw: String): YouTubeLink? {
            val uri = runCatching { URI(raw) }.getOrNull() ?: return null
            val host = (uri.host ?: return null).lowercase().removePrefix("www.")
            val query = parseQuery(uri.rawQuery)
            val segments = (uri.path ?: "").split('/').filter { it.isNotEmpty() }
            val music = host == "music.youtube.com"

            val id: String = when {
                host == "youtu.be" -> segments.firstOrNull() ?: ""
                host == "youtube.com" || host == "m.youtube.com" || music -> when (segments.firstOrNull()) {
                    "watch" -> query["v"] ?: ""
                    "shorts", "live", "embed", "v" -> segments.getOrNull(1) ?: ""
                    else -> ""
                }
                else -> return null
            }
            val list = query["list"] ?: ""
            val okId = idRx.matches(id)
            val okList = listRx.matches(list)
            if (!okId && !okList) return null
            return YouTubeLink(if (okId) id else "", if (okList) list else "", music)
        }

        private fun parseQuery(q: String?): Map<String, String> {
            if (q.isNullOrEmpty()) return emptyMap()
            return q.split('&').mapNotNull {
                val i = it.indexOf('=')
                if (i <= 0) null else it.substring(0, i) to java.net.URLDecoder.decode(it.substring(i + 1), "UTF-8")
            }.toMap()
        }
    }
}
