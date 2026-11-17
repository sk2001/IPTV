package app.iptvplayer.remote

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import java.net.Inet6Address
import java.net.InetAddress

const val PROTOCOL = 1

/** Ce que la cible sait faire ; l'implémentation Android gère le passage sur le thread principal. */
interface ControlTarget {
    val deviceId: String
    val deviceName: String
    val platform: String
    val appVersion: String
    fun stateJson(): JsonObject
    /** Exécute une commande ; retourne null si acceptée, sinon un message d'erreur (HTTP 422). */
    fun command(cmd: JsonObject): String?
    /** Synchronisation (§8) : (code HTTP, corps JSON) ou null si la route est inconnue. */
    fun sync(deviceId: String, method: String, collection: String, body: String): Pair<Int, String>? = null
}

class HttpRequest(val method: String, val path: String, val query: Map<String, String>, val headers: Map<String, String>, val body: String, val remote: InetAddress?)
class HttpResponse(val status: Int, val body: String)

/** Routage et sécurité du protocole v1 (§3 et §5), indépendant du transport pour être testé sur le JVM. */
class ControlApi(
    private val target: ControlTarget,
    private val registry: DeviceRegistry,
    private val pairing: PairingManager,
    private val fingerprint: () -> String,
    private val enabled: () -> Boolean = { true },
    private val clock: () -> Long = System::currentTimeMillis,
) {
    private val json = Json { ignoreUnknownKeys = true }
    private val hits = HashMap<String, ArrayDeque<Long>>()

    private fun err(status: Int, code: String, msg: String) = HttpResponse(status, buildJsonObject {
        put("error", buildJsonObject { put("code", code); put("message", msg) })
    }.toString())

    private fun ok(o: JsonObject = buildJsonObject { put("ok", true) }) = HttpResponse(200, o.toString())

    /** Jeton Bearer -> appareil appairé. */
    fun authenticate(req: HttpRequest): PairedDevice? {
        val token = req.headers["authorization"]?.removePrefix("Bearer ")?.trim()?.takeIf { it.isNotEmpty() } ?: return null
        val dev = registry.findByToken(token) ?: return null
        registry.touch(dev.id, clock())
        return dev
    }

    /** 120 requêtes par minute et par appareil. */
    private fun rateLimited(id: String): Boolean = synchronized(hits) {
        val now = clock(); val q = hits.getOrPut(id) { ArrayDeque() }
        while (q.isNotEmpty() && now - q.first() > 60_000) q.removeFirst()
        if (q.size >= 120) true else { q.addLast(now); false }
    }

    fun handle(req: HttpRequest): HttpResponse {
        if (!isPrivate(req.remote)) return err(403, "forbidden_network", "Local network only")
        val p = req.path.trimEnd('/')
        if (!p.startsWith("/api/v1/")) return err(404, "not_found", "Unknown path")
        val route = p.removePrefix("/api/v1")
        try {
            // Sans authentification : info et appairage.
            if (route == "/info" && req.method == "GET") return ok(info())
            if (route.startsWith("/pair/")) return pair(route, req)
            if (!enabled()) return err(403, "remote_disabled", "Remote control is disabled")
            val dev = authenticate(req) ?: return err(401, "unauthorized", "Pair this device first")
            if (rateLimited(dev.id)) return err(429, "rate_limited", "Too many requests")
            return when {
                route == "/pair" && req.method == "DELETE" -> { registry.remove(dev.id); pairing.onChange(); ok() }
                route == "/state" && req.method == "GET" -> ok(target.stateJson())
                route == "/command" && req.method == "POST" -> command(req)
                route.startsWith("/sync/") -> target.sync(dev.id, req.method, route.removePrefix("/sync/"), req.body)?.let { HttpResponse(it.first, it.second) } ?: err(404, "not_found", "Unknown collection")
                else -> err(404, "not_found", "Unknown route")
            }
        } catch (e: Exception) {
            return err(400, "bad_request", e.message ?: "Bad request")
        }
    }

    private fun info() = buildJsonObject {
        put("protocol", PROTOCOL); put("id", target.deviceId); put("name", target.deviceName); put("platform", target.platform)
        put("appVersion", target.appVersion); put("remoteEnabled", enabled()); put("paired", registry.all().isNotEmpty())
    }

    private fun body(req: HttpRequest): JsonObject = json.parseToJsonElement(req.body.ifBlank { "{}" }).jsonObject
    private fun JsonObject.str(k: String) = this[k]?.jsonPrimitive?.contentOrNull

    private fun pair(route: String, req: HttpRequest): HttpResponse {
        if (!enabled()) return err(403, "remote_disabled", "Remote control is disabled")
        if (req.method != "POST") return err(404, "not_found", "Unknown route")
        val b = body(req)
        return when (route) {
            "/pair/start" -> {
                val deviceId = b.str("deviceId") ?: return err(400, "bad_request", "deviceId")
                val nonce = b.str("clientNonce") ?: return err(400, "bad_request", "clientNonce")
                val s = pairing.start(deviceId, b.str("deviceName") ?: "Device", b.str("platform") ?: "unknown", nonce)
                    ?: return err(429, "rate_limited", "Too many pairing attempts")
                ok(buildJsonObject { put("pairingId", s.pairingId); put("serverNonce", s.serverNonce); put("expiresIn", s.expiresInSec) })
            }
            "/pair/confirm" -> when (val r = pairing.confirm(b.str("pairingId").orEmpty(), b.str("proof").orEmpty(), fingerprint())) {
                is PairResult.Ok -> ok(buildJsonObject {
                    put("token", r.token); put("serverId", target.deviceId); put("serverName", target.deviceName); put("serverProof", r.serverProof)
                })
                PairResult.Invalid -> err(401, "unauthorized", "Wrong PIN")
                PairResult.Expired -> err(409, "busy", "Pairing expired or cancelled")
            }
            else -> err(404, "not_found", "Unknown route")
        }
    }

    private fun command(req: HttpRequest): HttpResponse {
        val b = body(req)
        val type = b.str("type") ?: return err(400, "bad_request", "type")
        if (type !in COMMANDS) return err(422, "invalid_command", "Unknown command: $type")
        val e = target.command(b)
        return if (e == null) ok() else err(422, "invalid_command", e)
    }

    companion object {
        val COMMANDS = setOf("play", "playRef", "pause", "resume", "toggle", "stop", "seek", "seekBy", "volume", "volumeBy", "mute", "next", "prev", "audioTrack", "subtitleTrack", "fullscreen", "record")

        /** Adresses privées uniquement (RFC 1918, lien local, boucle locale, fc00::/7). */
        fun isPrivate(a: InetAddress?): Boolean {
            if (a == null) return false
            if (a.isLoopbackAddress || a.isSiteLocalAddress || a.isLinkLocalAddress) return true
            if (a is Inet6Address) return (a.address[0].toInt() and 0xFE) == 0xFC
            return false
        }
    }
}
