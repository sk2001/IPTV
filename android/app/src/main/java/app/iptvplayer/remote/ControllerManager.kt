package app.iptvplayer.remote

import android.app.Application
import android.content.Context
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import app.iptvplayer.AppViewModel
import app.iptvplayer.model.Kind
import app.iptvplayer.sync.SyncEngine
import app.iptvplayer.ui.isTelevision
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlinx.serialization.builtins.ListSerializer
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import kotlinx.serialization.json.putJsonObject
import java.io.File
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

/** Cette appli comme **contrôleur** : découverte des cibles, appairage, pilotage et « envoyer vers ». */
class ControllerManager(private val app: Application, private val vm: AppViewModel, private val scope: CoroutineScope) {
    private val json = Json { ignoreUnknownKeys = true }
    private val file = File(app.filesDir, "remote-targets.bin")
    private val serializer = ListSerializer(KnownTarget.serializer())

    private val _known = MutableStateFlow(loadKnown())
    val known: StateFlow<List<KnownTarget>> = _known.asStateFlow()
    private val _found = MutableStateFlow<List<FoundTarget>>(emptyList())
    val found: StateFlow<List<FoundTarget>> = _found.asStateFlow()
    private val _selected = MutableStateFlow<KnownTarget?>(null)
    val selected: StateFlow<KnownTarget?> = _selected.asStateFlow()
    private val _state = MutableStateFlow<JsonObject?>(null)
    val remoteState: StateFlow<JsonObject?> = _state.asStateFlow()
    private val _message = MutableStateFlow<String?>(null)
    /** Dernière erreur ou information (texte brut, affiché tel quel). */
    val message: StateFlow<String?> = _message.asStateFlow()

    private val platform get() = if (isTelevision(app)) "androidtv" else "android"

    // ------------------------------------------------------------------ Découverte (mDNS)

    private val nsd = app.getSystemService(Context.NSD_SERVICE) as? NsdManager
    private var discovery: NsdManager.DiscoveryListener? = null
    private val pending = ArrayDeque<NsdServiceInfo>()
    private var resolving = false

    fun startDiscovery() {
        val mgr = nsd ?: return
        if (discovery != null) return
        val l = object : NsdManager.DiscoveryListener {
            override fun onDiscoveryStarted(t: String) {}
            override fun onDiscoveryStopped(t: String) {}
            override fun onStartDiscoveryFailed(t: String, e: Int) { discovery = null }
            override fun onStopDiscoveryFailed(t: String, e: Int) {}
            override fun onServiceFound(i: NsdServiceInfo) { synchronized(pending) { pending.addLast(i) }; resolveNext() }
            override fun onServiceLost(i: NsdServiceInfo) { _found.value = _found.value.filterNot { it.name == i.serviceName } }
        }
        discovery = l
        runCatching { mgr.discoverServices("_iptvplayer._tcp", NsdManager.PROTOCOL_DNS_SD, l) }.onFailure { discovery = null }
    }

    fun stopDiscovery() {
        discovery?.let { l -> runCatching { nsd?.stopServiceDiscovery(l) } }
        discovery = null; _found.value = emptyList()
    }

    @Suppress("DEPRECATION")
    private fun resolveNext() {
        val mgr = nsd ?: return
        val next = synchronized(pending) { if (resolving) return; pending.removeFirstOrNull()?.also { resolving = true } } ?: return
        runCatching {
            mgr.resolveService(next, object : NsdManager.ResolveListener {
                override fun onResolveFailed(i: NsdServiceInfo, e: Int) { synchronized(pending) { resolving = false }; resolveNext() }
                override fun onServiceResolved(i: NsdServiceInfo) {
                    val a = i.attributes
                    fun attr(k: String) = a[k]?.toString(Charsets.UTF_8).orEmpty()
                    val id = attr("id")
                    val host = i.host?.hostAddress
                    if (id.isNotEmpty() && host != null && id != vm.remote.deviceId) {
                        val f = FoundTarget(id, attr("name").ifEmpty { i.serviceName }, host, i.port, attr("platform"))
                        _found.value = _found.value.filterNot { it.id == id } + f
                    }
                    synchronized(pending) { resolving = false }; resolveNext()
                }
            })
        }.onFailure { synchronized(pending) { resolving = false } }
    }

    // ------------------------------------------------------------------ Appairage

    private var session: RemoteClient.PairingSession? = null
    private var sessionTarget: FoundTarget? = null

    /** Demande à la cible d'afficher un PIN. Retourne null si tout va bien, sinon un message d'erreur. */
    suspend fun startPairing(t: FoundTarget): String? = withContext(Dispatchers.IO) {
        try {
            val s = RemoteClient(t.host, t.port, t.fingerprint, null).pairStart(vm.remote.deviceId, vm.remote.deviceName, platform)
            session = s; sessionTarget = t; null
        } catch (e: Exception) { e.message ?: e.javaClass.simpleName }
    }

    /** Valide le PIN saisi. Retourne null si l'appairage a réussi, "pin" si le PIN est faux, sinon un message d'erreur. */
    suspend fun confirmPairing(pin: String): String? = withContext(Dispatchers.IO) {
        val s = session ?: return@withContext "No pairing in progress"
        val t = sessionTarget ?: return@withContext "No pairing in progress"
        try {
            val (token, id, name) = RemoteClient(t.host, t.port, s.fingerprint, null).pairConfirm(s, pin)
            saveKnown(_known.value.filterNot { it.id == id } + KnownTarget(id, name, t.host, t.port, t.platform, s.fingerprint, token))
            session = null; null
        } catch (e: RemoteClient.WrongPin) { "pin" }
        catch (e: Exception) { session = null; e.message ?: e.javaClass.simpleName }
    }

    /** Lien de QR code avec PIN : appairage sans saisie (empreinte du certificat verifiee si fournie). null = ok, sinon message d'erreur. */
    suspend fun pairFromLink(t: FoundTarget): String? {
        val pin = t.pin ?: return "No PIN in this code"
        startPairing(t)?.let { return it }
        return confirmPairing(pin).let { if (it == "pin") "Wrong or expired code" else it }
    }

    /** Oublie une cible localement (sans la prevenir) : utilise apres une configuration depuis un PC qui ferme son serveur. */
    fun dropLocal(id: String) { if (_selected.value?.id == id) disconnect(); saveKnown(_known.value.filterNot { it.id == id }) }

    fun forget(t: KnownTarget) {
        if (_selected.value?.id == t.id) disconnect()
        saveKnown(_known.value.filterNot { it.id == t.id })
        scope.launch(Dispatchers.IO) { runCatching { clientOf(t).call("DELETE", "/pair") } }   // au mieux
    }

    // ------------------------------------------------------------------ Pilotage

    private var poll: Job? = null
    private fun clientOf(t: KnownTarget) = RemoteClient(t.host, t.port, t.fingerprint, t.token)

    /** Sélectionne une cible et suit son état une fois par seconde. */
    fun connect(t: KnownTarget) {
        poll?.cancel(); _selected.value = t; _state.value = null; _message.value = null
        poll = scope.launch(Dispatchers.IO) {
            while (true) {
                try {
                    val (code, o) = clientOf(t).call("GET", "/state")
                    if (code == 200) { _state.value = o; _message.value = null }
                    else if (code == 401) { _message.value = "Unauthorized: pair this device again"; break }
                } catch (e: Exception) { _message.value = e.message ?: e.javaClass.simpleName }
                delay(1000)
            }
        }
    }

    fun disconnect() { poll?.cancel(); poll = null; _selected.value = null; _state.value = null; _message.value = null }

    fun send(cmd: JsonObject) {
        val t = _selected.value ?: return
        scope.launch(Dispatchers.IO) { sendTo(t, cmd) }
    }

    private fun sendTo(t: KnownTarget, cmd: JsonObject): String? = try {
        val (code, o) = clientOf(t).call("POST", "/command", cmd)
        if (code == 200) null else (o["error"] as? JsonObject)?.get("message").toString().trim('"')
    } catch (e: Exception) { e.message ?: e.javaClass.simpleName }

    fun command(type: String, vararg extra: Pair<String, Any>) = send(buildJsonObject {
        put("type", type)
        extra.forEach { (k, v) -> when (v) { is Number -> put(k, v); is Boolean -> put(k, v); else -> put(k, v.toString()) } }
    })

    /** Synchronise favoris, historique, préférences (et comptes si activé des deux côtés) avec la cible. Retourne un résumé ou un message d'erreur. */
    suspend fun syncWith(t: KnownTarget, forceAccounts: Boolean = false): String = withContext(Dispatchers.IO) {
        try {
            val client = clientOf(t); val engine = vm.sync
            var received = 0; var sent = 0; var skipped = false
            for (c in engine.collections) {
                if (c == "accounts" && !forceAccounts && !vm.remote.syncAccounts.value) continue
                val (code, text) = client.callRaw("GET", "/sync/$c")
                if (code == 403) { skipped = true; continue }
                if (code != 200) return@withContext "HTTP $code"
                val remote = SyncEngine.fromJson(text)
                received += engine.merge(c, remote)
                val push = engine.missingOn(c, remote.associateBy { it.id })
                if (push.isNotEmpty()) {
                    val (pc, _) = client.callRaw("PUT", "/sync/$c", SyncEngine.toJson(push))
                    if (pc != 200) return@withContext "HTTP $pc"
                    sent += push.size
                }
            }
            "ok:$received:$sent" + if (skipped) ":skipped" else ""
        } catch (e: Exception) { e.message ?: e.javaClass.simpleName }
    }

    /** « Envoyer vers » : transmet la lecture en cours ici à la cible puis arrête la lecture locale. Retourne un message d'erreur ou null. */
    suspend fun sendCurrentTo(t: KnownTarget): String? {
        val s = vm.player.state.value
        val r = s.request ?: return "Nothing is playing"
        val err = withContext(Dispatchers.IO) {
            sendTo(t, buildJsonObject {
                put("type", "play")
                putJsonObject("source") {
                    put("url", r.url); put("title", r.title)
                    put("kind", when (r.kind) { Kind.LIVE -> "live"; Kind.MOVIE -> "movie"; Kind.SERIES -> "series" })
                    r.logo?.let { put("logo", it) }
                    putJsonObject("ref") { put("accountId", r.accountId); put("itemId", r.itemId); r.episodeId?.let { put("episodeId", it) } }
                }
                if (r.kind != Kind.LIVE && s.positionMs > 10_000) put("startMs", s.positionMs)
            })
        }
        if (err == null) vm.closePlayer()
        return err
    }

    // ------------------------------------------------------------------ Stockage chiffré (jetons)

    private fun saveKnown(list: List<KnownTarget>) {
        _known.value = list
        runCatching {
            val c = Cipher.getInstance("AES/GCM/NoPadding").apply { init(Cipher.ENCRYPT_MODE, key()) }
            file.writeBytes(c.iv + c.doFinal(json.encodeToString(serializer, list).toByteArray()))
        }
    }

    private fun loadKnown(): List<KnownTarget> = try {
        if (!file.exists()) emptyList() else {
            val d = file.readBytes()
            val c = Cipher.getInstance("AES/GCM/NoPadding").apply { init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(128, d, 0, 12)) }
            json.decodeFromString(serializer, String(c.doFinal(d, 12, d.size - 12)))
        }
    } catch (e: Exception) { emptyList() }

    private fun key(): SecretKey {
        val ks = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (ks.getKey("iptvplayer_targets", null) as? SecretKey)?.let { return it }
        return KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore").apply {
            init(KeyGenParameterSpec.Builder("iptvplayer_targets", KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).setKeySize(256).build())
        }.generateKey()
    }
}
