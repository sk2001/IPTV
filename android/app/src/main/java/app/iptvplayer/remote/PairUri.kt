package app.iptvplayer.remote

import java.net.Inet4Address
import java.net.NetworkInterface
import java.net.URLDecoder
import java.net.URLEncoder

/** Lien d'appairage affiché en QR code par la cible : `iptvplayer://pair?host=<ip>&port=<port>&id=<uuid>` (protocole v1, §2). */
object PairUri {
    fun build(host: String, port: Int, id: String, name: String = "", pin: String? = null, fingerprint: String? = null, platform: String = ""): String =
        "iptvplayer://pair?host=${enc(host)}&port=$port&id=${enc(id)}" + (if (name.isNotEmpty()) "&name=${enc(name)}" else "") +
            (if (pin != null) "&pin=$pin" else "") + (if (fingerprint != null) "&fp=$fingerprint" else "") + (if (platform.isNotEmpty()) "&plat=$platform" else "")

    /** Analyse un lien d'appairage ; null s'il est invalide. L'hôte doit être une adresse locale (pas de domaine public). */
    fun parse(text: String): FoundTarget? {
        val t = text.trim()
        if (!t.startsWith("iptvplayer://pair?", ignoreCase = true)) return null
        val q = t.substringAfter('?').split('&').filter { it.contains('=') }
            .associate { dec(it.substringBefore('=')) to dec(it.substringAfter('=')) }
        val host = q["host"]?.takeIf { it.isNotBlank() } ?: return null
        val port = q["port"]?.toIntOrNull()?.takeIf { it in 1..65535 } ?: return null
        if (!isLocalHost(host)) return null
        val pin = q["pin"]?.takeIf { it.length == 6 && it.all(Char::isDigit) }
        val fp = q["fp"]?.lowercase()?.takeIf { it.length == 64 && it.all { c -> c in "0123456789abcdef" } }
        return FoundTarget(q["id"].orEmpty(), q["name"]?.takeIf { it.isNotBlank() } ?: host, host, port, q["plat"].orEmpty(), pin, fp)
    }

    /** Refuse les noms de domaine et les adresses publiques : l'appairage ne se fait que sur le réseau local. */
    fun isLocalHost(host: String): Boolean {
        val m = Regex("""^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$""").matchEntire(host) ?: return host.startsWith("fe80:", true) || host.startsWith("fd", true) || host.startsWith("fc", true)
        val o = m.groupValues.drop(1).map { it.toInt() }
        if (o.any { it > 255 }) return false
        return o[0] == 10 || (o[0] == 172 && o[1] in 16..31) || (o[0] == 192 && o[1] == 168) || (o[0] == 169 && o[1] == 254) || o[0] == 127
    }

    /** Première adresse IPv4 privée de l'appareil (celle à mettre dans le QR code), null si aucune. */
    fun localIpv4(): String? = runCatching {
        NetworkInterface.getNetworkInterfaces().asSequence()
            .filter { it.isUp && !it.isLoopback }
            .flatMap { it.inetAddresses.asSequence() }
            .filterIsInstance<Inet4Address>()
            .firstOrNull { it.isSiteLocalAddress }?.hostAddress
    }.getOrNull()

    private fun enc(s: String) = URLEncoder.encode(s, "UTF-8")
    private fun dec(s: String) = URLDecoder.decode(s, "UTF-8")
}
