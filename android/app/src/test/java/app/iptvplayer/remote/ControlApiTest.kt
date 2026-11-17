package app.iptvplayer.remote

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.net.InetAddress

private class FakeTarget : ControlTarget {
    override val deviceId = "srv"; override val deviceName = "Salon"; override val platform = "androidtv"; override val appVersion = "1"
    val commands = mutableListOf<JsonObject>()
    override fun stateJson() = buildJsonObject { put("state", "idle") }
    override fun command(cmd: JsonObject): String? { commands += cmd; return if (cmd["type"]!!.jsonPrimitive.content == "playRef") "not found" else null }
}

class ControlApiTest {
    private val lan = InetAddress.getByName("192.168.1.20")
    private val target = FakeTarget()
    private var now = 1_000_000L
    private val registry = DeviceRegistry(null)
    private val pairing = PairingManager(registry) { now }
    private val api = ControlApi(target, registry, pairing, { "abcd" }, { true }) { now }
    private val json = Json

    private fun call(method: String, path: String, body: String = "", token: String? = null, from: InetAddress = lan) =
        api.handle(HttpRequest(method, path, emptyMap(), if (token != null) mapOf("authorization" to "Bearer $token") else emptyMap(), body, from))

    /** Déroule un appairage complet avec le bon PIN ; retourne le jeton. */
    private fun pair(pin: String? = null): String {
        val s = call("POST", "/api/v1/pair/start", """{"deviceId":"pc","deviceName":"PC","platform":"windows","clientNonce":"cn"}""")
        assertEquals(200, s.status)
        val o = json.parseToJsonElement(s.body).jsonObject
        val id = o["pairingId"]!!.jsonPrimitive.content; val sn = o["serverNonce"]!!.jsonPrimitive.content
        val usedPin = pin ?: pairing.currentPin!!
        val proof = PairCrypto.proof(usedPin, false, id, "cn", sn, "abcd")
        val c = call("POST", "/api/v1/pair/confirm", """{"pairingId":"$id","proof":"$proof"}""")
        if (c.status != 200) return ""
        val r = json.parseToJsonElement(c.body).jsonObject
        assertEquals(PairCrypto.proof(usedPin, true, id, "cn", sn, "abcd").length, r["serverProof"]!!.jsonPrimitive.content.length)
        return r["token"]!!.jsonPrimitive.content
    }

    @Test fun infoIsPublicButNetworkIsChecked() {
        assertEquals(200, call("GET", "/api/v1/info").status)
        assertEquals(403, call("GET", "/api/v1/info", from = InetAddress.getByName("8.8.8.8")).status)
    }

    @Test fun stateNeedsToken() {
        assertEquals(401, call("GET", "/api/v1/state").status)
        assertEquals(401, call("GET", "/api/v1/state", token = "nope").status)
    }

    @Test fun pairingThenCommands() {
        val token = pair()
        assertTrue(token.isNotEmpty())
        assertEquals(200, call("GET", "/api/v1/state", token = token).status)
        assertEquals(200, call("POST", "/api/v1/command", """{"type":"pause"}""", token).status)
        assertEquals("pause", target.commands.single()["type"]!!.jsonPrimitive.content)
        assertEquals(422, call("POST", "/api/v1/command", """{"type":"explode"}""", token).status)
        assertEquals(422, call("POST", "/api/v1/command", """{"type":"playRef"}""", token).status)
        assertEquals(1, registry.all().size)
        assertTrue(registry.all()[0].tokenHash != token)            // seul le hash est conservé
        assertEquals(200, call("DELETE", "/api/v1/pair", token = token).status)
        assertEquals(401, call("GET", "/api/v1/state", token = token).status)
    }

    @Test fun wrongPinFailsAndFifthAttemptCancels() {
        val s = json.parseToJsonElement(call("POST", "/api/v1/pair/start", """{"deviceId":"pc","deviceName":"PC","platform":"windows","clientNonce":"cn"}""").body).jsonObject
        val id = s["pairingId"]!!.jsonPrimitive.content
        val statuses = (1..5).map { call("POST", "/api/v1/pair/confirm", """{"pairingId":"$id","proof":"00"}""").status }
        assertEquals(listOf(401, 401, 401, 401, 409), statuses)
        assertNull(pairing.currentPin)
    }

    @Test fun pinBoundToCertificateFingerprint() {
        // Un intercepteur TLS voit une autre empreinte : sa preuve ne correspond pas.
        val s = json.parseToJsonElement(call("POST", "/api/v1/pair/start", """{"deviceId":"pc","deviceName":"PC","platform":"windows","clientNonce":"cn"}""").body).jsonObject
        val id = s["pairingId"]!!.jsonPrimitive.content; val sn = s["serverNonce"]!!.jsonPrimitive.content
        val bad = PairCrypto.proof(pairing.currentPin!!, false, id, "cn", sn, "other-fingerprint")
        assertEquals(401, call("POST", "/api/v1/pair/confirm", """{"pairingId":"$id","proof":"$bad"}""").status)
    }

    @Test fun pinExpires() {
        call("POST", "/api/v1/pair/start", """{"deviceId":"pc","deviceName":"PC","platform":"windows","clientNonce":"cn"}""")
        assertNotNull(pairing.currentPin)
        now += 121_000
        assertNull(pairing.currentPin)
    }

    @Test fun pairingStartsAreLimited() {
        val codes = (1..6).map { call("POST", "/api/v1/pair/start", """{"deviceId":"pc","deviceName":"PC","platform":"windows","clientNonce":"cn"}""").status }
        assertEquals(listOf(200, 200, 200, 200, 200, 429), codes)
    }

    @Test fun requestsAreRateLimited() {
        val token = pair()
        val codes = (1..121).map { call("GET", "/api/v1/state", token = token).status }
        assertEquals(429, codes.last())
        assertFalse(codes.dropLast(1).any { it != 200 })
    }

    @Test fun privateAddresses() {
        assertTrue(ControlApi.isPrivate(InetAddress.getByName("10.0.2.2")))
        assertTrue(ControlApi.isPrivate(InetAddress.getByName("172.16.5.1")))
        assertTrue(ControlApi.isPrivate(InetAddress.getByName("fd12:3456::1")))
        assertFalse(ControlApi.isPrivate(InetAddress.getByName("93.184.216.34")))
        assertFalse(ControlApi.isPrivate(null))
    }
}
