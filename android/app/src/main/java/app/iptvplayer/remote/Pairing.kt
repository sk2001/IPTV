package app.iptvplayer.remote

import kotlinx.serialization.Serializable
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import java.io.File
import java.security.MessageDigest
import java.security.SecureRandom
import kotlin.io.encoding.Base64
import kotlin.io.encoding.ExperimentalEncodingApi
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

/** Cryptographie de l'appairage (protocole v1, §4). Pur JVM : testable sans appareil. */
@OptIn(ExperimentalEncodingApi::class)
object PairCrypto {
    private val rnd = SecureRandom()

    fun randomToken(bytes: Int = 32): String = Base64.UrlSafe.withPadding(Base64.PaddingOption.ABSENT).encode(ByteArray(bytes).also(rnd::nextBytes))
    fun randomHex(bytes: Int): String = ByteArray(bytes).also(rnd::nextBytes).joinToString("") { "%02x".format(it) }
    fun newPin(): String = "%06d".format(rnd.nextInt(1_000_000))

    fun sha256Hex(s: String): String = MessageDigest.getInstance("SHA-256").digest(s.toByteArray()).joinToString("") { "%02x".format(it) }

    /**
     * Preuve de connaissance du PIN liée à l'empreinte du certificat : HMAC-SHA256 (clé = PIN) de
     * `label \n pairingId \n clientNonce \n serverNonce \n empreinte`. Label : `iptvplayer-pair-v1` (client) ou `iptvplayer-pair-v1-server`.
     */
    fun proof(pin: String, server: Boolean, pairingId: String, clientNonce: String, serverNonce: String, fingerprint: String): String {
        val mac = Mac.getInstance("HmacSHA256").apply { init(SecretKeySpec(pin.toByteArray(), "HmacSHA256")) }
        val label = if (server) "iptvplayer-pair-v1-server" else "iptvplayer-pair-v1"
        val msg = listOf(label, pairingId, clientNonce, serverNonce, fingerprint.lowercase()).joinToString("\n")
        return mac.doFinal(msg.toByteArray()).joinToString("") { "%02x".format(it) }
    }

    fun constantTimeEquals(a: String, b: String): Boolean = MessageDigest.isEqual(a.toByteArray(), b.toByteArray())
}

@Serializable
data class PairedDevice(val id: String, val name: String, val platform: String, val tokenHash: String, val pairedAt: Long, val lastUsed: Long = 0)

@Serializable
private data class DeviceFile(val devices: List<PairedDevice> = emptyList())

/** Appareils appairés : seul le SHA-256 du jeton est conservé. Fichier facultatif (null = mémoire seule, pour les tests). */
class DeviceRegistry(private val file: File?) {
    private val json = Json { ignoreUnknownKeys = true }
    private val lock = Any()
    private var list: List<PairedDevice> = file?.takeIf { it.exists() }?.let { runCatching { json.decodeFromString<DeviceFile>(it.readText()).devices }.getOrNull() } ?: emptyList()

    fun all(): List<PairedDevice> = synchronized(lock) { list }

    fun add(d: PairedDevice) = synchronized(lock) { list = list.filterNot { it.id == d.id } + d; save() }
    fun remove(id: String) = synchronized(lock) { list = list.filterNot { it.id == id }; save() }
    fun findByToken(token: String): PairedDevice? = synchronized(lock) {
        val h = PairCrypto.sha256Hex(token); list.firstOrNull { PairCrypto.constantTimeEquals(it.tokenHash, h) }
    }
    fun touch(id: String, now: Long) = synchronized(lock) { list = list.map { if (it.id == id) it.copy(lastUsed = now) else it } }
    fun flush() = synchronized(lock) { save() }

    private fun save() {
        val f = file ?: return
        runCatching { f.writeText(json.encodeToString(DeviceFile(list))) }
    }
}

class PairStart(val pairingId: String, val serverNonce: String, val expiresInSec: Int)
sealed interface PairResult {
    class Ok(val token: String, val serverProof: String, val device: PairedDevice) : PairResult
    data object Invalid : PairResult      // preuve fausse (le PIN reste valable tant qu'il reste des essais)
    data object Expired : PairResult      // pas d'appairage en cours, expiré ou trop d'essais
}

/** Un seul appairage à la fois ; PIN à 6 chiffres valable 120 s, 5 essais, 5 démarrages par minute. */
class PairingManager(private val registry: DeviceRegistry, private val clock: () -> Long = System::currentTimeMillis) {
    private class Pending(val id: String, val pin: String, val deviceId: String, val deviceName: String, val platform: String,
                          val clientNonce: String, val serverNonce: String, val expiresAt: Long, val fromOffer: Boolean = false, var attempts: Int = 0)

    private val lock = Any()
    private var pending: Pending? = null
    private val starts = ArrayDeque<Long>()

    // Offre : PIN a usage unique valable 5 minutes, affiche aussi dans le QR code (connexion simplifiee / configuration d'un nouvel appareil).
    private var offerPin: String? = null
    private var offerExpires = 0L
    private var offerFailures = 0
    private val provisioned = HashMap<String, Long>()      // appareil configure par une offre -> fin de l'autorisation de recevoir les comptes

    val offerActive: Boolean get() = synchronized(lock) { offerPin != null && clock() < offerExpires }

    /** Ouvre une offre et retourne son PIN (a mettre dans le QR code). */
    fun openOffer(ttlMs: Long = 300_000): String {
        val pin = PairCrypto.newPin()
        synchronized(lock) { offerPin = pin; offerExpires = clock() + ttlMs; offerFailures = 0 }
        onChange(); return pin
    }

    fun closeOffer() { synchronized(lock) { offerPin = null }; onChange() }

    /** Cet appareil a ete appaire via une offre recente : il peut recevoir / envoyer les comptes (mots de passe compris). */
    fun accountsAllowed(deviceId: String): Boolean = synchronized(lock) { (provisioned[deviceId] ?: 0) > clock() }

    /** PIN à afficher sur l'écran de la cible (null = aucun appairage en cours). */
    val currentPin: String? get() = synchronized(lock) {
        pending?.takeIf { clock() < it.expiresAt }?.pin ?: offerPin?.takeIf { clock() < offerExpires }
    }
    var onChange: () -> Unit = {}

    fun start(deviceId: String, deviceName: String, platform: String, clientNonce: String): PairStart? {
        val s = synchronized(lock) {
            val now = clock()
            while (starts.isNotEmpty() && now - starts.first() > 60_000) starts.removeFirst()
            if (starts.size >= 5) return null
            starts.addLast(now)
            val offer = offerPin != null && now < offerExpires
            val p = Pending(PairCrypto.randomHex(8), if (offer) offerPin!! else PairCrypto.newPin(), deviceId.take(64), deviceName.take(64), platform.take(16), clientNonce.take(128), PairCrypto.randomHex(16), now + 120_000, offer)
            pending = p
            PairStart(p.id, p.serverNonce, 120)
        }
        onChange(); return s
    }

    fun confirm(pairingId: String, proof: String, fingerprint: String): PairResult {
        val result = synchronized(lock) {
            val p = pending
            if (p == null || p.id != pairingId || clock() >= p.expiresAt) return@synchronized PairResult.Expired
            p.attempts++
            val expected = PairCrypto.proof(p.pin, false, p.id, p.clientNonce, p.serverNonce, fingerprint)
            if (!PairCrypto.constantTimeEquals(expected, proof.lowercase())) {
                if (p.attempts >= 5) pending = null
                if (p.fromOffer && ++offerFailures >= 10) offerPin = null      // trop d'essais : l'offre est annulee
                return@synchronized if (pending == null) PairResult.Expired else PairResult.Invalid
            }
            pending = null
            if (p.fromOffer) { offerPin = null; provisioned[p.deviceId] = clock() + 10 * 60_000L }       // offre a usage unique
            val token = PairCrypto.randomToken()
            val dev = PairedDevice(p.deviceId, p.deviceName, p.platform, PairCrypto.sha256Hex(token), clock())
            registry.add(dev)
            PairResult.Ok(token, PairCrypto.proof(p.pin, true, p.id, p.clientNonce, p.serverNonce, fingerprint), dev)
        }
        onChange(); return result
    }

    fun cancel() { synchronized(lock) { pending = null }; onChange() }
}
