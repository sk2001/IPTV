package app.iptvplayer.remote

import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import java.net.URL
import java.security.MessageDigest
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import javax.net.ssl.HostnameVerifier
import javax.net.ssl.HttpsURLConnection
import javax.net.ssl.SSLContext
import javax.net.ssl.X509TrustManager

/** Appareil appairé, avec l'empreinte épinglée de son certificat et le jeton secret (stocké chiffré). */
@Serializable
data class KnownTarget(val id: String, val name: String, val host: String, val port: Int, val platform: String, val fingerprint: String, val token: String, val accounts: Boolean = false)

/** Appareil vu sur le réseau (annonce mDNS), pas encore forcément appairé. */
data class FoundTarget(val id: String, val name: String, val host: String, val port: Int, val platform: String, val pin: String? = null, val fingerprint: String? = null)

/** Client du protocole v1 : HTTPS avec **épinglage de l'empreinte** du certificat (aucune autorité de confiance n'est utilisée). */
class RemoteClient(private val host: String, private val port: Int, private val expectedFp: String?, private val token: String?) {
    @Volatile var seenFingerprint: String? = null
    private val json = Json { ignoreUnknownKeys = true }

    private val trust = object : X509TrustManager {
        override fun checkClientTrusted(chain: Array<X509Certificate>, authType: String) {}
        override fun checkServerTrusted(chain: Array<X509Certificate>, authType: String) {
            val fp = MessageDigest.getInstance("SHA-256").digest(chain[0].encoded).joinToString("") { "%02x".format(it) }
            seenFingerprint = fp
            if (expectedFp != null && !PairCrypto.constantTimeEquals(fp, expectedFp)) throw CertificateException("Certificate changed")
        }
        override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
    }

    /** Variante brute : corps texte (tableau JSON de la synchro). Retourne (code HTTP, texte). */
    fun callRaw(method: String, path: String, body: String? = null): Pair<Int, String> {
        val ctx = SSLContext.getInstance("TLS").apply { init(null, arrayOf(trust), null) }
        val h = if (host.contains(':')) "[$host]" else host
        val c = URL("https://$h:$port/api/v1$path").openConnection() as HttpsURLConnection
        try {
            c.sslSocketFactory = ctx.socketFactory
            c.hostnameVerifier = HostnameVerifier { _, _ -> true }
            c.requestMethod = method; c.connectTimeout = 5000; c.readTimeout = 60_000
            token?.let { c.setRequestProperty("Authorization", "Bearer $it") }
            if (body != null) { c.doOutput = true; c.setRequestProperty("Content-Type", "application/json"); c.outputStream.use { it.write(body.toByteArray()) } }
            val code = c.responseCode
            return code to ((if (code in 200..299) c.inputStream else c.errorStream)?.bufferedReader()?.use { it.readText() }.orEmpty())
        } finally { c.disconnect() }
    }

    /** Appel bloquant (thread d'arrière-plan). Retourne (code HTTP, corps JSON). */
    fun call(method: String, path: String, body: JsonObject? = null): Pair<Int, JsonObject> {
        val ctx = SSLContext.getInstance("TLS").apply { init(null, arrayOf(trust), null) }
        val h = if (host.contains(':')) "[$host]" else host
        val c = URL("https://$h:$port/api/v1$path").openConnection() as HttpsURLConnection
        try {
            c.sslSocketFactory = ctx.socketFactory
            c.hostnameVerifier = HostnameVerifier { _, _ -> true }     // l'identité est garantie par l'empreinte épinglée
            c.requestMethod = method; c.connectTimeout = 5000; c.readTimeout = 8000
            token?.let { c.setRequestProperty("Authorization", "Bearer $it") }
            if (body != null) {
                c.doOutput = true; c.setRequestProperty("Content-Type", "application/json")
                c.outputStream.use { it.write(body.toString().toByteArray()) }
            }
            val code = c.responseCode
            val text = (if (code in 200..299) c.inputStream else c.errorStream)?.bufferedReader()?.use { it.readText() }.orEmpty()
            return code to (runCatching { json.parseToJsonElement(text).jsonObject }.getOrDefault(JsonObject(emptyMap())))
        } finally { c.disconnect() }
    }

    class PairingSession(val pairingId: String, val clientNonce: String, val serverNonce: String, val fingerprint: String)

    /** Étape 1 : la cible affiche un PIN. */
    fun pairStart(deviceId: String, deviceName: String, platform: String): PairingSession {
        val nonce = PairCrypto.randomHex(16)
        val (code, o) = call("POST", "/pair/start", buildJsonObject {
            put("deviceId", deviceId); put("deviceName", deviceName); put("platform", platform); put("clientNonce", nonce)
        })
        if (code != 200) throw java.io.IOException(o["error"]?.jsonObject?.get("message")?.jsonPrimitive?.contentOrNull ?: "HTTP $code")
        return PairingSession(o["pairingId"]!!.jsonPrimitive.content, nonce, o["serverNonce"]!!.jsonPrimitive.content, seenFingerprint ?: throw java.io.IOException("No TLS"))
    }

    /** Étape 2 : preuve du PIN saisi ; vérifie aussi la preuve du serveur. Retourne (jeton, id, nom de la cible). */
    fun pairConfirm(s: PairingSession, pin: String): Triple<String, String, String> {
        val proof = PairCrypto.proof(pin, false, s.pairingId, s.clientNonce, s.serverNonce, s.fingerprint)
        val (code, o) = call("POST", "/pair/confirm", buildJsonObject { put("pairingId", s.pairingId); put("proof", proof) })
        if (code == 401) throw WrongPin()
        if (code != 200) throw java.io.IOException(o["error"]?.jsonObject?.get("message")?.jsonPrimitive?.contentOrNull ?: "HTTP $code")
        val expected = PairCrypto.proof(pin, true, s.pairingId, s.clientNonce, s.serverNonce, s.fingerprint)
        if (!PairCrypto.constantTimeEquals(expected, o["serverProof"]!!.jsonPrimitive.content)) throw java.io.IOException("Server proof mismatch")
        return Triple(o["token"]!!.jsonPrimitive.content, o["serverId"]!!.jsonPrimitive.content, o["serverName"]!!.jsonPrimitive.content)
    }

    class WrongPin : Exception("Wrong PIN")
}
