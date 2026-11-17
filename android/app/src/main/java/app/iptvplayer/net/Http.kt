package app.iptvplayer.net

import java.io.InputStream
import java.net.HttpURLConnection
import java.net.URL
import java.util.zip.GZIPInputStream

/** Requêtes HTTP simples (bloquantes : à appeler depuis Dispatchers.IO). */
object Http {
    // Certains fournisseurs bloquent les User-Agent inconnus.
    private const val USER_AGENT = "VLC/3.0.21 LibVLC/3.0.21"

    fun open(url: String, timeoutMs: Int = 60_000): InputStream {
        val c = URL(url).openConnection() as HttpURLConnection
        c.connectTimeout = 20_000
        c.readTimeout = timeoutMs
        c.instanceFollowRedirects = true
        c.setRequestProperty("User-Agent", USER_AGENT)
        c.setRequestProperty("Accept-Encoding", "gzip")
        val code = c.responseCode
        if (code !in 200..299) { c.disconnect(); throw java.io.IOException("HTTP $code") }
        val raw = c.inputStream
        return if (c.contentEncoding.equals("gzip", ignoreCase = true)) GZIPInputStream(raw) else raw
    }

    fun getString(url: String, timeoutMs: Int = 60_000): String =
        open(url, timeoutMs).bufferedReader(Charsets.UTF_8).use { it.readText() }
}
